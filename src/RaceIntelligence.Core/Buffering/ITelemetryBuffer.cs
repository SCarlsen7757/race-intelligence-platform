using RaceIntelligence.Core.Telemetry;

namespace RaceIntelligence.Core.Buffering;

/// <summary>
/// A local queue that sits between an <see cref="ITelemetrySource"/> (or the collector consuming
/// it) and the background uploader, absorbing bursts and short network outages.
/// </summary>
/// <remarks>
/// <para>
/// <b>Known gap:</b> the shipping implementation is in-memory only. Buffered samples are lost on
/// a process crash, and samples are dropped once capacity is reached during an outage longer than
/// the buffer can absorb. Short outages are covered; longer ones are not.
/// </para>
/// <para>
/// The shape deliberately mirrors <see cref="System.Threading.Channels.Channel{T}"/> so a durable
/// implementation — e.g. SQLite with write-ahead logging — can be substituted with no change to
/// producers or consumers.
/// </para>
/// </remarks>
public interface ITelemetryBuffer : IAsyncDisposable
{
    /// <summary>
    /// Attempts to enqueue a sample. Returns <see langword="false"/> if the sample was dropped, in
    /// which case <see cref="BufferMetrics.TotalDropped"/> is incremented.
    /// </summary>
    /// <param name="sample">The sample to enqueue.</param>
    /// <param name="cancellationToken">Unparks an implementation that is applying backpressure.</param>
    /// <remarks>
    /// Whether this blocks is the implementation's choice, and both answers are legitimate: an
    /// implementation that drops on a full buffer returns immediately, while one that applies real
    /// backpressure has no other way to do so from a synchronous method and will block the calling
    /// thread until space frees, the buffer completes, or <paramref name="cancellationToken"/> is
    /// cancelled. Callers on a loop that must stay responsive to shutdown are therefore expected to
    /// pass their stopping token — without it, a blocking implementation cannot be unparked by
    /// anything other than <see cref="Complete"/>.
    /// </remarks>
    bool TryWrite(TelemetrySample sample, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits until a sample is available to read, the buffer completes, or
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <returns><see langword="true"/> if a sample is available; <see langword="false"/> if the buffer has completed and is drained.</returns>
    ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Attempts to dequeue a sample without blocking.</summary>
    bool TryRead(out TelemetrySample sample);

    /// <summary>Signals that no further samples will be written. Readers drain remaining samples and then stop.</summary>
    void Complete();

    /// <summary>Current throughput and health counters for this buffer.</summary>
    BufferMetrics Metrics { get; }
}
