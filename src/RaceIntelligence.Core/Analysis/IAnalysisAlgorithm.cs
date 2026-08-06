using RaceIntelligence.Core.Capabilities;

namespace RaceIntelligence.Core.Analysis;

/// <summary>
/// The non-generic identity and capability requirements of an analysis/strategy algorithm.
/// </summary>
/// <remarks>
/// Split out from <see cref="IAnalysisAlgorithm{TInput, TOutput}"/> so a caller can filter a
/// heterogeneous set of algorithms by what each needs without knowing their generic input/output
/// types.
/// </remarks>
public interface IAlgorithmMetadata
{
    /// <summary>Stable, human-readable name of the algorithm (e.g. "Linear Lap-Time Trend").</summary>
    string AlgorithmName { get; }

    /// <summary>Version of this algorithm implementation.</summary>
    Version AlgorithmVersion { get; }

    /// <summary>
    /// The capabilities a session must provide for this algorithm to be applicable. Callers check
    /// it with <see cref="SimCapabilitiesExtensions.Has"/> before running the algorithm.
    /// </summary>
    SimCapabilities RequiredCapabilities { get; }
}

/// <summary>
/// A single, versioned, capability-gated analysis or strategy algorithm.
/// </summary>
/// <typeparam name="TInput">The input the algorithm consumes (e.g. a set of laps or samples).</typeparam>
/// <typeparam name="TOutput">The result the algorithm produces.</typeparam>
public interface IAnalysisAlgorithm<in TInput, out TOutput> : IAlgorithmMetadata
{
    /// <summary>Runs the algorithm against <paramref name="input"/>.</summary>
    TOutput Analyze(TInput input);
}
