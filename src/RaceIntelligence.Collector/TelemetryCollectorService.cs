using RaceIntelligence.Collector.Live;
using RaceIntelligence.Collector.Mapping;
using RaceIntelligence.Collector.Upload;
using RaceIntelligence.Core.Buffering;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Core.Telemetry;
using RaceIntelligence.Live.Contracts;
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
    ILiveOutbox liveOutbox,
    OpenBatchTracker openBatch,
    TimeProvider timeProvider,
    ILogger<TelemetryCollectorService> logger) : BackgroundService
{
    /// <summary>How long to wait for the upload pipeline to drain before PATCHing a session's end time regardless.</summary>
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan FlushPollInterval = TimeSpan.FromMilliseconds(50);

    private Guid? _currentSessionId;

    /// <summary>
    /// The current session's driver identity, carried so live frames for the local car can say
    /// whose they are. A <see cref="TelemetrySample"/> describes a car; only the session knows the
    /// driver in it.
    /// </summary>
    private string? _currentSimDriverId;

    /// <summary>
    /// The roster fingerprint last announced to the hub. Tracked so the session is re-announced
    /// when the field changes materially — the hub matches clients into one room by roster overlap,
    /// and an announcement made before anybody had loaded in carries no roster at all.
    /// </summary>
    private string _announcedRosterFingerprint = string.Empty;

    /// <summary>The session as first announced, kept so a roster change can be re-announced against it.</summary>
    private SessionInfo? _currentSession;

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
        StandingsUpdated standings => HandleStandingsUpdated(standings),
        LapCompleted lap => HandleLapCompletedAsync(lap, cancellationToken),
        SessionEnded ended => HandleSessionEndedAsync(ended, cancellationToken),
        ConnectionStateChanged stateChanged => HandleConnectionStateChanged(stateChanged),
        _ => Task.CompletedTask,
    };

    private async Task HandleSessionStartedAsync(SessionStarted started, CancellationToken cancellationToken)
    {
        _currentSessionId = started.Session.SessionId;
        _currentSession = started.Session;
        _currentSimDriverId = started.Session.SimDriverId;
        _announcedRosterFingerprint = string.Empty;

        // Announced before the ingest call, and not conditional on it. The two paths are
        // independent by design: an ingest API that is down must not also take the live view with
        // it, and vice versa. This call cannot throw or block.
        liveOutbox.PublishSessionStarted(started.Session, _announcedRosterFingerprint, rosterSize: 0);

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

        // After the buffer, so a live path that somehow misbehaved could not delay the archive. The
        // outbox conflates rather than queues, so this neither blocks nor grows.
        liveOutbox.PublishSelf(sample, _currentSimDriverId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Forwards the observed view of the field to the live path. Nothing archives it — standings are
    /// live-only, so this event has no ingest counterpart.
    /// </summary>
    private Task HandleStandingsUpdated(StandingsUpdated standingsUpdated)
    {
        var standings = standingsUpdated.Standings;
        liveOutbox.PublishStandings(standings);

        // Re-announce the session when the field has changed enough to move the fingerprint. The
        // hub decides which clients belong in one room from roster overlap, and the announcement
        // made at session start was necessarily empty — nobody had loaded in yet.
        string fingerprint = LiveRosterFingerprint.Compute(standings.Drivers);
        if (!string.Equals(fingerprint, _announcedRosterFingerprint, StringComparison.Ordinal)
            && _currentSession is { } session)
        {
            _announcedRosterFingerprint = fingerprint;
            liveOutbox.PublishSessionStarted(session, fingerprint, standings.Drivers.Count);
        }

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
            _currentSession = null;
            _currentSimDriverId = null;
            _announcedRosterFingerprint = string.Empty;
        }

        // Immediately, not after the drain below: the drain exists so the archive's tail samples
        // land before the session is marked ended server-side, and the live view has no tail to
        // wait for. Telling the hub now is what stops a finished session sitting in the dashboard
        // until its room times out.
        liveOutbox.PublishSessionEnded(sessionEnded.SessionId, reason: null);

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
