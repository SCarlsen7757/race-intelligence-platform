using RaceIntelligence.Core.Telemetry;

namespace RaceIntelligence.Core.Buffering;

/// <summary>
/// A local queue that sits between an <see cref="ITelemetrySource"/> (or the collector consuming
/// it) and the background uploader, absorbing bursts and short network outages.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1 gap:</b> the initial implementation of this interface is in-memory only. It loses
/// all buffered data on a process crash, and it loses samples once <see cref="Metrics"/>'
/// <c>CurrentDepth</c> reaches capacity during a network outage longer than the buffer can
/// absorb. The README's collector requirement to "handle temporary network outages" and "resume
/// uploads automatically" is therefore only partially met by Phase 1: short outages are fully
/// covered, but an outage that outlasts the buffer, or a crash while samples are still queued, is
/// a known, accepted gap for the first release.
/// </para>
/// <para>
/// The shape of this interface deliberately mirrors <see cref="System.Threading.Channels.Channel{T}"/>
/// (<c>TryWrite</c>/<c>WaitToReadAsync</c>/<c>TryRead</c>/<c>Complete</c>) so that a future durable
/// implementation — e.g. backed by SQLite with write-ahead logging — can be substituted via
/// dependency injection with no change to producers or consumers of this interface.
/// </para>
/// </remarks>
public interface ITelemetryBuffer : IAsyncDisposable
{
    /// <summary>
    /// Attempts to enqueue a sample without blocking. Returns <see langword="false"/> if the
    /// buffer is full or has been completed, in which case the sample is dropped and
    /// <see cref="BufferMetrics.TotalDropped"/> is incremented.
    /// </summary>
    bool TryWrite(TelemetrySample sample);

    /// <summary>
    /// Waits until a sample is available to read, the buffer completes, or
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <returns><see langword="true"/> if a sample is available; <see langword="false"/> if the buffer has completed and is drained.</returns>
    ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Attempts to dequeue a sample without blocking.</summary>
    /// <param name="sample">The dequeued sample, if one was available.</param>
    /// <returns><see langword="true"/> if a sample was dequeued.</returns>
    bool TryRead(out TelemetrySample sample);

    /// <summary>Signals that no further samples will be written. Readers drain remaining samples and then stop.</summary>
    void Complete();

    /// <summary>Current throughput and health counters for this buffer.</summary>
    BufferMetrics Metrics { get; }
}
