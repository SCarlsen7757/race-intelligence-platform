using RaceIntelligence.Collector.Mapping;
using RaceIntelligence.Collector.Upload;
using RaceIntelligence.Core.Buffering;
using RaceIntelligence.Core.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RaceIntelligence.Collector;

/// <summary>
/// Producer half of the collector: consumes <see cref="ITelemetrySource.ReadAllAsync"/> and turns
/// each event into either an ingest API call (session/lap bookkeeping) or a
/// <see cref="ITelemetryBuffer.TryWrite"/> (the hot-path telemetry samples themselves, which
/// <see cref="Upload.TelemetryUploadService"/> uploads separately).
/// </summary>
/// <remarks>
/// <para>
/// This class performs <b>no analysis</b>. It reads, converts (via <c>Mapping.CollectorRequestMapper</c>
/// and the shared <c>Ingest.Contracts.Mapping</c> mappers), buffers, and forwards — nothing here
/// computes a derived value.
/// </para>
/// <para>
/// <b>Must survive the simulator not running.</b> <see cref="ITelemetrySource"/> implementations
/// already handle reconnect internally (see e.g. <c>RaceRoomTelemetrySource</c>'s own state
/// machine), so the only failure this class needs to defend against is an exception from handling
/// a single event — most commonly the ingest API being unreachable. Every event is handled inside
/// a try/catch that logs and continues rather than letting the exception propagate out of
/// <see cref="ExecuteAsync"/>, which would stop the whole worker. A failed
/// <see cref="IIngestClient.CreateSessionAsync"/> call (after the platform's own HTTP resilience
/// policy has exhausted its retries) means the ingest API will not recognize that session for
/// subsequent lap/telemetry calls either — those will also fail, get logged, and be skipped. That
/// is the accepted Phase 1 gap documented on <see cref="ITelemetryBuffer"/>.
/// </para>
/// </remarks>
public sealed class TelemetryCollectorService(
    ITelemetrySource telemetrySource,
    ITelemetryBuffer buffer,
    IIngestClient ingestClient,
    OpenBatchTracker openBatch,
    TimeProvider timeProvider,
    ILogger<TelemetryCollectorService> logger) : BackgroundService
{
    /// <summary>How long to wait for the upload pipeline to drain before PATCHing a session's end time regardless.</summary>
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan FlushPollInterval = TimeSpan.FromMilliseconds(50);

    private Guid? _currentSessionId;

    /// <summary>
    /// The in-flight end-of-session completion (drain wait plus PATCH), kept off the poll loop.
    /// Successive session ends chain onto it so they stay ordered relative to each other.
    /// </summary>
    private Task _sessionEndCompletion = Task.CompletedTask;

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
            // Signals TelemetryUploadService to flush and stop once it has drained what remains.
            buffer.Complete();

            // The last session's PATCH runs off the poll loop; give it a chance to land before this
            // service reports itself stopped. It is already bounded by FlushTimeout and honours
            // stoppingToken, so this cannot extend shutdown indefinitely.
            try
            {
                await _sessionEndCompletion.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The final end-of-session update did not complete.");
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Completes the buffer <i>before</i> waiting for <see cref="ExecuteAsync"/> to finish. With
    /// <see cref="System.Threading.Channels.BoundedChannelFullMode.Wait"/> — the default, and the
    /// mode whose entire purpose is to produce a full buffer — this loop can be parked inside a
    /// blocking <see cref="ITelemetryBuffer.TryWrite"/> when shutdown begins, holding its own
    /// thread and so unable to ever observe <c>stoppingToken</c>. Completing first unparks it, so
    /// shutdown finishes immediately instead of waiting out the host's ShutdownTimeout.
    /// </remarks>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        buffer.Complete();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task HandleEventAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken) => telemetryEvent switch
    {
        SessionStarted started => HandleSessionStartedAsync(started, cancellationToken),
        TelemetrySampleReceived sample => HandleSampleReceived(sample, cancellationToken),
        LapCompleted lap => HandleLapCompletedAsync(lap, cancellationToken),
        SessionEnded ended => HandleSessionEndedAsync(ended, cancellationToken),
        ConnectionStateChanged stateChanged => HandleConnectionStateChanged(stateChanged),
        _ => Task.CompletedTask,
    };

    private async Task HandleSessionStartedAsync(SessionStarted started, CancellationToken cancellationToken)
    {
        _currentSessionId = started.Session.SessionId;

        var request = CollectorRequestMapper.ToSessionCreateRequest(started.Session);
        await ingestClient.CreateSessionAsync(request, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Session {SessionId} started ({SessionType} at {Track}/{Layout}).",
            started.Session.SessionId, started.Session.SessionType, started.Session.TrackName, started.Session.LayoutName);
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

        // The return value is intentionally ignored: ChannelTelemetryBuffer already logs and
        // counts a drop itself (see its TryWrite), so logging again here would just double the
        // log volume for the same event without adding information. The token matters — under
        // BufferFullMode.Wait this call parks the poll loop's thread until space frees, and it is
        // the only thing that lets shutdown unpark it.
        _ = buffer.TryWrite(sample, cancellationToken);

        return Task.CompletedTask;
    }

    private async Task HandleLapCompletedAsync(LapCompleted lapCompleted, CancellationToken cancellationToken)
    {
        var request = CollectorRequestMapper.ToLapCompletedRequest(lapCompleted.Lap);
        await ingestClient.RecordLapAsync(lapCompleted.Lap.SessionId, request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the end-of-session completion (wait for the upload pipeline to drain, then PATCH the
    /// end time) and returns immediately.
    /// </summary>
    /// <remarks>
    /// Deliberately does <b>not</b> await it: the drain wait is bounded by <see cref="FlushTimeout"/>,
    /// and awaiting it here would stall the poll loop for that long — during which the connector
    /// reports nothing, so a session started right after the previous one ended (restart a race,
    /// jump into the next session) would be noticed up to ten seconds late. Completions are chained
    /// onto each other so two sessions ending in quick succession are still PATCHed in order, and
    /// the last one is awaited in <see cref="ExecuteAsync"/>'s finally.
    /// </remarks>
    private Task HandleSessionEndedAsync(SessionEnded sessionEnded, CancellationToken cancellationToken)
    {
        if (_currentSessionId == sessionEnded.SessionId)
        {
            _currentSessionId = null;
        }

        var previous = _sessionEndCompletion;
        _sessionEndCompletion = CompleteSessionAsync(previous, sessionEnded, cancellationToken);
        return Task.CompletedTask;
    }

    private async Task CompleteSessionAsync(Task previousCompletion, SessionEnded sessionEnded, CancellationToken cancellationToken)
    {
        try
        {
            await previousCompletion.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A previous end-of-session update failed; continuing with session {SessionId}.", sessionEnded.SessionId);
        }

        try
        {
            await FlushPipelineBestEffortAsync(sessionEnded.SessionId, cancellationToken).ConfigureAwait(false);

            var request = CollectorRequestMapper.ToSessionEndedRequest(sessionEnded.OccurredAtUtc);
            await ingestClient.UpdateSessionAsync(sessionEnded.SessionId, request, cancellationToken).ConfigureAwait(false);

            logger.LogInformation("Session {SessionId} ended.", sessionEnded.SessionId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Shutdown interrupted the end-of-session update for session {SessionId}.", sessionEnded.SessionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record the end of session {SessionId}.", sessionEnded.SessionId);
        }
    }

    private Task HandleConnectionStateChanged(ConnectionStateChanged stateChanged)
    {
        logger.LogInformation(
            "Telemetry source connection state changed to {State}{Reason}.",
            stateChanged.State, stateChanged.Reason is null ? string.Empty : $": {stateChanged.Reason}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Best-effort wait for the whole upload pipeline to drain before a session is marked ended
    /// server-side, so <see cref="Upload.TelemetryUploadService"/> has a chance to upload the
    /// session's tail samples first.
    /// </summary>
    /// <remarks>
    /// "Drained" means the buffer is empty <i>and</i> the uploader's open batch is empty. Watching
    /// buffer depth alone is not enough: a sample leaves the buffer the instant the uploader reads
    /// it, so up to <see cref="CollectorOptions.MaxBatchSize"/> samples can be sitting in an
    /// un-uploaded batch while the buffer reports zero. Bounded by <see cref="FlushTimeout"/> — a
    /// session end must not be blocked forever by, e.g., an ingest API outage; if anything is still
    /// pending when the timeout elapses this logs a warning and proceeds with the PATCH anyway.
    /// Timing goes through <see cref="TimeProvider"/> rather than the wall clock so this is
    /// testable without real sleeps, and so a clock adjustment cannot skew the deadline.
    /// </remarks>
    private async Task FlushPipelineBestEffortAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var deadline = timeProvider.GetUtcNow() + FlushTimeout;
        while (PendingSampleCount() > 0 && timeProvider.GetUtcNow() < deadline)
        {
            await Task.Delay(FlushPollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        int remaining = PendingSampleCount();
        if (remaining > 0)
        {
            logger.LogWarning(
                "{Pending} samples were still unuploaded after waiting {Timeout} for session {SessionId} to end; " +
                "PATCHing the session end time anyway.",
                remaining, FlushTimeout, sessionId);
        }
    }

    /// <summary>Samples read from the source but not yet uploaded: queued in the buffer, plus the uploader's open batch.</summary>
    private int PendingSampleCount() => buffer.Metrics.CurrentDepth + openBatch.Count;
}
