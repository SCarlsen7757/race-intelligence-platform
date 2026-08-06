using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Sessions;
using Shouldly;

namespace RaceIntelligence.Analysis.Tests;

public class LinearLapTimeTrendModelTests
{
    private static readonly Guid SessionId = Guid.NewGuid();

    private static LapInfo Lap(int lapNumber, double? lapTimeSeconds, bool isValid = true, float? qualityScore = null) => new()
    {
        SessionId = SessionId,
        LapNumber = lapNumber,
        LapTime = lapTimeSeconds.HasValue ? TimeSpan.FromSeconds(lapTimeSeconds.Value) : null,
        IsValid = isValid,
        QualityScore = qualityScore,
    };

    [Fact]
    public void Metadata_MatchesSpec()
    {
        var model = new LinearLapTimeTrendModel();

        model.AlgorithmName.ShouldBe("Linear Lap-Time Trend");
        model.AlgorithmVersion.ShouldBe(new Version(1, 0));
        model.RequiredCapabilities.ShouldBe(SimCapabilities.None);
    }

    [Fact]
    public void Analyze_LinearlyIncreasingLapTimes_ReturnsKnownSlopeAndZeroStandardError()
    {
        var model = new LinearLapTimeTrendModel();
        var input = new LapTimeTrendInput(
        [
            Lap(1, 90.0),
            Lap(2, 90.5),
            Lap(3, 91.0),
            Lap(4, 91.5),
            Lap(5, 92.0),
        ]);

        var result = model.Analyze(input);

        result.LapTimeDeltaPerLap!.Value.ShouldBe(0.5, 0.0001);
        // Every point sits on the line, so the slope has no estimation spread at all.
        result.StandardError!.Value.ShouldBe(0.0, 1e-9);
        result.LapsUsed.ShouldBe(5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Analyze_FewerThanThreeQualifyingLaps_ReportsNoTrendRatherThanZero(int qualifyingLapCount)
    {
        var model = new LinearLapTimeTrendModel();
        var laps = Enumerable.Range(1, qualifyingLapCount).Select(n => Lap(n, 90.0 + n)).ToList();

        var result = model.Analyze(new LapTimeTrendInput(laps));

        result.LapTimeDeltaPerLap.ShouldBeNull();
        result.StandardError.ShouldBeNull();
        result.LapsUsed.ShouldBe(qualifyingLapCount);
    }

    [Fact]
    public void Analyze_ExactlyThreeQualifyingLaps_RunsRegressionRatherThanShortCircuiting()
    {
        var model = new LinearLapTimeTrendModel();
        var input = new LapTimeTrendInput(
        [
            Lap(1, 90.0),
            Lap(2, 90.5),
            Lap(3, 91.0),
        ]);

        var result = model.Analyze(input);

        result.LapTimeDeltaPerLap!.Value.ShouldBe(0.5, 0.0001);
        result.StandardError!.Value.ShouldBe(0.0, 1e-9);
        result.LapsUsed.ShouldBe(3);
    }

    [Fact]
    public void Analyze_InvalidAndMissingLapTimeLaps_AreExcludedFromRegression()
    {
        var model = new LinearLapTimeTrendModel();
        var input = new LapTimeTrendInput(
        [
            Lap(1, 90.0),
            Lap(2, 90.5),
            Lap(3, 91.0),
            Lap(4, 91.5),
            Lap(5, 92.0),
            Lap(6, 200.0, isValid: false),
            Lap(7, null),
        ]);

        var result = model.Analyze(input);

        result.LapTimeDeltaPerLap!.Value.ShouldBe(0.5, 0.0001);
        result.LapsUsed.ShouldBe(5);
    }

    [Fact]
    public void Analyze_NonNullQualityScore_DoesNotExcludeOrAlterTheLap()
    {
        var model = new LinearLapTimeTrendModel();
        var input = new LapTimeTrendInput(
        [
            Lap(1, 90.0, qualityScore: 0.9f),
            Lap(2, 90.5, qualityScore: 0.1f),
            Lap(3, 91.0, qualityScore: 0.0f),
            Lap(4, 91.5),
            Lap(5, 92.0, qualityScore: 0.5f),
        ]);

        var result = model.Analyze(input);

        result.LapTimeDeltaPerLap!.Value.ShouldBe(0.5, 0.0001);
        result.LapsUsed.ShouldBe(5);
    }

    [Fact]
    public void Analyze_IdenticalLapTimes_ReportsAZeroSlopeWithZeroStandardError()
    {
        // Nothing moved, and nothing scattered around the flat line. Zero slope with zero standard
        // error is the literal truth here — not a stand-in for "we don't know".
        var model = new LinearLapTimeTrendModel();
        var input = new LapTimeTrendInput(
        [
            Lap(1, 90.0),
            Lap(2, 90.0),
            Lap(3, 90.0),
            Lap(4, 90.0),
            Lap(5, 90.0),
        ]);

        var result = model.Analyze(input);

        result.LapTimeDeltaPerLap!.Value.ShouldBe(0.0, 1e-9);
        result.StandardError!.Value.ShouldBe(0.0, 1e-9);
        result.LapsUsed.ShouldBe(5);
    }

    [Fact]
    public void Analyze_IdenticalLapTimesAtNonRoundValue_IsNotDisturbedByFloatingPointNoise()
    {
        // 90.1s does not round-trip exactly through TimeSpan ticks / TotalSeconds the way 90.0s
        // does, so summing squared deviations of "identical" values leaves noise on the order of
        // 1e-27. The slope and its standard error must still come out at zero to within noise.
        var model = new LinearLapTimeTrendModel();
        var laps = Enumerable.Range(1, 7).Select(n => Lap(n, 90.1)).ToList();

        var result = model.Analyze(new LapTimeTrendInput(laps));

        result.LapTimeDeltaPerLap!.Value.ShouldBe(0.0, 1e-9);
        result.StandardError!.Value.ShouldBe(0.0, 1e-9);
        result.LapsUsed.ShouldBe(7);
    }

    [Fact]
    public void Analyze_DecreasingLapTimes_ReturnsNegativeSlopeUnclamped()
    {
        var model = new LinearLapTimeTrendModel();
        var input = new LapTimeTrendInput(
        [
            Lap(1, 90.0),
            Lap(2, 89.5),
            Lap(3, 89.0),
            Lap(4, 88.5),
            Lap(5, 88.0),
        ]);

        var result = model.Analyze(input);

        result.LapTimeDeltaPerLap!.Value.ShouldBe(-0.5, 0.0001);
    }

    [Fact]
    public void Analyze_NonContiguousLapNumbers_RegressesOnActualLapNumberNotListPosition()
    {
        // Lap numbers step by 5, lap time by 0.5 -> true slope is 0.1 per lap. An implementation
        // regressing on list index (0,1,2,3,4) instead of LapNumber would produce 0.5 instead.
        var model = new LinearLapTimeTrendModel();
        var input = new LapTimeTrendInput(
        [
            Lap(10, 90.0),
            Lap(15, 90.5),
            Lap(20, 91.0),
            Lap(25, 91.5),
            Lap(30, 92.0),
        ]);

        var result = model.Analyze(input);

        result.LapTimeDeltaPerLap!.Value.ShouldBe(0.1, 0.0001);
    }

    [Fact]
    public void Analyze_ScatteredLapTimes_ReturnsHandVerifiedSlopeAndStandardError()
    {
        // Hand-verified via independent OLS computation over x = 1..5, y = 90.0, 90.6, 90.9, 91.7,
        // 92.0. Mean y = 91.04, Sxx = 10, Sxy = 5.1 -> slope = 0.51. Total SS = 2.652 and the
        // slope explains 0.51 * 5.1 = 2.601 of it, so RSS = 0.051 and
        // SE = sqrt(0.051 / 3 / 10) = sqrt(0.0017) = 0.0412311.
        var model = new LinearLapTimeTrendModel();
        var input = new LapTimeTrendInput(
        [
            Lap(1, 90.0),
            Lap(2, 90.6),
            Lap(3, 90.9),
            Lap(4, 91.7),
            Lap(5, 92.0),
        ]);

        var result = model.Analyze(input);

        result.LapTimeDeltaPerLap!.Value.ShouldBe(0.51, 0.001);
        result.StandardError!.Value.ShouldBe(0.0412311, 0.00001);
    }

    [Fact]
    public void Analyze_MoreScatterAtTheSameSlope_WidensTheStandardError()
    {
        // The standard error must react to fit quality, which is the whole point of reporting it.
        var model = new LinearLapTimeTrendModel();
        var tight = model.Analyze(new LapTimeTrendInput(
        [
            Lap(1, 90.0),
            Lap(2, 90.6),
            Lap(3, 90.9),
            Lap(4, 91.7),
            Lap(5, 92.0),
        ]));
        var loose = model.Analyze(new LapTimeTrendInput(
        [
            Lap(1, 88.0),
            Lap(2, 92.6),
            Lap(3, 89.0),
            Lap(4, 93.7),
            Lap(5, 90.0),
        ]));

        loose.StandardError!.Value.ShouldBeGreaterThan(tight.StandardError!.Value);
    }

    [Fact]
    public void Analyze_AllQualifyingLapsShareTheSameLapNumber_ReportsNoTrendInsteadOfDividingByZero()
    {
        // No lap-number variance means the fit is a vertical line and no slope exists. Reporting
        // zero would claim a flat trend the data does not support.
        var model = new LinearLapTimeTrendModel();
        var input = new LapTimeTrendInput(
        [
            Lap(5, 89.0),
            Lap(5, 90.0),
            Lap(5, 91.0),
        ]);

        var result = model.Analyze(input);

        result.LapTimeDeltaPerLap.ShouldBeNull();
        result.StandardError.ShouldBeNull();
        result.LapsUsed.ShouldBe(3);
    }
}
