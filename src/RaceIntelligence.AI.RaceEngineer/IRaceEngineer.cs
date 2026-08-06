using RaceIntelligence.Strategy;

namespace RaceIntelligence.AI.RaceEngineer;

/// <summary>
/// Turns a strategy recommendation into a spoken-style explanation for the driver.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not implemented.</b>
/// </para>
/// <para>
/// The critical design constraint: the AI <b>does not calculate telemetry</b>. Every number it
/// mentions comes from <see cref="PitRecommendation"/>, so it can only speak about what the
/// deterministic engines actually computed. It has no traffic, weather or telemetry input of its
/// own and must not imply otherwise.
/// </para>
/// <para>
/// Example output: <i>"Box on lap 18 rather than now — that's worth about 1.8 seconds."</i>
/// </para>
/// </remarks>
public interface IRaceEngineer
{
    /// <summary>Explains a recommendation in natural language, without recomputing it.</summary>
    /// <param name="recommendation">The strategy engine's output. Treated as fact, never recalculated.</param>
    Task<string> ExplainAsync(PitRecommendation recommendation, CancellationToken cancellationToken = default);
}
