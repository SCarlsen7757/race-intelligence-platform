using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceIntelligence.Collector.Abstractions;
using RaceIntelligence.Core.Telemetry;

namespace RaceIntelligence.Collector;

/// <summary>
/// The collect loop: reads <see cref="ITelemetrySource.ReadAllAsync"/> and dispatches each event to
/// whichever plugins consume it.
/// </summary>
/// <remarks>
/// <para>
/// This class performs <b>no analysis</b> and knows nothing about where telemetry goes. It reads and
/// dispatches; a plugin decides what that means. Adding a destination therefore changes nothing
/// here.
/// </para>
/// <para>
/// <b>Plugins are isolated from each other.</b> Every observer is invoked inside its own try/catch,
/// so an ingest API outage cannot stop the live view and a wedged hub cannot stop archiving. The
/// asynchronous observers are all <i>started</i> before any of them is awaited, which preserves the
/// ordering that matters: a plugin doing its work synchronously — the live outbox, which only swaps
/// a slot — completes before a plugin making an HTTP call has finished its first await.
/// </para>
/// <para>
/// <b>Must survive the simulator not running.</b> <see cref="ITelemetrySource"/> implementations
/// handle reconnect internally (see <c>RaceRoomTelemetrySource</c>'s state machine), so the only
/// failure this class defends against is an exception from handling one event. Everything is logged
/// and the loop continues, rather than letting an exception escape <see cref="ExecuteAsync"/> and
/// stop the worker.
/// </para>
/// </remarks>
public sealed class TelemetryCollectorService(
    ITelemetrySource telemetrySource,
    IEnumerable<ISessionObserver> sessionObservers,
    IEnumerable<ISampleObserver> sampleObservers,
    IEnumerable<IStandingsObserver> standingsObservers,
    IEnumerable<IExtrasObserver> extrasObservers,
    ILogger<TelemetryCollectorService> logger) : BackgroundService
{
    private readonly ISessionObserver[] _sessionObservers = [.. sessionObservers];
    private readonly ISampleObserver[] _sampleObservers = [.. sampleObservers];
    private readonly IStandingsObserver[] _standingsObservers = [.. standingsObservers];
    private readonly IExtrasObserver[] _extrasObservers = [.. extrasObservers];

    private Guid? _currentSessionId;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var telemetryEvent in telemetrySource.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await HandleEventAsync(telemetryEvent, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Failed to handle telemetry event {EventType}; continuing.", telemetryEvent.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            NotifySampleStreamCompleted();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Tells the sample observers the stream is over <i>before</i> waiting for
    /// <see cref="ExecuteAsync"/> to finish. An observer applying backpressure — the archive buffer
    /// in its default Wait mode, whose whole purpose is to fill up — can have this loop parked
    /// inside <see cref="ISampleObserver.OnSample"/> when shutdown begins, holding its own thread
    /// and so unable to ever observe <c>stoppingToken</c>. Signalling first unparks it, so shutdown
    /// finishes immediately instead of waiting out the host's ShutdownTimeout.
    /// </remarks>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        NotifySampleStreamCompleted();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task HandleEventAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken) => telemetryEvent switch
    {
        SessionStarted started => HandleSessionStartedAsync(started, cancellationToken),
        TelemetrySampleReceived sample => HandleSampleReceived(sample, cancellationToken),
        StandingsUpdated standings => HandleStandingsUpdated(standings),
        ExtrasUpdated extras => HandleExtrasUpdated(extras),
        LapCompleted lap => DispatchSessionAsync(
            observer => observer.OnLapCompletedAsync(lap.Lap, cancellationToken),
            nameof(ISessionObserver.OnLapCompletedAsync)),
        SessionEnded ended => HandleSessionEndedAsync(ended, cancellationToken),
        ConnectionStateChanged stateChanged => HandleConnectionStateChanged(stateChanged),
        _ => Task.CompletedTask,
    };

    private Task HandleSessionStartedAsync(SessionStarted started, CancellationToken cancellationToken)
    {
        _currentSessionId = started.Session.SessionId;

        logger.LogInformation(
            "Session {SessionId} started ({SessionType} at {Track}/{Layout}).",
            started.Session.SessionId, started.Session.SessionType, started.Session.TrackName, started.Session.LayoutName);

        return DispatchSessionAsync(
            observer => observer.OnSessionStartedAsync(started.Session, cancellationToken),
            nameof(ISessionObserver.OnSessionStartedAsync));
    }

    private Task HandleSessionEndedAsync(SessionEnded sessionEnded, CancellationToken cancellationToken)
    {
        if (_currentSessionId == sessionEnded.SessionId)
        {
            _currentSessionId = null;
        }

        logger.LogInformation("Session {SessionId} ended.", sessionEnded.SessionId);

        return DispatchSessionAsync(
            observer => observer.OnSessionEndedAsync(sessionEnded.SessionId, sessionEnded.OccurredAtUtc, cancellationToken),
            nameof(ISessionObserver.OnSessionEndedAsync));
    }

    private Task HandleSampleReceived(TelemetrySampleReceived sampleReceived, CancellationToken cancellationToken)
    {
        var sample = sampleReceived.Sample;

        if (_currentSessionId is { } currentSessionId && sample.SessionId != currentSessionId)
        {
            logger.LogWarning(
                "Received a telemetry sample for session {SampleSessionId} while tracking session {CurrentSessionId}.",
                sample.SessionId, currentSessionId);
        }

        foreach (var observer in _sampleObservers)
        {
            try
            {
                observer.OnSample(sample, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogObserverFailure(ex, observer, nameof(ISampleObserver.OnSample));
            }
        }

        return Task.CompletedTask;
    }

    private Task HandleStandingsUpdated(StandingsUpdated standingsUpdated)
    {
        foreach (var observer in _standingsObservers)
        {
            try
            {
                observer.OnStandings(standingsUpdated.Standings);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogObserverFailure(ex, observer, nameof(IStandingsObserver.OnStandings));
            }
        }

        return Task.CompletedTask;
    }

    private Task HandleExtrasUpdated(ExtrasUpdated extrasUpdated)
    {
        foreach (var observer in _extrasObservers)
        {
            try
            {
                observer.OnExtras(extrasUpdated.SessionId, extrasUpdated.ExtrasJson);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogObserverFailure(ex, observer, nameof(IExtrasObserver.OnExtras));
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Invokes every session observer, then awaits them all.
    /// </summary>
    /// <remarks>
    /// Started first and awaited afterwards, rather than awaited one at a time, so a plugin that is
    /// merely slow cannot delay a plugin that is not. Each observer's failure is caught and logged
    /// against that observer, so one throwing tells the others nothing.
    /// </remarks>
    private async Task DispatchSessionAsync(Func<ISessionObserver, ValueTask> invoke, string operation)
    {
        List<Task>? pending = null;

        foreach (var observer in _sessionObservers)
        {
            try
            {
                var call = invoke(observer);
                if (!call.IsCompletedSuccessfully)
                {
                    pending ??= new List<Task>(_sessionObservers.Length);
                    pending.Add(AwaitObserverAsync(call, observer, operation));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogObserverFailure(ex, observer, operation);
            }
        }

        if (pending is not null)
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
    }

    private async Task AwaitObserverAsync(ValueTask call, ISessionObserver observer, string operation)
    {
        try
        {
            await call.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogObserverFailure(ex, observer, operation);
        }
    }

    /// <remarks>Safe to call more than once: observers are required to tolerate it.</remarks>
    private void NotifySampleStreamCompleted()
    {
        foreach (var observer in _sampleObservers)
        {
            try
            {
                observer.OnSampleStreamCompleted();
            }
            catch (Exception ex)
            {
                LogObserverFailure(ex, observer, nameof(ISampleObserver.OnSampleStreamCompleted));
            }
        }
    }

    private void LogObserverFailure(Exception exception, object observer, string operation) =>
        logger.LogError(
            exception,
            "Plugin observer {Observer} failed during {Operation}; other plugins are unaffected.",
            observer.GetType().Name, operation);

    private Task HandleConnectionStateChanged(ConnectionStateChanged stateChanged)
    {
        logger.LogInformation(
            "Telemetry source connection state changed to {State}{Reason}.",
            stateChanged.State, stateChanged.Reason is null ? string.Empty : $": {stateChanged.Reason}");
        return Task.CompletedTask;
    }
}
