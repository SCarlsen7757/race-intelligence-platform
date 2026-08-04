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
/// is the same accepted Phase 1 gap documented on <c>Buffering.ChannelTelemetryBuffer</c>: an
/// outage that outlasts what retries and buffering can absorb loses data, but always via a logged
/// error, never silently.
/// </para>
/// </remarks>
public sealed class TelemetryCollectorService(
    ITelemetrySource telemetrySource,
    ITelemetryBuffer buffer,
    IIngestClient ingestClient,
    ILogger<TelemetryCollectorService> logger) : BackgroundService
{
    /// <summary>How long to wait for the buffer to drain before PATCHing a session's end time regardless.</summary>
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan FlushPollInterval = TimeSpan.FromMilliseconds(50);

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
            // Signals TelemetryUploadService to flush and stop once it has drained what remains.
            buffer.Complete();
        }
    }

    private Task HandleEventAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken) => telemetryEvent switch
    {
        SessionStarted started => HandleSessionStartedAsync(started, cancellationToken),
        TelemetrySampleReceived sample => HandleSampleReceived(sample),
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

    private Task HandleSampleReceived(TelemetrySampleReceived sampleReceived)
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
        // log volume for the same event without adding information.
        _ = buffer.TryWrite(sample);

        return Task.CompletedTask;
    }

    private async Task HandleLapCompletedAsync(LapCompleted lapCompleted, CancellationToken cancellationToken)
    {
        var request = CollectorRequestMapper.ToLapCompletedRequest(lapCompleted.Lap);
        await ingestClient.RecordLapAsync(lapCompleted.Lap.SessionId, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleSessionEndedAsync(SessionEnded sessionEnded, CancellationToken cancellationToken)
    {
        await FlushBufferBestEffortAsync(sessionEnded.SessionId, cancellationToken).ConfigureAwait(false);

        var request = CollectorRequestMapper.ToSessionEndedRequest(sessionEnded.OccurredAtUtc);
        await ingestClient.UpdateSessionAsync(sessionEnded.SessionId, request, cancellationToken).ConfigureAwait(false);

        if (_currentSessionId == sessionEnded.SessionId)
        {
            _currentSessionId = null;
        }

        logger.LogInformation("Session {SessionId} ended.", sessionEnded.SessionId);
    }

    private Task HandleConnectionStateChanged(ConnectionStateChanged stateChanged)
    {
        logger.LogInformation(
            "Telemetry source connection state changed to {State}{Reason}.",
            stateChanged.State, stateChanged.Reason is null ? string.Empty : $": {stateChanged.Reason}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Best-effort wait for <see cref="ITelemetryBuffer.Metrics"/>'s <c>CurrentDepth</c> to reach
    /// zero before a session ends, so <see cref="Upload.TelemetryUploadService"/> has a chance to
    /// upload the session's tail samples before the session is marked ended server-side. Bounded by
    /// <see cref="FlushTimeout"/> — a session end must not be blocked forever by, e.g., an ingest
    /// API outage; if the buffer is still non-empty when the timeout elapses this logs a warning
    /// and proceeds with the PATCH anyway rather than hanging the collector.
    /// </summary>
    private async Task FlushBufferBestEffortAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + FlushTimeout;
        while (buffer.Metrics.CurrentDepth > 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(FlushPollInterval, cancellationToken).ConfigureAwait(false);
        }

        int remaining = buffer.Metrics.CurrentDepth;
        if (remaining > 0)
        {
            logger.LogWarning(
                "Buffer still holds {Depth} unflushed samples after waiting {Timeout} for session {SessionId} to end; " +
                "PATCHing the session end time anyway.",
                remaining, FlushTimeout, sessionId);
        }
    }
}
