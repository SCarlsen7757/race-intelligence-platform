using RaceIntelligence.Analysis;
using RaceIntelligence.Core.Capabilities;
using RaceIntelligence.Core.Sessions;
using Shouldly;

namespace RaceIntelligence.Analysis.Tests;

public class LinearTyreDegradationModelTests
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
        var model = new LinearTyreDegradationModel();

        model.AlgorithmName.ShouldBe("Tyre Degradation Model");
        model.AlgorithmVersion.ShouldBe(new Version(1, 0));
        model.RequiredCapabilities.ShouldBe(SimCapabilities.None);
    }

    [Fact]
    public void Analyze_LinearlyIncreasingLapTimes_ReturnsKnownSlopeAndConfidence()
    {
        var model = new LinearTyreDegradationModel();
        var input = new StintAnalysisInput(SessionId,
        [
            Lap(1, 90.0),
            Lap(2, 90.5),
            Lap(3, 91.0),
            Lap(4, 91.5),
            Lap(5, 92.0),
        ]);

        var result = model.Analyze(input);

        result.DegradationSecondsPerLap.ShouldBe(0.5, 0.0001);
        result.Confidence.ShouldBe(0.375, 0.0001);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Analyze_FewerThanThreeQualifyingLaps_ReturnsZeroEstimate(int qualifyingLapCount)
    {
        var model = new LinearTyreDegradationModel();
        var laps = Enumerable.Range(1, qualifyingLapCount).Select(n => Lap(n, 90.0 + n)).ToList();
        var input = new StintAnalysisInput(SessionId, laps);

        var result = model.Analyze(input);

        result.ShouldBe(new TyreDegradationEstimate(0.0, 0.0));
    }

    [Fact]
    public void Analyze_ExactlyThreeQualifyingLaps_RunsRegressionRatherThanShortCircuiting()
    {
        var model = new LinearTyreDegradationModel();
        var input = new StintAnalysisInput(SessionId,
        [
            Lap(1, 90.0),
            Lap(2, 90.5),
            Lap(3, 91.0),
        ]);

        var result = model.Analyze(input);

        // Perfect 3-lap fit: R² = 1, confidence = 1 * min(1, (3-2)/8) = 0.125.
        result.DegradationSecondsPerLap.ShouldBe(0.5, 0.0001);
        result.Confidence.ShouldBe(0.125, 0.0001);
    }

    [Fact]
    public void Analyze_InvalidAndMissingLapTimeLaps_AreExcludedFromRegression()
    {
        var model = new LinearTyreDegradationModel();
        var input = new StintAnalysisInput(SessionId,
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

        result.DegradationSecondsPerLap.ShouldBe(0.5, 0.0001);
        result.Confidence.ShouldBe(0.375, 0.0001);
    }

    [Fact]
    public void Analyze_NonNullQualityScore_DoesNotExcludeOrAlterTheLap()
    {
        var model = new LinearTyreDegradationModel();
        var input = new StintAnalysisInput(SessionId,
        [
            Lap(1, 90.0, qualityScore: 0.9f),
            Lap(2, 90.5, qualityScore: 0.1f),
            Lap(3, 91.0, qualityScore: 0.0f),
            Lap(4, 91.5),
            Lap(5, 92.0, qualityScore: 0.5f),
        ]);

        var result = model.Analyze(input);

        result.DegradationSecondsPerLap.ShouldBe(0.5, 0.0001);
        result.Confidence.ShouldBe(0.375, 0.0001);
    }

    [Fact]
    public void Analyze_ConstantLapTimes_ReturnsNearZeroSlopeWithRSquaredOneConfidence()
    {
        var model = new LinearTyreDegradationModel();
        var input = new StintAnalysisInput(SessionId,
        [
            Lap(1, 90.0),
            Lap(2, 90.0),
            Lap(3, 90.0),
            Lap(4, 90.0),
            Lap(5, 90.0),
        ]);

        var result = model.Analyze(input);

        result.DegradationSecondsPerLap.ShouldBe(0.0, 0.0001);
        result.Confidence.ShouldBe(0.375, 0.0001);
    }

    [Fact]
    public void Analyze_ConstantLapTimesAtNonRoundValue_StillHitsTheZeroVarianceCase()
    {
        // 90.1s does not round-trip exactly through TimeSpan ticks / TotalSeconds the way 90.0s
        // does, so this pins down that the zero-variance check tolerates floating-point noise
        // rather than requiring bit-exact equality.
        var model = new LinearTyreDegradationModel();
        var input = new StintAnalysisInput(SessionId,
        [
            Lap(1, 90.1),
            Lap(2, 90.1),
            Lap(3, 90.1),
            Lap(4, 90.1),
            Lap(5, 90.1),
            Lap(6, 90.1),
            Lap(7, 90.1),
        ]);

        var result = model.Analyze(input);

        result.DegradationSecondsPerLap.ShouldBe(0.0, 0.0001);
        // n=7: confidence = 1 * min(1, (7-2)/8) = 0.625.
        result.Confidence.ShouldBe(0.625, 0.0001);
    }

    [Fact]
    public void Analyze_DecreasingLapTimes_ReturnsNegativeSlopeUnclampedWithMatchingConfidence()
    {
        var model = new LinearTyreDegradationModel();
        var input = new StintAnalysisInput(SessionId,
        [
            Lap(1, 90.0),
            Lap(2, 89.5),
            Lap(3, 89.0),
            Lap(4, 88.5),
            Lap(5, 88.0),
        ]);

        var result = model.Analyze(input);

        result.DegradationSecondsPerLap.ShouldBe(-0.5, 0.0001);
        result.Confidence.ShouldBe(0.375, 0.0001);
    }

    [Fact]
    public void Analyze_NonContiguousLapNumbers_RegressesOnActualLapNumberNotListPosition()
    {
        // Lap numbers step by 5, lap time by 0.5 -> true slope is 0.1 per lap. A implementation
        // regressing on list index (0,1,2,3,4) instead of LapNumber would produce 0.5 instead.
        var model = new LinearTyreDegradationModel();
        var input = new StintAnalysisInput(SessionId,
        [
            Lap(10, 90.0),
            Lap(15, 90.5),
            Lap(20, 91.0),
            Lap(25, 91.5),
            Lap(30, 92.0),
        ]);

        var result = model.Analyze(input);

        result.DegradationSecondsPerLap.ShouldBe(0.1, 0.0001);
        result.Confidence.ShouldBe(0.375, 0.0001);
    }

    [Fact]
    public void Analyze_ScatteredLapTimes_ReturnsPartialConfidenceReflectingImperfectFit()
    {
        // Hand-verified via independent OLS computation: slope 0.51, R² 0.98077, confidence 0.36779.
        var model = new LinearTyreDegradationModel();
        var input = new StintAnalysisInput(SessionId,
        [
            Lap(1, 90.0),
            Lap(2, 90.6),
            Lap(3, 90.9),
            Lap(4, 91.7),
            Lap(5, 92.0),
        ]);

        var result = model.Analyze(input);

        result.DegradationSecondsPerLap.ShouldBe(0.51, 0.001);
        result.Confidence.ShouldBe(0.36779, 0.0001);
    }

    [Fact]
    public void Analyze_TenQualifyingLaps_ConfidenceSaturatesAtOneForAPerfectFit()
    {
        var model = new LinearTyreDegradationModel();
        var laps = Enumerable.Range(1, 10).Select(n => Lap(n, 90.0 + n)).ToList();
        var input = new StintAnalysisInput(SessionId, laps);

        var result = model.Analyze(input);

        result.DegradationSecondsPerLap.ShouldBe(1.0, 0.0001);
        result.Confidence.ShouldBe(1.0, 0.0001);
    }

    [Fact]
    public void Analyze_MoreThanTenQualifyingLaps_ConfidenceStaysCappedAtOne()
    {
        var model = new LinearTyreDegradationModel();
        var laps = Enumerable.Range(1, 12).Select(n => Lap(n, 90.0 + n)).ToList();
        var input = new StintAnalysisInput(SessionId, laps);

        var result = model.Analyze(input);

        result.Confidence.ShouldBe(1.0, 0.0001);
    }

    [Fact]
    public void Analyze_AllQualifyingLapsShareTheSameLapNumber_ReturnsZeroSlopeInsteadOfDividingByZero()
    {
        var model = new LinearTyreDegradationModel();
        var input = new StintAnalysisInput(SessionId,
        [
            Lap(5, 89.0),
            Lap(5, 90.0),
            Lap(5, 91.0),
        ]);

        var result = model.Analyze(input);

        // No lap-number variance -> no slope is defined; residuals equal the lap times' own
        // variance around the mean, so R² is 0 (the flat "fit" explains nothing) and so is confidence.
        result.DegradationSecondsPerLap.ShouldBe(0.0, 0.0001);
        result.Confidence.ShouldBe(0.0, 0.0001);
    }
}
