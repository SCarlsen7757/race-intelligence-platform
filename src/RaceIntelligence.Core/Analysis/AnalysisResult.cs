namespace RaceIntelligence.Core.Analysis;

/// <summary>
/// The output of running a specific, versioned algorithm, tagged with what produced it and when.
/// </summary>
/// <remarks>
/// Analysis and strategy outputs are stored separately from raw telemetry, and every result
/// records the algorithm name and version that produced it. This makes it possible to compare
/// algorithms, roll back a poor version, benchmark improvements, and measure prediction accuracy
/// over time — without ever touching the immutable raw telemetry that produced the result.
/// </remarks>
/// <typeparam name="TOutput">The shape of the algorithm's output.</typeparam>
/// <param name="AlgorithmName">Stable name of the algorithm that produced this result.</param>
/// <param name="AlgorithmVersion">Version of the algorithm that produced this result.</param>
/// <param name="ComputedAtUtc">UTC time the result was computed.</param>
/// <param name="Output">The algorithm's output.</param>
public sealed record AnalysisResult<TOutput>(string AlgorithmName, Version AlgorithmVersion, DateTimeOffset ComputedAtUtc, TOutput Output);
