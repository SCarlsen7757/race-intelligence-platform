using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaceIntelligence.Core.Buffering;
using RaceIntelligence.Core.Telemetry;
using RaceIntelligence.Ingest.Contracts;
using RaceIntelligence.Ingest.Contracts.Mapping;
using RaceIntelligence.Ingest.Contracts.Telemetry;

namespace RaceIntelligence.Collector.Plugins.Ingest.Upload;

/// <summary>
/// Consumer half of the collector: drains <see cref="ITelemetryBuffer"/> and uploads samples to the
/// ingest API in batches, flushing a batch once it reaches <see cref="IngestOptions.MaxBatchSize"/>
/// samples <i>or</i> <see cref="IngestOptions.MaxBatchAge"/> has elapsed since the batch's first
/// sample — whichever happens first.
/// </summary>
/// <remarks>
/// <para>
/// Batching purely by size would let a slow-filling batch (light traffic, a session sitting in the
/// pits, a sim briefly running below its usual sample rate) sit unuploaded indefinitely. The age
/// bound guarantees every batch uploads within <see cref="IngestOptions.MaxBatchAge"/> of its
/// first sample regardless of how slowly samples arrive, so a slow session still uploads promptly.
/// </para>
/// <para>
/// A single uploaded batch never mixes samples from two sessions — the wire
/// <see cref="TelemetryBatchRequest"/> carries exactly one <see cref="TelemetryBatchRequest.SessionId"/>.
/// If a sample belonging to a different session is read while a batch is still open, the open batch
/// is flushed immediately (regardless of its size or age) before the new sample starts a fresh one.
/// </para>
/// <para>
/// Uses <see cref="TimeProvider"/> rather than <see cref="DateTimeOffset.UtcNow"/>/<see cref="Task.Delay(TimeSpan,CancellationToken)"/>
/// directly so the size-vs-age race is deterministically testable with a fake time provider instead
/// of real wall-clock sleeps.
/// </para>
/// <para>
/// <b>Known in-memory-buffer compromise:</b> uploads already retry, time out, and circuit-break via
/// the ingest <see cref="HttpClient"/>'s attached resilience handler (<c>AddStandardResilienceHandler</c>),
/// so <see cref="FlushAsync"/> itself does not hand-roll a second retry loop. If a batch still fails
/// after that policy is exhausted, the batch is logged at <see cref="LogLevel.Error"/> and discarded
/// — it is not re-queued into the buffer, both because the buffer may already be full again by the
/// time the failure is observed and because re-queuing would reorder it behind samples already read
/// after it. This is the accepted Phase 1 gap documented on <see cref="ITelemetryBuffer"/>: the loss
/// is always reported, never silent.
/// </para>
/// </remarks>
public sealed class TelemetryUploadService(
    ITelemetryBuffer buffer,
    IIngestClient ingestClient,
    IOptions<IngestOptions> options,
    OpenBatchTracker openBatch,
    TimeProvider timeProvider,
    ILogger<TelemetryUploadService> logger) : BackgroundService
{
    private enum WaitOutcome
    {
        DataAvailable,
        BatchAgeElapsed,
        BufferCompletedAndDrained,
    }

    /// <summary>Mutable state for the batch currently being assembled, threaded through one <see cref="ExecuteAsync"/> run.</summary>
    private sealed class OpenBatch(int maxBatchSize)
    {
        public List<TelemetrySample> Samples { get; } = new(maxBatchSize);

        public Guid SessionId { get; set; }

        public DateTimeOffset OpenedAt { get; set; }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ingestOptions = options.Value;
        var batch = new OpenBatch(ingestOptions.MaxBatchSize);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TimeSpan? maxWait = batch.Samples.Count > 0
                    ? batch.OpenedAt + ingestOptions.MaxBatchAge - timeProvider.GetUtcNow()
                    : null;

                var outcome = await WaitForNextAsync(maxWait, stoppingToken).ConfigureAwait(false);

                if (outcome == WaitOutcome.BatchAgeElapsed)
                {
                    await FlushAsync(batch, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (outcome == WaitOutcome.BufferCompletedAndDrained)
                {
                    break;
                }

                await DrainAvailableSamplesAsync(batch, ingestOptions.MaxBatchSize, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            if (batch.Samples.Count > 0)
            {
                // Best-effort final flush on shutdown: stoppingToken may already be cancelled, so
                // this deliberately uses CancellationToken.None to give the last batch a real
                // chance rather than failing it purely because shutdown was in progress.
                try
                {
                    await FlushAsync(batch, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to flush the final telemetry batch of {Count} samples for session {SessionId} during shutdown; these samples are lost.",
                        batch.Samples.Count, batch.SessionId);
                }
            }
        }
    }

    /// <summary>
    /// Reads every sample currently available without waiting, appending each to
    /// <paramref name="batch"/>. Flushes immediately — mid-drain — whenever appending would violate
    /// a batching rule: the batch reaching <paramref name="maxBatchSize"/>, or a sample belonging to
    /// a different session than the batch already open (a single upload batch must never mix
    /// sessions).
    /// </summary>
    private async Task DrainAvailableSamplesAsync(OpenBatch batch, int maxBatchSize, CancellationToken cancellationToken)
    {
        while (buffer.TryRead(out var sample))
        {
            if (batch.Samples.Count > 0 && sample.SessionId != batch.SessionId)
            {
                await FlushAsync(batch, cancellationToken).ConfigureAwait(false);
            }

            if (batch.Samples.Count == 0)
            {
                batch.SessionId = sample.SessionId;
                batch.OpenedAt = timeProvider.GetUtcNow();
            }

            batch.Samples.Add(sample);
            openBatch.Set(batch.Samples.Count);

            if (batch.Samples.Count >= maxBatchSize)
            {
                await FlushAsync(batch, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Waits for either more data to become available, <paramref name="maxWait"/> to elapse, or the
    /// buffer to complete and fully drain — whichever happens first. A <see langword="null"/>
    /// <paramref name="maxWait"/> means "no open batch to age out," so this waits indefinitely for
    /// data.
    /// </summary>
    private async Task<WaitOutcome> WaitForNextAsync(TimeSpan? maxWait, CancellationToken cancellationToken)
    {
        if (maxWait is not { } wait)
        {
            bool dataAvailable = await buffer.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
            return dataAvailable ? WaitOutcome.DataAvailable : WaitOutcome.BufferCompletedAndDrained;
        }

        if (wait <= TimeSpan.Zero)
        {
            return WaitOutcome.BatchAgeElapsed;
        }

        var waitToReadTask = buffer.WaitToReadAsync(cancellationToken).AsTask();
        var delayTask = Task.Delay(wait, timeProvider, cancellationToken);

        var winner = await Task.WhenAny(waitToReadTask, delayTask).ConfigureAwait(false);
        if (winner == delayTask)
        {
            // waitToReadTask is deliberately left running rather than cancelled: the buffer is
            // configured with SingleReader:false specifically so an overlapping WaitToReadAsync
            // like this one is safe to abandon — it will simply complete on a later write and be
            // discarded, a no-op.
            return WaitOutcome.BatchAgeElapsed;
        }

        bool hasData = await waitToReadTask.ConfigureAwait(false);
        return hasData ? WaitOutcome.DataAvailable : WaitOutcome.BufferCompletedAndDrained;
    }

    /// <summary>Converts and uploads the currently open batch, then clears it. A no-op if it is empty.</summary>
    private async Task FlushAsync(OpenBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Samples.Count == 0)
        {
            return;
        }

        var sessionId = batch.SessionId;
        var samples = batch.Samples.Select(TelemetrySampleContractMapper.ToDto).ToList();
        var request = new TelemetryBatchRequest(
            SchemaVersion.Current, sessionId, samples[0].SequenceNumber, samples[^1].SequenceNumber, samples);

        try
        {
            var response = await ingestClient.UploadTelemetryBatchAsync(sessionId, request, cancellationToken).ConfigureAwait(false);
            logger.LogDebug(
                "Uploaded telemetry batch for session {SessionId}: {Accepted} accepted, {Duplicates} duplicates.",
                sessionId, response.Accepted, response.Duplicates);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to upload telemetry batch of {Count} samples (sequence {First}-{Last}) for session {SessionId} " +
                "after resilience retries were exhausted; this batch is discarded — see the known in-memory buffer gap.",
                batch.Samples.Count, samples[0].SequenceNumber, samples[^1].SequenceNumber, sessionId);
        }
        finally
        {
            batch.Samples.Clear();
            openBatch.Set(0);
        }
    }
}
