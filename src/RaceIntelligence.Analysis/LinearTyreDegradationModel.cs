using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Sessions;

namespace RaceIntelligence.Analysis;

/// <summary>
/// Deterministic tyre degradation model: least-squares linear fit of lap time against lap number.
/// </summary>
/// <remarks>
/// <para>
/// The slope of the fit is reported as degradation. It is deliberately <b>not</b> clamped to
/// non-negative values — a stint can genuinely get faster as fuel burns off, and hiding that would
/// misreport the trend the strategy layer needs.
/// </para>
/// <para>
/// <see cref="LapInfo.QualityScore"/> is ignored: it is computed later in the pipeline and is
/// normally <see langword="null"/> by the time a stint is analysed.
/// </para>
/// <para>
/// Requires <see cref="SimCapabilities.None"/>: it regresses only <see cref="LapInfo.LapNumber"/>
/// and <see cref="LapInfo.LapTime"/>, both canonical fields present on every session regardless of
/// simulator capability. A future model that actually reads per-lap tyre wear should declare
/// <see cref="SimCapabilities.TyreWear"/> itself; gating this one on it would just hide a valid
/// lap-time-only estimate from simulators that don't report wear.
/// </para>
/// </remarks>
public sealed class LinearTyreDegradationModel : ITyreDegradationModel
{
    /// <summary>Below this many usable laps a regression line says nothing meaningful.</summary>
    private const int MinimumLaps = 3;

    /// <summary>
    /// Denominator of the confidence formula's sample-size term, <c>(n - 2) / SampleSizeDivisor</c>.
    /// The term reaches 1.0 (saturates) at <c>n = 2 + SampleSizeDivisor</c> laps, i.e. n = 10.
    /// </summary>
    private const double SampleSizeDivisor = 8.0;

    /// <summary>
    /// Below this magnitude, the total sum of squares is treated as zero (all qualifying lap times
    /// effectively identical) rather than compared with exact equality. Lap times round-trip through
    /// <see cref="TimeSpan"/> ticks and <see cref="TimeSpan.TotalSeconds"/> division, so summing and
    /// averaging even bit-identical inputs can leave floating-point noise on the order of 1e-27 in
    /// the sum of squared deviations — comparing that to <c>0.0</c> exactly fails most of the time.
    /// This threshold sits many orders of magnitude above that noise floor and many orders below the
    /// smallest real difference two lap times can have (one tick, 100ns, contributing roughly 1e-14
    /// to the sum of squares), so it cannot mask genuine variation.
    /// </summary>
    private const double TotalSumOfSquaresEpsilon = 1e-15;

    /// <inheritdoc />
    public string AlgorithmName => "Tyre Degradation Model";

    /// <inheritdoc />
    public Version AlgorithmVersion { get; } = new(1, 0);

    /// <inheritdoc />
    public SimCapabilities RequiredCapabilities => SimCapabilities.None;

    /// <inheritdoc />
    /// <remarks>
    /// Confidence is <c>R² × min(1, (n − 2) / 8)</c>, clamped to 0..1: a good fit over few laps is
    /// still weak evidence, so fit quality is scaled by sample size, saturating at ten laps.
    /// </remarks>
    public TyreDegradationEstimate Analyze(StintAnalysisInput input)
    {
        List<LapInfo> laps = [.. input.Laps.Where(lap => lap.IsValid && lap.LapTime.HasValue)];

        if (laps.Count < MinimumLaps)
        {
            return new TyreDegradationEstimate(0.0, 0.0);
        }

        int n = laps.Count;
        double xMean = laps.Average(lap => (double)lap.LapNumber);
        double yMean = laps.Average(lap => lap.LapTime!.Value.TotalSeconds);

        double covariance = 0.0;
        double xVariance = 0.0;
        foreach (LapInfo lap in laps)
        {
            double dx = lap.LapNumber - xMean;
            covariance += dx * (lap.LapTime!.Value.TotalSeconds - yMean);
            xVariance += dx * dx;
        }

        // All laps share a lap number: no slope is defined, so report no trend.
        double slope = xVariance == 0.0 ? 0.0 : covariance / xVariance;
        double intercept = yMean - (slope * xMean);

        double residualSumOfSquares = 0.0;
        double totalSumOfSquares = 0.0;
        foreach (LapInfo lap in laps)
        {
            double y = lap.LapTime!.Value.TotalSeconds;
            double residual = y - (intercept + (slope * lap.LapNumber));
            residualSumOfSquares += residual * residual;
            totalSumOfSquares += (y - yMean) * (y - yMean);
        }

        // Identical lap times leave nothing to explain; treat that as a perfect fit rather than 0/0
        // (or than dividing out floating-point noise — see TotalSumOfSquaresEpsilon).
        double rSquared = totalSumOfSquares <= TotalSumOfSquaresEpsilon
            ? 1.0
            : 1.0 - (residualSumOfSquares / totalSumOfSquares);

        double confidence = Math.Clamp(
            rSquared * Math.Min(1.0, (n - 2) / SampleSizeDivisor),
            0.0,
            1.0);

        return new TyreDegradationEstimate(slope, confidence);
    }
}
