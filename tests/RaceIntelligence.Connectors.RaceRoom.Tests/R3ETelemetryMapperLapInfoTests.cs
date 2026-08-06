using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Connectors.RaceRoom.Tests.Support;
using RaceIntelligence.Core.Sessions;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests;

/// <summary>
/// Covers <see cref="R3ETelemetryMapper.ToLapInfo"/> — every lap ever recorded goes through it, and
/// each of its three sentinel branches (lap time, fuel, validity) was untested. The validity branch
/// in particular is the kind that fails silently: <c>prev_lap_valid</c> uses -1 for "not available"
/// and 0 for "invalid", and mapping N/A to "valid" or vice versa produces a database full of
/// plausible-looking laps that analysis must not trust.
/// </summary>
public class R3ETelemetryMapperLapInfoTests
{
    private static LapInfo MapLap(Action<R3ESharedRawBuilder> configure, int completedLapNumber = 1, bool snapshotDescribesThisLap = true)
    {
        var builder = new R3ESharedRawBuilder().InRaceSession("Lap Track", "Lap Layout");
        configure(builder);
        var raw = builder.Build();
        return R3ETelemetryMapper.ToLapInfo(in raw, Guid.NewGuid(), completedLapNumber, snapshotDescribesThisLap);
    }

    [Fact]
    public void SessionIdAndLapNumber_ArePassedThrough()
    {
        var raw = new R3ESharedRawBuilder().InRaceSession("Lap Track", "Lap Layout").Build();
        var sessionId = Guid.NewGuid();

        var lap = R3ETelemetryMapper.ToLapInfo(in raw, sessionId, completedLapNumber: 7);

        lap.SessionId.ShouldBe(sessionId);
        lap.LapNumber.ShouldBe(7);
    }

    [Fact]
    public void LapTime_NegativeSentinel_MapsToNull()
    {
        MapLap(b => b.WithPreviousLap(lapTimeSeconds: null, prevLapValid: 1)).LapTime.ShouldBeNull();
    }

    [Fact]
    public void LapTime_PositiveValue_MapsToSeconds()
    {
        MapLap(b => b.WithPreviousLap(92.25f, prevLapValid: 1)).LapTime.ShouldBe(TimeSpan.FromSeconds(92.25f));
    }

    [Fact]
    public void LapTime_Zero_MapsToZero_NotNull()
    {
        // 0 is not the N/A sentinel: only a negative value is. Conflating them would turn a
        // (nonsensical but real) zero reading into "no data" and vice versa.
        MapLap(b => b.WithPreviousLap(0f, prevLapValid: 1)).LapTime.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void FuelUsed_NegativeSentinel_MapsToNull()
    {
        MapLap(b => b.Configure((ref R3ESharedRaw raw) => raw.FuelPerLap = -1f)).FuelUsed.ShouldBeNull();
    }

    [Fact]
    public void FuelUsed_Zero_MapsToZero_NotNull()
    {
        MapLap(b => b.Configure((ref R3ESharedRaw raw) => raw.FuelPerLap = 0f)).FuelUsed.ShouldBe(0f);
    }

    [Fact]
    public void FuelUsed_PositiveValue_PassesThrough()
    {
        MapLap(b => b.Configure((ref R3ESharedRaw raw) => raw.FuelPerLap = 2.75f)).FuelUsed.ShouldBe(2.75f);
    }

    [Theory]
    [InlineData(1, true)] // valid
    [InlineData(0, false)] // explicitly invalid (cut, aborted)
    [InlineData(-1, false)] // not available -- must not be optimistically treated as valid
    public void IsValid_FollowsPrevLapValid_TreatingNotAvailableAsNotValid(int prevLapValid, bool expected)
    {
        MapLap(b => b.WithPreviousLap(90f, prevLapValid)).IsValid.ShouldBe(expected);
    }

    [Fact]
    public void AnalysisOnlyFields_AreNeverPopulatedByTheConnector()
    {
        var lap = MapLap(b => b.WithPreviousLap(90f, prevLapValid: 1));

        // Not exposed by the shared memory API, and the collector performs no analysis.
        lap.AverageSpeed.ShouldBeNull();
        lap.MaxSpeed.ShouldBeNull();
        lap.QualityScore.ShouldBeNull();
    }

    [Fact]
    public void ALapTheSnapshotDoesNotDescribe_ReportsUnknownTimingsRatherThanTheNewestLaps()
    {
        // lap_time_previous_self and prev_lap_valid always describe the most recently completed
        // lap. When a poll is missed and the counter jumps, copying them onto the skipped laps
        // would invent laps that were never driven that way -- permanently.
        var lap = MapLap(
            b => b.WithPreviousLap(90f, prevLapValid: 1).Configure((ref R3ESharedRaw raw) => raw.FuelPerLap = 2.5f),
            completedLapNumber: 2,
            snapshotDescribesThisLap: false);

        lap.LapNumber.ShouldBe(2);
        lap.LapTime.ShouldBeNull();
        lap.FuelUsed.ShouldBeNull();
        lap.IsValid.ShouldBeFalse("an unverifiable lap must not be presented to analysis as a valid one.");
    }
}
