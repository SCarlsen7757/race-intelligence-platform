using RaceIntelligence.Collector.TestSupport;
using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Collector.Abstractions.Telemetry;
using RaceIntelligence.RaceRoom.Telemetry;

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
        RaceLength = new RaceLength
        {
            Laps = 30,
            DurationSeconds = 3600,
            Unit = RaceLengthUnit.Laps,
        },
    };

    public static RaceRoomTelemetrySample FullyPopulatedSample() => new()
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
        WheelSpeedFl = 1f,
        WheelSpeedFr = 2f,
        WheelSpeedRl = 3f,
        WheelSpeedRr = 4f,
        SuspensionTravelFl = 5f,
        SuspensionTravelFr = 6f,
        SuspensionTravelRl = 7f,
        SuspensionTravelRr = 8f,
        TyreTempFlInner = 80f,
        TyreTempFlMiddle = 81f,
        TyreTempFlOuter = 82f,
        TyreTempFrInner = 83f,
        TyreTempFrMiddle = 84f,
        TyreTempFrOuter = 85f,
        TyreTempRlInner = 86f,
        TyreTempRlMiddle = 87f,
        TyreTempRlOuter = 88f,
        TyreTempRrInner = 89f,
        TyreTempRrMiddle = 90f,
        TyreTempRrOuter = 91f,
        TyrePressureFl = 180f,
        TyrePressureFr = 181f,
        TyrePressureRl = 182f,
        TyrePressureRr = 183f,
        TyreWearFl = 0.1f,
        TyreWearFr = 0.2f,
        TyreWearRl = 0.3f,
        // The rear right is unreported throughout, so it can prove a missing corner stays missing
        // rather than arriving as a tyre with no wear or a brake that did nothing.
        TyreWearRr = null,
        BrakePressureFl = 3.1f,
        BrakePressureFr = 3.2f,
        BrakePressureRl = 1.4f,
        BrakePressureRr = null,
        TrackPositionFraction = 0.4242f,
        PushToPassEngaged = 1,
    };

    /// <summary>One operating window per corner. Shared with the collector suites.</summary>
    public static IReadOnlyList<OperatingWindow> OperatingWindows() => OperatingWindowFactory.Create();
}
