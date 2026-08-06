namespace RaceIntelligence.Core.Analysis;

/// <summary>
/// The output of running a specific, versioned algorithm, tagged with what produced it and when.
/// </summary>
/// <remarks>
/// An in-memory wrapper only. Nothing persists these yet — there is no table, entity or migration
/// for analysis results — so the algorithm name and version travel with the value for as long as
/// the process holds it and no longer. Storing them, and the version comparison and rollback that
/// would enable, is not built.
/// </remarks>
/// <typeparam name="TOutput">The shape of the algorithm's output.</typeparam>
/// <param name="AlgorithmName">Stable name of the algorithm that produced this result.</param>
/// <param name="AlgorithmVersion">Version of the algorithm that produced this result.</param>
/// <param name="ComputedAtUtc">UTC time the result was computed.</param>
/// <param name="Output">The algorithm's output.</param>
public sealed record AnalysisResult<TOutput>(string AlgorithmName, Version AlgorithmVersion, DateTimeOffset ComputedAtUtc, TOutput Output);
