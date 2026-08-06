using RaceIntelligence.Core.Sessions;

namespace RaceIntelligence.Analysis;

/// <summary>
/// The completed laps of a single stint, in lap order.
/// </summary>
/// <remarks>
/// Not validated: the caller is responsible for supplying laps that belong to one session and one
/// stint. Laps with duplicate lap numbers are accepted and regressed as-is — repeated x values are
/// legitimate OLS input, and the model already handles the case where they leave no lap-number
/// variance at all.
/// </remarks>
/// <param name="Laps">Completed laps in the stint, in lap order.</param>
public sealed record LapTimeTrendInput(IReadOnlyList<LapInfo> Laps);

/// <summary>
/// The ordinary-least-squares trend of lap time against lap number over a stint.
/// </summary>
/// <remarks>
/// This is a lap-time trend, <b>not</b> a tyre wear measurement: the slope also absorbs fuel
/// burn-off, track evolution, driver learning and traffic, none of which this model separates out.
/// </remarks>
/// <param name="LapTimeDeltaPerLap">
/// Fitted change in lap time per lap, in seconds. Positive means laps are getting slower.
/// <see langword="null"/> when no slope could be estimated — see <see cref="LapsUsed"/>.
/// </param>
/// <param name="StandardError">
/// Standard error of <paramref name="LapTimeDeltaPerLap"/>, in seconds per lap: the spread of the
/// slope estimate itself. Roughly, the true slope lies within about two standard errors of the
/// estimate. Zero when every lap sits exactly on the fitted line. <see langword="null"/> whenever
/// <paramref name="LapTimeDeltaPerLap"/> is.
/// </param>
/// <param name="LapsUsed">
/// How many laps the fit actually used (valid laps with a recorded lap time). Fewer than three
/// leaves no residual degrees of freedom, so no slope or standard error is reported.
/// </param>
public sealed record LapTimeTrend(double? LapTimeDeltaPerLap, double? StandardError, int LapsUsed);
