using RaceIntelligence.Core.Analysis;
using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Sessions;

namespace RaceIntelligence.Analysis;

/// <summary>
/// Deterministic least-squares linear fit of lap time against lap number over a stint.
/// </summary>
/// <remarks>
/// <para>
/// The slope measures how lap time moved across the stint and nothing more. Tyre wear, fuel
/// burn-off, track evolution, driver learning and traffic are all folded into that one number and
/// this model cannot separate them, so the result must not be read as a wear estimate.
/// </para>
/// <para>
/// The slope is deliberately not clamped to non-negative values — a stint can genuinely get faster
/// as fuel burns off, and hiding that would misreport the trend.
/// </para>
/// <para>
/// <see cref="LapInfo.QualityScore"/> is ignored: it is computed later in the pipeline and is
/// normally <see langword="null"/> by the time a stint is analysed.
/// </para>
/// <para>
/// Requires <see cref="SimCapabilities.None"/>: it reads only <see cref="LapInfo.LapNumber"/> and
/// <see cref="LapInfo.LapTime"/>, canonical fields present regardless of simulator capability.
/// </para>
/// </remarks>
public sealed class LinearLapTimeTrendModel : IAnalysisAlgorithm<LapTimeTrendInput, LapTimeTrend>
{
    /// <summary>
    /// Below three laps the fit has no residual degrees of freedom (n − 2 ≤ 0), so the slope's
    /// standard error is undefined and no trend is reported at all.
    /// </summary>
    private const int MinimumLaps = 3;

    /// <inheritdoc />
    public string AlgorithmName => "Linear Lap-Time Trend";

    /// <inheritdoc />
    public Version AlgorithmVersion { get; } = new(1, 0);

    /// <inheritdoc />
    public SimCapabilities RequiredCapabilities => SimCapabilities.None;

    /// <inheritdoc />
    public LapTimeTrend Analyze(LapTimeTrendInput input)
    {
        List<LapInfo> laps = [.. input.Laps.Where(lap => lap.IsValid && lap.LapTime.HasValue)];
        int n = laps.Count;

        if (n < MinimumLaps)
        {
            return new LapTimeTrend(null, null, n);
        }

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

        // Every lap shares a lap number: the fit is a vertical line, so no slope exists. Reporting
        // zero here would claim a flat trend we have no basis for.
        if (xVariance == 0.0)
        {
            return new LapTimeTrend(null, null, n);
        }

        double slope = covariance / xVariance;
        double intercept = yMean - (slope * xMean);

        double residualSumOfSquares = 0.0;
        foreach (LapInfo lap in laps)
        {
            double residual = lap.LapTime!.Value.TotalSeconds - (intercept + (slope * lap.LapNumber));
            residualSumOfSquares += residual * residual;
        }

        // Textbook OLS slope standard error: sqrt(residual variance / lap-number variance), with
        // n - 2 degrees of freedom because the fit consumed two (slope and intercept). Identical
        // lap times give exactly zero residual scatter and therefore a standard error of zero.
        double standardError = Math.Sqrt(residualSumOfSquares / (n - 2) / xVariance);

        return new LapTimeTrend(slope, standardError, n);
    }
}
