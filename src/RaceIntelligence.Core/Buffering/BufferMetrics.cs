namespace RaceIntelligence.Core.Buffering;

/// <summary>
/// Point-in-time counters describing an <see cref="ITelemetryBuffer"/>'s throughput and health.
/// </summary>
/// <param name="TotalWritten">Total number of samples successfully written since the buffer was created.</param>
/// <param name="TotalRead">Total number of samples successfully read since the buffer was created.</param>
/// <param name="TotalDropped">Total number of samples that could not be written (e.g. buffer full) and were discarded.</param>
/// <param name="CurrentDepth">Number of samples currently sitting in the buffer, awaiting read.</param>
public sealed record BufferMetrics(long TotalWritten, long TotalRead, long TotalDropped, int CurrentDepth);
