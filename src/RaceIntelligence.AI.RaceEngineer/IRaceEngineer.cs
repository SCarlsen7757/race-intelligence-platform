using RaceIntelligence.Strategy;

namespace RaceIntelligence.AI.RaceEngineer;

/// <summary>
/// Turns a strategy recommendation into a spoken-style explanation for the driver.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 4+ — not yet implemented.</b>
/// </para>
/// <para>
/// The critical design constraint: the AI <b>does not calculate telemetry</b>. Every number it
/// mentions is produced by the analysis and strategy engines and arrives in
/// <see cref="PitRecommendation"/>. The AI's only job is reasoning about and communicating those
/// numbers — which keeps the calculations deterministic, testable and versioned, and confines the
/// AI to the part it is actually good at.
/// </para>
/// <para>
/// Example output: <i>"Stay out for two more laps. Tyre degradation is still acceptable, and
/// pitting now would rejoin behind slower traffic. Waiting until lap 18 is estimated to gain
/// 1.8 seconds."</i>
/// </para>
/// </remarks>
public interface IRaceEngineer
{
    /// <summary>Explains a recommendation in natural language, without recomputing it.</summary>
    /// <param name="recommendation">The strategy engine's output. Treated as fact, never recalculated.</param>
    /// <param name="cancellationToken">Cancels generation.</param>
    Task<string> ExplainAsync(PitRecommendation recommendation, CancellationToken cancellationToken = default);
}
