using RaceIntelligence.Connectors.RaceRoom;
using RaceIntelligence.Connectors.RaceRoom.Interop;
using RaceIntelligence.Connectors.RaceRoom.Tests.Support;
using RaceIntelligence.Core.Sessions;
using Shouldly;

namespace RaceIntelligence.Connectors.RaceRoom.Tests;

/// <summary>
/// The inference that turns RaceRoom's binary <c>in_pitlane</c> into a stage for cars other than
/// the local one.
/// </summary>
/// <remarks>
/// The distinction under test throughout is between what was <b>seen</b> and what is <b>guessed</b>:
/// entering and exiting are guesses drawn from a car's history, and every case where the history is
/// missing has to fall back to the weaker claim rather than pick one.
/// </remarks>
public sealed class R3EPitLaneTrackerTests
{
    private const int Slot = 7;

    [Fact]
    public void A_car_on_track_is_on_track()
    {
        var tracker = new R3EPitLaneTracker();

        tracker.Observe(Slot, inPitLane: false, speedMetersPerSecond: 60f)
            .ShouldBe(PitLaneState.OnTrack);
    }

    [Fact]
    public void An_unreported_flag_is_unavailable_rather_than_on_track()
    {
        var tracker = new R3EPitLaneTracker();

        tracker.Observe(Slot, inPitLane: null, speedMetersPerSecond: 60f)
            .ShouldBe(PitLaneState.Unavailable);
    }

    /// <summary>
    /// The whole point of holding state: the same frame reads as entering before the stop and
    /// exiting after it, and only the memory of having been stationary tells them apart.
    /// </summary>
    [Fact]
    public void A_visit_reads_as_entering_then_stopped_then_exiting()
    {
        var tracker = new R3EPitLaneTracker();

        tracker.Observe(Slot, inPitLane: true, speedMetersPerSecond: 22f)
            .ShouldBe(PitLaneState.Entering);
        tracker.Observe(Slot, inPitLane: true, speedMetersPerSecond: 0.1f)
            .ShouldBe(PitLaneState.Stopped);
        tracker.Observe(Slot, inPitLane: true, speedMetersPerSecond: 15f)
            .ShouldBe(PitLaneState.Exiting);
    }

    /// <summary>
    /// A car on the jacks still twitches. Without a tolerance it would flicker between stopped and
    /// exiting for the length of its stop, which is the one moment the row is being watched.
    /// </summary>
    [Fact]
    public void A_car_settling_on_its_jacks_still_reads_as_stopped()
    {
        var tracker = new R3EPitLaneTracker();
        tracker.Observe(Slot, inPitLane: true, speedMetersPerSecond: 22f);

        tracker.Observe(Slot, inPitLane: true, speedMetersPerSecond: 0.4f)
            .ShouldBe(PitLaneState.Stopped);
    }

    [Fact]
    public void Leaving_the_pit_lane_ends_the_visit_so_the_next_one_opens_on_entering()
    {
        var tracker = new R3EPitLaneTracker();
        tracker.Observe(Slot, inPitLane: true, speedMetersPerSecond: 0f);
        tracker.Observe(Slot, inPitLane: false, speedMetersPerSecond: 40f);

        tracker.Observe(Slot, inPitLane: true, speedMetersPerSecond: 22f)
            .ShouldBe(PitLaneState.Entering);
    }

    [Fact]
    public void Each_car_is_remembered_separately()
    {
        var tracker = new R3EPitLaneTracker();
        tracker.Observe(slotId: 1, inPitLane: true, speedMetersPerSecond: 0f);

        tracker.Observe(slotId: 2, inPitLane: true, speedMetersPerSecond: 20f)
            .ShouldBe(PitLaneState.Entering);
        tracker.Observe(slotId: 1, inPitLane: true, speedMetersPerSecond: 20f)
            .ShouldBe(PitLaneState.Exiting);
    }

    /// <summary>
    /// Without a speed there is no way to tell a car heading for its box from one leaving it, and
    /// without a slot there is nothing to file the answer under. Both give the ungraded reading.
    /// </summary>
    [Theory]
    [InlineData(null, 20f)]
    [InlineData(Slot, null)]
    public void An_ungradable_car_reports_being_in_the_pit_lane_and_nothing_more(int? slotId, float? speed)
    {
        var tracker = new R3EPitLaneTracker();

        tracker.Observe(slotId, inPitLane: true, speed).ShouldBe(PitLaneState.InPitLane);
    }

    [Fact]
    public void Clearing_forgets_every_car()
    {
        var tracker = new R3EPitLaneTracker();
        tracker.Observe(Slot, inPitLane: true, speedMetersPerSecond: 0f);

        tracker.Clear();

        tracker.Observe(Slot, inPitLane: true, speedMetersPerSecond: 20f)
            .ShouldBe(PitLaneState.Entering);
    }

    /// <summary>
    /// RaceRoom publishes the local car's stage outright, including the one rung — a requested stop
    /// while still on track — that no amount of watching the driver array could produce.
    /// </summary>
    [Theory]
    [InlineData(1, PitLaneState.Requested)]
    [InlineData(2, PitLaneState.Entering)]
    [InlineData(3, PitLaneState.Stopped)]
    [InlineData(4, PitLaneState.Exiting)]
    public void The_local_cars_reported_stage_wins_over_the_inferred_one(int rawPitState, PitLaneState expected)
    {
        R3EPitLaneTracker.FromLocalPitState(rawPitState, PitLaneState.OnTrack).ShouldBe(expected);
    }

    /// <summary>
    /// <c>pit_state</c> 0 says "no stop scheduled", not "not in the pit lane" — a car driving
    /// through with nothing booked reports it while squarely inside. Deferring to the inference
    /// keeps that car off the track it is not on.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(99)]
    public void A_stage_the_simulator_does_not_state_leaves_the_inference_standing(int rawPitState)
    {
        R3EPitLaneTracker.FromLocalPitState(rawPitState, PitLaneState.Entering)
            .ShouldBe(PitLaneState.Entering);
    }

    [Fact]
    public void The_local_car_is_matched_by_account_id_when_the_session_issues_one()
    {
        var raw = new R3ESharedRawBuilder()
            .InRaceSession("Spa", "Grand Prix")
            .Configure((ref R3ESharedRaw r) => r.VehicleInfo.UserId = 4242)
            .Build();

        var mine = new R3EDriverDataBuilder().WithName("Kimi").WithUserId(4242).Build();
        var theirs = new R3EDriverDataBuilder().WithName("Rival").WithUserId(99).Build();

        R3EPitLaneTracker.IsLocalCar(in mine, in raw).ShouldBeTrue();
        R3EPitLaneTracker.IsLocalCar(in theirs, in raw).ShouldBeFalse();
    }

    /// <summary>
    /// Offline RaceRoom issues no account ids at all, so the slot is the only thing separating the
    /// local car from the AI beside it.
    /// </summary>
    [Fact]
    public void The_local_car_falls_back_to_the_slot_when_nobody_has_an_account_id()
    {
        var raw = new R3ESharedRawBuilder()
            .InRaceSession("Spa", "Grand Prix")
            .Configure((ref R3ESharedRaw r) => r.VehicleInfo.SlotId = 3)
            .Build();

        var mine = new R3EDriverDataBuilder().WithName("Kimi").WithSlotId(3).Build();
        var theirs = new R3EDriverDataBuilder().WithName("AI").WithSlotId(4).Build();

        R3EPitLaneTracker.IsLocalCar(in mine, in raw).ShouldBeTrue();
        R3EPitLaneTracker.IsLocalCar(in theirs, in raw).ShouldBeFalse();
    }
}
