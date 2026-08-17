using RaceIntelligence.Connectors.RaceRoom;
using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Connectors.RaceRoom.Tests.Support;
using RaceIntelligence.Core.Sessions;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests;

/// <summary>
/// Covers the translation of RaceRoom's per-car driver entry into <see cref="DriverStanding"/> —
/// in particular that the block's "-1 = N/A" sentinels become <see langword="null"/> rather than
/// confident, wrong numbers, since a timing tower showing "-1.0s behind" is worse than showing
/// nothing.
/// </summary>
public sealed class R3ETelemetryMapperStandingsTests
{
    [Fact]
    public void Maps_identity_position_and_lap_progress()
    {
        var driver = new R3EDriverDataBuilder()
            .WithName("Kimi")
            .WithUserId(4242)
            .WithSlotId(7)
            .WithCarNumber(11)
            .WithCarIds(modelId: 300, classId: 400, manufacturerId: 500)
            .WithPlace(place: 3, placeClass: 2)
            .WithLapProgress(completedLaps: 12, lapDistanceFraction: 0.42f, trackSector: 2)
            .WithSpeed(58.5f)
            .Build();

        var standing = R3ETelemetryMapper.ToDriverStanding(in driver, R3ESectorTimeConvention.Cumulative);

        standing.SimDriverId.ShouldBe("4242");
        standing.SlotId.ShouldBe(7);
        standing.DisplayName.ShouldBe("Kimi");
        standing.CarNumber.ShouldBe(11);
        standing.SimCarId.ShouldBe("300");
        standing.SimCarClassId.ShouldBe("400");
        standing.SimManufacturerId.ShouldBe("500");
        standing.Position.ShouldBe(3);
        standing.PositionInClass.ShouldBe(2);
        standing.CompletedLaps.ShouldBe(12);
        standing.TrackPositionFraction.ShouldBe(0.42f);
        standing.Sector.ShouldBe(2);
        standing.Speed.ShouldBe(58.5f);
    }

    [Fact]
    public void Maps_cumulative_sector_times_and_derives_lap_totals_from_the_final_split()
    {
        var driver = new R3EDriverDataBuilder()
            .WithName("Kimi")
            .WithCurrentLap(lapTimeSeconds: 45.0f, sector1: 30.5f, sector2: -1f, sector3: -1f)
            .WithPreviousLap(sector1: 30.1f, sector2: 60.4f, sector3: 92.7f)
            .WithBestLap(sector1: 29.9f, sector2: 59.8f, sector3: 91.2f)
            .Build();

        var standing = R3ETelemetryMapper.ToDriverStanding(in driver, R3ESectorTimeConvention.Cumulative);

        standing.CurrentLapTime.ShouldBe(TimeSpan.FromSeconds(45.0f));
        standing.CurrentSectorTimes.ShouldBe([TimeSpan.FromSeconds(30.5f), null, null]);

        // The driver array has no previous/best lap-time field; the lap total is its final
        // cumulative split by definition.
        standing.PreviousLapTime.ShouldBe(TimeSpan.FromSeconds(92.7f));
        standing.BestLapTime.ShouldBe(TimeSpan.FromSeconds(91.2f));
        standing.PreviousSectorTimes.Count.ShouldBe(3);
        standing.BestSectorTimes[2].ShouldBe(TimeSpan.FromSeconds(91.2f));
    }

    [Fact]
    public void Sentinel_fields_become_null_rather_than_negative_readings()
    {
        // The builder's defaults are all -1 (N/A), which is what a car that has not yet set a time
        // and has nobody ahead or behind actually reports.
        var driver = new R3EDriverDataBuilder().WithName("Rookie").Build();

        var standing = R3ETelemetryMapper.ToDriverStanding(in driver, R3ESectorTimeConvention.Cumulative);

        standing.SimDriverId.ShouldBeNull();
        standing.SlotId.ShouldBeNull();
        standing.CarNumber.ShouldBeNull();
        standing.Position.ShouldBeNull();
        standing.PositionInClass.ShouldBeNull();
        standing.TrackPositionFraction.ShouldBeNull();
        standing.Sector.ShouldBeNull();
        standing.Speed.ShouldBeNull();
        standing.CurrentLapTime.ShouldBeNull();
        standing.PreviousLapTime.ShouldBeNull();
        standing.BestLapTime.ShouldBeNull();
        standing.GapToCarAhead.ShouldBeNull();
        standing.GapToCarBehind.ShouldBeNull();
        standing.InPitLane.ShouldBeNull();
        standing.CurrentLapValid.ShouldBeNull();
        standing.PitStopCount.ShouldBeNull();
        standing.PitStopStatus.ShouldBe(PitStopStatus.Unavailable);
        standing.FinishStatus.ShouldBe(DriverFinishStatus.Unavailable);

        // r3e.h documents completed_laps as "-1 = n/a". A car in a slot that is not yet active
        // must not be recorded as the fact "has completed zero laps" — on a timing tower that is
        // the difference between a driver who is out and one who has not started.
        standing.CompletedLaps.ShouldBeNull();
    }

    /// <summary>
    /// A user id of 0 is RaceRoom's "offline / not signed in", not an account. Passing it through
    /// would make every unauthenticated driver in a session share one identity — and identity is
    /// what views from several machines are matched on.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_user_id_is_not_an_identity(int userId)
    {
        var driver = new R3EDriverDataBuilder().WithName("Offline").WithUserId(userId).Build();

        R3ETelemetryMapper.ToDriverStanding(in driver, R3ESectorTimeConvention.Cumulative).SimDriverId.ShouldBeNull();
    }

    [Fact]
    public void Maps_gaps_pit_state_and_finish_status()
    {
        var driver = new R3EDriverDataBuilder()
            .WithName("Kimi")
            .WithGaps(toCarAhead: 1.25f, toCarBehind: 0.75f)
            .WithPitState(inPitlane: 1, pitStopStatus: (int)PitStopStatus.FourTyresUnserved, numPitstops: 2)
            .WithFinishStatus((int)DriverFinishStatus.None)
            .Build();

        var standing = R3ETelemetryMapper.ToDriverStanding(in driver, R3ESectorTimeConvention.Cumulative);

        standing.GapToCarAhead.ShouldBe(TimeSpan.FromSeconds(1.25f));
        standing.GapToCarBehind.ShouldBe(TimeSpan.FromSeconds(0.75f));
        standing.InPitLane.ShouldBe(true);
        // No tracker was passed, so the stage is the ungraded reading rather than a guess at one.
        standing.PitLaneState.ShouldBe(PitLaneState.InPitLane);
        standing.PitStopStatus.ShouldBe(PitStopStatus.FourTyresUnserved);
        standing.PitStopCount.ShouldBe(2);
        standing.FinishStatus.ShouldBe(DriverFinishStatus.None);
    }

    [Fact]
    public void An_unrecognised_status_code_reads_as_unavailable_rather_than_being_invented()
    {
        var driver = new R3EDriverDataBuilder()
            .WithName("Kimi")
            .WithPitState(inPitlane: 0, pitStopStatus: 99, numPitstops: 0)
            .WithFinishStatus(99)
            .Build();

        var standing = R3ETelemetryMapper.ToDriverStanding(in driver, R3ESectorTimeConvention.Cumulative);

        standing.PitStopStatus.ShouldBe(PitStopStatus.Unavailable);
        standing.FinishStatus.ShouldBe(DriverFinishStatus.Unavailable);
    }

    [Fact]
    public void Counts_only_the_penalties_actually_pending()
    {
        var none = new R3EDriverDataBuilder().WithName("Clean").Build();
        R3ETelemetryMapper.ToDriverStanding(in none, R3ESectorTimeConvention.Cumulative).PenaltyCount.ShouldBe(0);

        var two = new R3EDriverDataBuilder()
            .WithName("Naughty")
            .WithPenalties(driveThrough: 0f, slowDown: 3.5f)
            .Build();
        R3ETelemetryMapper.ToDriverStanding(in two, R3ESectorTimeConvention.Cumulative).PenaltyCount.ShouldBe(2);
    }

    [Fact]
    public void Standings_carry_the_local_driver_id_so_the_rich_row_can_be_identified()
    {
        var raw = new R3ESharedRawBuilder()
            .InRaceSession("Spa", "Grand Prix")
            .WithSimulationTime(123.5)
            .Configure((ref R3ESharedRaw r) => r.VehicleInfo.UserId = 4242)
            .Build();

        R3EDriverData[] drivers =
        [
            new R3EDriverDataBuilder().WithName("Kimi").WithUserId(4242).WithPlace(1, 1).Build(),
            new R3EDriverDataBuilder().WithName("Rival").WithUserId(99).WithPlace(2, 2).Build(),
        ];

        var sessionId = Guid.NewGuid();
        var capturedAt = DateTimeOffset.UnixEpoch;

        var standings = R3ETelemetryMapper.ToSessionStandings(drivers, in raw, sessionId, capturedAt, R3ESectorTimeConvention.Cumulative);

        standings.SessionId.ShouldBe(sessionId);
        standings.CapturedAtUtc.ShouldBe(capturedAt);
        standings.SimulationTime.ShouldBe(123.5);
        standings.LocalSimDriverId.ShouldBe("4242");
        standings.Drivers.Count.ShouldBe(2);
        standings.Drivers[0].SimDriverId.ShouldBe(standings.LocalSimDriverId);
        standings.Drivers.Select(d => d.DisplayName).ShouldBe(["Kimi", "Rival"]);
    }

    /// <summary>
    /// The local car's stage comes from the root block, which reports it; every other car's is
    /// inferred from a flag and a speed. Both land on the same field, and the asymmetry is the
    /// point — a rival's "entering" is a deduction, the local car's is the simulator's own word.
    /// </summary>
    [Fact]
    public void The_local_car_gets_the_reported_pit_stage_while_rivals_get_an_inferred_one()
    {
        var raw = new R3ESharedRawBuilder()
            .InRaceSession("Spa", "Grand Prix")
            .Configure((ref R3ESharedRaw r) =>
            {
                r.VehicleInfo.UserId = 4242;
                // 1 = requested stop: still on track, and a rung nothing in the driver array can say.
                r.PitState = 1;
            })
            .Build();

        R3EDriverData[] drivers =
        [
            new R3EDriverDataBuilder().WithName("Kimi").WithUserId(4242).WithSlotId(1).WithPlace(1, 1)
                .WithPitState(inPitlane: 0, pitStopStatus: -1, numPitstops: 0)
                .WithSpeed(70f)
                .Build(),
            new R3EDriverDataBuilder().WithName("Rival").WithUserId(99).WithSlotId(2).WithPlace(2, 2)
                .WithPitState(inPitlane: 1, pitStopStatus: -1, numPitstops: 0)
                .WithSpeed(20f)
                .Build(),
        ];

        var standings = R3ETelemetryMapper.ToSessionStandings(
            drivers, in raw, Guid.NewGuid(), DateTimeOffset.UnixEpoch,
            R3ESectorTimeConvention.Cumulative, new R3EPitLaneTracker());

        standings.Drivers[0].PitLaneState.ShouldBe(PitLaneState.Requested);
        standings.Drivers[0].InPitLane.ShouldBe(false);
        standings.Drivers[1].PitLaneState.ShouldBe(PitLaneState.Entering);
    }
}
