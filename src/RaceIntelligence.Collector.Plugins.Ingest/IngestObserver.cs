using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceIntelligence.Collector.Abstractions;
using RaceIntelligence.Collector.Plugins.Ingest.Mapping;
using RaceIntelligence.Collector.Plugins.Ingest.Upload;
using RaceIntelligence.Collector.Abstractions.Buffering;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Collector.Abstractions.Telemetry;
using RaceIntelligence.RaceRoom.Telemetry;

namespace RaceIntelligence.Collector.Plugins.Ingest;

/// <summary>
/// Feeds the archive: session and lap bookkeeping straight to the ingest API, samples into the
/// buffer that <see cref="Upload.TelemetryUploadService"/> drains.
/// </summary>
/// <remarks>
/// <para>
/// Session and lap calls are awaited on the collect loop. They are rare — one session start, one
/// end, one call a lap — and doing them inline keeps ordering obvious. A failure has already been
/// through the platform's HTTP resilience policy by the time it surfaces, and it is logged and
/// dropped by the caller rather than retried here.
/// </para>
/// <para>
/// The end of a session is the exception, and deliberately not awaited. It waits for the upload
/// pipeline to drain before marking the session ended server-side, which is bounded by
/// <see cref="FlushTimeout"/> — awaiting that on the collect loop would leave the connector
/// reporting nothing for up to ten seconds, so a session started right after the previous one ended
/// (restart a race, jump to the next session) would be noticed that late. Completions chain onto
/// each other instead, so two sessions ending in quick succession are still recorded in order.
/// </para>
/// </remarks>
public sealed class IngestObserver(
    ITelemetryBuffer buffer,
    IIngestClient ingestClient,
    OpenBatchTracker openBatch,
    LatestOperatingWindows operatingWindows,
    TimeProvider timeProvider,
    ILogger<IngestObserver> logger)
    : ISessionObserver, ISampleObserver, ISlowChannelObserver, IHostedService
{
    /// <summary>How long to wait for the upload pipeline to drain before recording a session's end time regardless.</summary>
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan FlushPollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// The in-flight end-of-session completion (drain wait plus update), kept off the collect loop.
    /// Successive session ends chain onto it so they stay ordered relative to each other.
    /// </summary>
    private Task _sessionEndCompletion = Task.CompletedTask;

    /// <inheritdoc />
    /// <remarks>Nothing to start: the observer is driven entirely by the collect loop.</remarks>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    /// <remarks>
    /// Waits for the last session's drain-and-update to land. This is registered before the collect
    /// loop, and the host stops hosted services in reverse registration order, so by the time this
    /// runs the loop has already stopped and no further session ends can be queued behind it.
    /// The wait is already bounded by <see cref="FlushTimeout"/> and honours the token, so it cannot
    /// extend shutdown indefinitely.
    /// </remarks>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _sessionEndCompletion.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The final end-of-session update did not complete.");
        }
    }

    /// <inheritdoc />
    public async ValueTask OnSessionStartedAsync(SessionInfo session, CancellationToken cancellationToken)
    {
        var request = CollectorRequestMapper.ToSessionCreateRequest(session);
        await ingestClient.CreateSessionAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask OnLapCompletedAsync(LapInfo lap, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lap);

        var request = CollectorRequestMapper.ToLapCompletedRequest(lap);
        await ingestClient.RecordLapAsync(lap.SessionId, request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask OnSessionEndedAsync(Guid sessionId, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
    {
        var previous = _sessionEndCompletion;
        _sessionEndCompletion = CompleteSessionAsync(previous, sessionId, occurredAtUtc, cancellationToken);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void OnSample(RaceRoomTelemetrySample sample, CancellationToken cancellationToken)
    {
        // The return value is intentionally ignored: ChannelTelemetryBuffer already logs and counts a
        // drop itself, so logging again here would double the log volume for the same event without
        // adding information. The token matters — under BufferFullMode.Wait this call parks the
        // collect loop until space frees, and it is one of the two things that can unpark it.
        _ = buffer.TryWrite(sample, cancellationToken);
    }

    /// <inheritdoc />
    public void OnSampleStreamCompleted() => buffer.Complete();

    /// <inheritdoc />
    /// <remarks>
    /// The archive stores the windows once per session and compound, not once per sample, so this
    /// only has to keep the latest set where the upload loop can find it. See
    /// <see cref="LatestOperatingWindows"/> for why every batch then carries them.
    /// </remarks>
    public TimeSpan SlowChannelInterval => TimeSpan.FromSeconds(1);

    /// <inheritdoc />
    public void OnSlowChannels(RaceRoomTelemetrySample sample, IReadOnlyList<OperatingWindow> windows) =>
        operatingWindows.Set(windows);

    private async Task CompleteSessionAsync(
        Task previousCompletion,
        Guid sessionId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await previousCompletion.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A previous end-of-session update failed; continuing with session {SessionId}.", sessionId);
        }

        try
        {
            await FlushPipelineBestEffortAsync(sessionId, cancellationToken).ConfigureAwait(false);

            var request = CollectorRequestMapper.ToSessionEndedRequest(occurredAtUtc);
            await ingestClient.UpdateSessionAsync(sessionId, request, cancellationToken).ConfigureAwait(false);

            logger.LogInformation("Session {SessionId} archived as ended.", sessionId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Shutdown interrupted the end-of-session update for session {SessionId}.", sessionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record the end of session {SessionId}.", sessionId);
        }
    }

    /// <summary>
    /// Best-effort wait for the whole upload pipeline to drain before a session is marked ended
    /// server-side, so <see cref="Upload.TelemetryUploadService"/> has a chance to upload the
    /// session's tail samples first.
    /// </summary>
    /// <remarks>
    /// "Drained" means the buffer is empty <i>and</i> the uploader's open batch is empty. Watching
    /// buffer depth alone is not enough: a sample leaves the buffer the instant the uploader reads
    /// it, so up to <see cref="IngestOptions.MaxBatchSize"/> samples can be sitting in an un-uploaded
    /// batch while the buffer reports zero. Bounded by <see cref="FlushTimeout"/> — a session end
    /// must not be blocked forever by, e.g., an ingest API outage. Timing goes through
    /// <see cref="TimeProvider"/> rather than the wall clock so this is testable without real sleeps,
    /// and so a clock adjustment cannot skew the deadline.
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
                "{Pending} samples were still unuploaded after waiting {Timeout} for session {SessionId} to end; "
                + "recording the session end time anyway.",
                remaining, FlushTimeout, sessionId);
        }
    }

    /// <summary>Samples read from the source but not yet uploaded: queued in the buffer, plus the uploader's open batch.</summary>
    private int PendingSampleCount() => buffer.Metrics.CurrentDepth + openBatch.Count;
}
