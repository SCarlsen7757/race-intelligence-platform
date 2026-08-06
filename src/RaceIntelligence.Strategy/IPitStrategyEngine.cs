using RaceIntelligence.Analysis;
using RaceIntelligence.Core.Analysis;

namespace RaceIntelligence.Strategy;

/// <summary>
/// Input to the strategy engine: the current race state plus the analysis results available for it.
/// </summary>
/// <param name="SessionId">The session being raced.</param>
/// <param name="CurrentLap">The lap currently being run.</param>
/// <param name="TotalLaps">Total race laps, when the race is lap-limited.</param>
/// <param name="LapTimeTrend">Lap-time trend over the current stint, when a model has produced one.</param>
public sealed record StrategyInput(
    Guid SessionId,
    int CurrentLap,
    int? TotalLaps,
    AnalysisResult<LapTimeTrend>? LapTimeTrend);

/// <summary>
/// A pit recommendation, including the expected time gained or lost by acting on it.
/// </summary>
/// <param name="ShouldPitNow">Whether pitting on this lap is the recommended action.</param>
/// <param name="RecommendedLap">The lap the engine recommends pitting on.</param>
/// <param name="ExpectedGainSeconds">Expected gain versus the next-best alternative; negative is a loss.</param>
/// <param name="Rationale">
/// Machine-readable reasoning inputs. The AI race engineer turns this into an explanation — it
/// never recomputes the numbers itself.
/// </param>
public sealed record PitRecommendation(
    bool ShouldPitNow,
    int RecommendedLap,
    double ExpectedGainSeconds,
    IReadOnlyDictionary<string, double> Rationale);

/// <summary>
/// A versioned, capability-gated pit strategy engine.
/// </summary>
/// <remarks>
/// <b>Phase 3 — not yet implemented.</b> Consumes analysis results rather than raw telemetry,
/// keeping the strategy layer independent of both the simulator and the storage schema.
/// </remarks>
public interface IPitStrategyEngine : IAnalysisAlgorithm<StrategyInput, PitRecommendation>;
