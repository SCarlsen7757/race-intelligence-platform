using RaceIntelligence.Collector.Abstractions.Buffering;
using RaceIntelligence.Collector.Abstractions.Telemetry;
using RaceIntelligence.RaceRoom.Telemetry;

namespace RaceIntelligence.Collector.Plugins.Ingest.Tests.Support;

/// <summary>
/// Decorates a real <see cref="ITelemetryBuffer"/> and appends a marker to a shared, ordered log on
/// every successful <see cref="TryWrite"/> — lets a test interleave buffer writes with
/// <see cref="RecordingIngestClient"/> calls on one timeline to assert cross-component ordering.
/// </summary>
internal sealed class LoggingTelemetryBuffer(ITelemetryBuffer inner, List<string> sharedLog) : ITelemetryBuffer
{
    public bool TryWrite(RaceRoomTelemetrySample sample, CancellationToken cancellationToken = default)
    {
        bool written = inner.TryWrite(sample, cancellationToken);
        if (written)
        {
            lock (sharedLog)
            {
                sharedLog.Add($"Sample:{sample.SequenceNumber}");
            }
        }

        return written;
    }

    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default) =>
        inner.WaitToReadAsync(cancellationToken);

    public bool TryRead(out RaceRoomTelemetrySample sample) => inner.TryRead(out sample);

    public void Complete() => inner.Complete();

    public BufferMetrics Metrics => inner.Metrics;

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
