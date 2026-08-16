using RaceIntelligence.Core.Buffering;
using RaceIntelligence.Core.Telemetry;

namespace RaceIntelligence.Collector.Buffering;

/// <summary>
/// The buffer used when archiving is switched off: writes are accepted and discarded, and there is
/// never anything to read.
/// </summary>
/// <remarks>
/// <para>
/// Discarding is the correct behaviour here, not a compromise. A publish-only collector has been
/// told not to store telemetry, so a sample that goes nowhere is the configured outcome rather than
/// a loss — which is why nothing here counts a drop. <see cref="Metrics"/> reports every sample as
/// written and none dropped, so an operator reading it does not see a fault that is not there.
/// </para>
/// <para>
/// <see cref="WaitToReadAsync"/> completes as <see langword="false"/> immediately rather than
/// blocking. No uploader is registered in this configuration, but a caller that did wait on it must
/// be told there will never be data rather than parked forever.
/// </para>
/// </remarks>
public sealed class NullTelemetryBuffer : ITelemetryBuffer
{
    private long _written;

    /// <inheritdoc />
    public bool TryWrite(TelemetrySample sample, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _written);
        return true;
    }

    /// <inheritdoc />
    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);

    /// <inheritdoc />
    public bool TryRead(out TelemetrySample sample)
    {
        sample = null!;
        return false;
    }

    /// <inheritdoc />
    public void Complete()
    {
    }

    /// <inheritdoc />
    public BufferMetrics Metrics
    {
        get
        {
            long written = Interlocked.Read(ref _written);
            return new BufferMetrics(written, TotalRead: written, TotalDropped: 0, CurrentDepth: 0);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
