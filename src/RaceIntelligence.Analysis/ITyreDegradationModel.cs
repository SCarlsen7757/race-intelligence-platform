using RaceIntelligence.Core.Analysis;
using RaceIntelligence.Core.Sessions;

namespace RaceIntelligence.Analysis;

/// <summary>
/// Input to a tyre degradation model: the completed laps of a single stint.
/// </summary>
/// <param name="SessionId">The session the stint belongs to.</param>
/// <param name="Laps">Completed laps in the stint, in lap order.</param>
public sealed record StintAnalysisInput(Guid SessionId, IReadOnlyList<LapInfo> Laps);

/// <summary>
/// Estimated tyre degradation over a stint, expressed as lap-time loss per lap.
/// </summary>
/// <param name="DegradationSecondsPerLap">Estimated lap-time loss attributable to tyre wear.</param>
/// <param name="Confidence">0..1 confidence in the estimate, given lap count and lap quality.</param>
public sealed record TyreDegradationEstimate(double DegradationSecondsPerLap, double Confidence);

/// <summary>
/// A versioned, capability-gated tyre degradation model.
/// </summary>
/// <remarks>
/// <para>
/// The Phase 2 deliverable. <see cref="LinearTyreDegradationModel"/> is the first implementation;
/// the interface exists separately so the dependency direction and the versioning/capability
/// contract stay visible regardless of which implementation is active.
/// </para>
/// <para>
/// Deliberately an <see cref="IAnalysisAlgorithm{TInput, TOutput}"/>: every implementation carries
/// its own <see cref="IAlgorithmMetadata.AlgorithmVersion"/> ("Tyre Model v1", "v2", ...) so results
/// can be compared, benchmarked and rolled back, and declares
/// <see cref="IAlgorithmMetadata.RequiredCapabilities"/> so it is simply skipped for a simulator that
/// does not expose tyre wear — rather than the caller branching on which simulator produced the data.
/// </para>
/// </remarks>
public interface ITyreDegradationModel : IAnalysisAlgorithm<StintAnalysisInput, TyreDegradationEstimate>;
