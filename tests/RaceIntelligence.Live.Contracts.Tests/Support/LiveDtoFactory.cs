using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Core.Telemetry;

namespace RaceIntelligence.Live.Contracts.Tests.Support;

/// <summary>
/// Builds fully-populated canonical objects for round-trip tests.
/// </summary>
/// <remarks>
/// Deliberately populates <b>every</b> field, including the optional ones. A round-trip test built
/// on a sparsely-populated object passes just as happily when a mapper forgets a field, because
/// <see langword="null"/> maps to <see langword="null"/> whether or not the line exists. The
/// distinctive values below are what make an omission show up as a failure.
/// </remarks>
internal static class LiveDtoFactory
{
    public static DriverStanding FullyPopulatedStanding(string simDriverId = "4242") => new()
    {
        SimDriverId = simDriverId,
        SlotId = 7,
        DisplayName = "Kimi Räikkönen",
        CarNumber = 11,
        SimCarId = "300",
        SimCarClassId = "400",
        SimManufacturerId = "500",
        Position = 3,
        PositionInClass = 2,
        CompletedLaps = 12,
        TrackPositionFraction = 0.4242f,
        Sector = 2,
        Speed = 58.5f,
        CurrentLapTime = TimeSpan.FromSeconds(45.25),
        PreviousLapTime = TimeSpan.FromSeconds(92.75),
        BestLapTime = TimeSpan.FromSeconds(91.125),
        CurrentLapValid = true,
        CurrentSectorTimes = [TimeSpan.FromSeconds(30.5), null, null],
        PreviousSectorTimes = [TimeSpan.FromSeconds(30.125), TimeSpan.FromSeconds(60.25), TimeSpan.FromSeconds(92.75)],
        BestSectorTimes = [TimeSpan.FromSeconds(29.875), TimeSpan.FromSeconds(59.5), TimeSpan.FromSeconds(91.125)],
        GapToCarAhead = TimeSpan.FromSeconds(1.25),
        GapToCarBehind = TimeSpan.FromSeconds(0.75),
        InPitLane = false,
        PitStopStatus = PitStopStatus.FourTyresUnserved,
        PitStopCount = 2,
        FinishStatus = DriverFinishStatus.None,
        PenaltyCount = 1,
    };

    /// <summary>
    /// A car that has reported nothing beyond its name — every optional field absent. Exercises the
    /// half of the mapping a populated object cannot: that <see langword="null"/> survives rather
    /// than becoming a confident zero.
    /// </summary>
    public static DriverStanding EmptyStanding() => new()
    {
        DisplayName = "Unknown",
        CompletedLaps = 0,
        PitStopStatus = PitStopStatus.Unavailable,
        FinishStatus = DriverFinishStatus.Unavailable,
    };

    public static SessionStandings FullyPopulatedStandings() => new()
    {
        SessionId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        CapturedAtUtc = new DateTimeOffset(2026, 8, 16, 12, 34, 56, 789, TimeSpan.Zero),
        SimulationTime = 123.5,
        LocalSimDriverId = "4242",
        Drivers = [FullyPopulatedStanding(), EmptyStanding()],
        PitWindow = new PitWindow
        {
            Status = PitWindowStatus.Open,
            Start = 12,
            End = 20,
            Unit = PitWindowUnit.Laps,
        },
    };

    public static TelemetrySample FullyPopulatedSample() => new()
    {
        SessionId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        SequenceNumber = 987,
        Timestamp = new DateTimeOffset(2026, 8, 16, 12, 34, 56, 789, TimeSpan.Zero),
        SimulationTime = 123.5,
        Speed = 58.5f,
        Throttle = 0.75f,
        Brake = 0.25f,
        Steering = -0.5f,
        Gear = 4,
        EngineRpm = 7200f,
        FuelLeft = 42.5f,
        LapNumber = 13,
        Sector = 2,
        Position = 3,
        WheelSpeed = new WheelData<float>(1f, 2f, 3f, 4f),
        SuspensionTravel = new WheelData<float>(5f, 6f, 7f, 8f),
        TyreTemperature = new WheelData<TyreTemperature>(
            new TyreTemperature(80f, 81f, 82f, 90f, 60f, 110f),
            new TyreTemperature(83f, 84f, 85f, 90f, 60f, 110f),
            new TyreTemperature(86f, 87f, 88f, 90f, 60f, 110f),
            new TyreTemperature(89f, 90f, 91f, 90f, 60f, 110f)),
        TyrePressure = new WheelData<float?>(180f, 181f, 182f, 183f),
        TyreWear = new WheelData<float?>(0.1f, 0.2f, 0.3f, null),
        TrackPositionFraction = 0.4242f,
        Extras = """{"pushToPass":{"engaged":1}}""",
    };
}
