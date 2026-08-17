using RaceIntelligence.Live.Contracts.Publish;
using RaceIntelligence.Live.Contracts.View;
using RaceIntelligence.Web.Live;
using RaceIntelligence.Web.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Web.Tests.Live;

/// <summary>
/// Covers the one piece of live state that accumulates rather than conflating: the completed laps a
/// room has watched go by. The properties that matter are all about what a race engineer must never
/// be shown — a lap twice, a lap missing, or a lap whose validity belongs to the following one.
/// </summary>
public sealed class LapHistoryTests
{
    private static readonly TimeSpan LapTime = TimeSpan.FromSeconds(104.5);

    private static LiveDriverDto Car(
        int completedLaps,
        bool? currentLapValid = true,
        TimeSpan? previousLapTime = null,
        string simDriverId = "77") =>
        LiveDtoFactory.Driver(
            simDriverId: simDriverId,
            position: 1,
            completedLaps: completedLaps,
            previousLapTime: previousLapTime ?? LapTime,
            previousSectorTimes: [TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(70), previousLapTime ?? LapTime],
            currentLapValid: currentLapValid);

    /// <summary>Drives a room through a series of snapshots from one publisher.</summary>
    private static (LiveHubFixture Hub, LiveRoom Room, LivePublisherIdentity Identity) RoomWith(
        params LiveDriverDto[][] snapshots)
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "77");
        var sessionId = Guid.NewGuid();

        foreach (var snapshot in snapshots)
        {
            hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrameOf(sessionId, snapshot));
        }

        return (hub, room, identity);
    }

    /// <summary>
    /// The first snapshot seeds and records nothing: the hub cannot tell whether the
    /// <c>PreviousLapTime</c> it arrives holding belongs to this session or to the last one the
    /// simulator ran, and a history whose first row might be from another session is worse than one
    /// that starts where the hub started watching.
    /// </summary>
    [Fact]
    public void The_first_snapshot_seeds_the_lap_count_without_recording_anything()
    {
        var (_, room, _) = RoomWith([Car(completedLaps: 4)]);

        room.LapHistoryFor("id:77").ShouldNotBeNull().Laps.ShouldBeEmpty();
    }

    [Fact]
    public void A_completed_lap_is_recorded_with_its_time_and_splits()
    {
        var (_, room, _) = RoomWith(
            [Car(completedLaps: 4)],
            [Car(completedLaps: 5)]);

        var lap = room.LapHistoryFor("id:77").ShouldNotBeNull().Laps.ShouldHaveSingleItem();
        lap.LapNumber.ShouldBe(5);
        lap.LapTimeMs.ShouldBe(LapTime.TotalMilliseconds);
        lap.SectorMs.Count.ShouldBe(3);
        lap.SectorMs[0].ShouldBe(30_000);
    }

    /// <summary>
    /// The reason the accumulator keeps a validity flag of its own. <c>CurrentLapValid</c> describes
    /// the lap <i>in progress</i>, so the snapshot that reports the count going up already carries
    /// the new lap's flag — reading it there would attribute the next lap's validity to this one and
    /// mark an invalidated lap as clean.
    /// </summary>
    [Fact]
    public void A_lap_completed_while_invalid_is_recorded_invalid()
    {
        var (_, room, _) = RoomWith(
            [Car(completedLaps: 4, currentLapValid: true)],
            [Car(completedLaps: 4, currentLapValid: false)],   // the driver puts a wheel off
            [Car(completedLaps: 5, currentLapValid: true)]);   // and the flag resets for the new lap

        room.LapHistoryFor("id:77").ShouldNotBeNull().Laps.ShouldHaveSingleItem().Valid.ShouldBe(false);
    }

    [Fact]
    public void A_lap_completed_while_valid_is_recorded_valid()
    {
        var (_, room, _) = RoomWith(
            [Car(completedLaps: 4, currentLapValid: true)],
            [Car(completedLaps: 5, currentLapValid: true)]);

        room.LapHistoryFor("id:77").ShouldNotBeNull().Laps.ShouldHaveSingleItem().Valid.ShouldBe(true);
    }

    /// <summary>
    /// Standings arrive at ten times a second and a lap finishes about once a minute, so the same
    /// completed lap is described by hundreds of snapshots. Recording is idempotent by lap number,
    /// which is what makes all but the first of them a no-op.
    /// </summary>
    [Fact]
    public void Repeating_a_snapshot_records_the_lap_once()
    {
        var (_, room, _) = RoomWith(
            [Car(completedLaps: 4)],
            [Car(completedLaps: 5)],
            [Car(completedLaps: 5)],
            [Car(completedLaps: 5)]);

        room.LapHistoryFor("id:77").ShouldNotBeNull().Laps.ShouldHaveSingleItem();
    }

    /// <summary>
    /// A publisher away for longer than a lap. The laps it missed are listed with their timings
    /// unknown rather than silently absent, because only the newest is described by the snapshot
    /// that reports them — giving the others its numbers would attribute one lap's time to several.
    /// </summary>
    [Fact]
    public void Laps_the_hub_did_not_watch_are_listed_with_unknown_timings()
    {
        var (_, room, _) = RoomWith(
            [Car(completedLaps: 4)],
            [Car(completedLaps: 7)]);

        var laps = room.LapHistoryFor("id:77").ShouldNotBeNull().Laps;
        laps.Select(lap => lap.LapNumber).ShouldBe([5, 6, 7]);
        laps[0].LapTimeMs.ShouldBeNull("nothing describes lap 5, and a guess would be worse than a gap");
        laps[1].LapTimeMs.ShouldBeNull();
        laps[2].LapTimeMs.ShouldBe(LapTime.TotalMilliseconds);
    }

    /// <summary>
    /// The acceptance case for switching publishers mid-race. Two clients see the same driver, one
    /// drops out, and the room's snapshot selection moves to the other — which is a lap behind.
    /// Idempotency by lap number is what makes that produce neither a duplicate nor a gap.
    /// </summary>
    [Fact]
    public void Two_publishers_one_dropping_out_produce_no_duplicate_or_missing_laps()
    {
        var hub = new LiveHubFixture();

        // The leader sees a bigger field, so SelectSnapshotLocked prefers it while it is connected.
        var leader = LiveDtoFactory.Identity(clientName: "Leader");
        var laggard = LiveDtoFactory.Identity(clientName: "Laggard");
        hub.AnnounceRoom(leader, localSimDriverId: "77");
        var room = hub.AnnounceRoom(laggard, localSimDriverId: "88");

        var session = Guid.NewGuid();
        var spectator = LiveDtoFactory.Driver(simDriverId: "99", position: 2, completedLaps: 0);

        void Leader(int laps) => hub.Rooms.ApplyStandings(
            leader.ClientId, LiveDtoFactory.StandingsFrameOf(session, Car(laps), spectator));

        void Laggard(int laps) => hub.Rooms.ApplyStandings(
            laggard.ClientId, LiveDtoFactory.StandingsFrameOf(session, Car(laps)));

        Leader(1);
        Laggard(1);
        Leader(2);
        Laggard(1);   // the laggard is a snapshot behind throughout
        Leader(3);
        Laggard(2);

        // The leader goes away; the laggard's smaller snapshot becomes the selected one and rewinds
        // the visible lap count from 3 to 2.
        hub.Rooms.RemovePublisher(leader.ClientId);

        Laggard(3);
        Laggard(4);

        var laps = room.LapHistoryFor("id:77").ShouldNotBeNull().Laps;
        laps.Select(lap => lap.LapNumber).ShouldBe([2, 3, 4]);
    }

    /// <summary>
    /// A collector whose socket dropped rejoins the room it was already in, inside the janitor's
    /// grace period. The room object survives, and so does everything it has accumulated — that is
    /// the whole point of expiring on last frame rather than on last publisher.
    /// </summary>
    [Fact]
    public void History_survives_a_publisher_reconnect_within_the_room_expiry()
    {
        var hub = new LiveHubFixture(roomExpiry: TimeSpan.FromSeconds(30));
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "77");
        var session = Guid.NewGuid();

        hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrameOf(session, Car(1)));
        hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrameOf(session, Car(2)));

        hub.Rooms.RemovePublisher(identity.ClientId);
        hub.Time.Advance(TimeSpan.FromSeconds(10));
        hub.Rooms.RemoveExpiredRooms().ShouldBe(0, "the room is still inside its grace period");

        // The same client id reconnects, so it rejoins rather than opening a second room.
        var rejoined = hub.AnnounceRoom(identity, localSimDriverId: "77");
        rejoined.RoomId.ShouldBe(room.RoomId);

        hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrameOf(session, Car(3)));

        rejoined.LapHistoryFor("id:77").ShouldNotBeNull()
            .Laps.Select(lap => lap.LapNumber).ShouldBe([2, 3]);
    }

    /// <summary>
    /// Nothing here may grow with session length. An overnight practice server would otherwise hold
    /// every lap of every car for as long as the room lives.
    /// </summary>
    [Fact]
    public void History_is_bounded_and_says_so_once_it_starts_dropping_laps()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "77");
        var session = Guid.NewGuid();

        const int Laps = LapHistoryAccumulator.MaxLapsPerDriver + 10;
        for (int lap = 0; lap <= Laps; lap++)
        {
            hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrameOf(session, Car(lap)));
        }

        var history = room.LapHistoryFor("id:77").ShouldNotBeNull();
        history.Laps.Count.ShouldBe(LapHistoryAccumulator.MaxLapsPerDriver);
        history.Truncated.ShouldBeTrue();

        // Drop-oldest: the newest laps are the ones a race engineer is reading.
        history.Laps[^1].LapNumber.ShouldBe(Laps);
        history.Laps[0].LapNumber.ShouldBe(Laps - LapHistoryAccumulator.MaxLapsPerDriver + 1);
    }

    [Fact]
    public void A_history_within_the_cap_is_not_marked_truncated()
    {
        var (_, room, _) = RoomWith([Car(0)], [Car(1)], [Car(2)]);

        room.LapHistoryFor("id:77").ShouldNotBeNull().Truncated.ShouldBeFalse();
    }

    [Fact]
    public void A_driver_the_room_has_never_seen_has_no_history_at_all()
    {
        var (_, room, _) = RoomWith([Car(1)]);

        room.LapHistoryFor("id:nobody").ShouldBeNull();
    }

    /// <summary>
    /// A completed lap reaches only the viewers that asked for that driver — expanding a row is a
    /// subscription, not a broadcast.
    /// </summary>
    [Fact]
    public void A_completed_lap_reaches_only_subscribed_viewers()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "77");
        var session = Guid.NewGuid();

        var subscribed = hub.AddViewer(room.RoomId);
        subscribed.SubscribeLapHistory("id:77");
        var uninterested = hub.AddViewer(room.RoomId);

        hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrameOf(session, Car(1)));
        hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrameOf(session, Car(2)));

        Drain(subscribed).OfType<LapHistoryMessage>().ShouldHaveSingleItem()
            .Laps.ShouldHaveSingleItem().LapNumber.ShouldBe(2);
        Drain(uninterested).OfType<LapHistoryMessage>().ShouldBeEmpty();
    }

    /// <summary>
    /// The property that makes a full snapshot the right shape for a conflating slot. A viewer too
    /// slow to read every message has the older ones collapsed into the newest — and because each
    /// one restates the whole history, the survivor contains every lap the ones it replaced did.
    /// An incremental "lap N completed" event would leave a hole here.
    /// </summary>
    [Fact]
    public void A_slow_viewer_coalesces_per_driver_and_never_sees_a_gap()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "77");
        var session = Guid.NewGuid();

        var viewer = hub.AddViewer(room.RoomId);
        viewer.SubscribeLapHistory("id:77");
        viewer.SubscribeLapHistory("id:88");

        var other = LiveDtoFactory.Driver(simDriverId: "88", position: 2, completedLaps: 0);
        LiveDriverDto Other(int laps) =>
            other with { CompletedLaps = laps, PreviousLapTime = LapTime };

        // Six laps between the two drivers, none of them read by the viewer as they arrive.
        for (int lap = 0; lap <= 3; lap++)
        {
            hub.Rooms.ApplyStandings(
                identity.ClientId, LiveDtoFactory.StandingsFrameOf(session, Car(lap), Other(lap)));
        }

        var histories = Drain(viewer).OfType<LapHistoryMessage>().ToList();

        // One surviving message per driver, not one per lap...
        histories.Count.ShouldBe(2);

        // ...and each carries every lap that driver completed, in order and with no hole.
        foreach (var history in histories)
        {
            history.Laps.Select(lap => lap.LapNumber).ShouldBe([1, 2, 3]);
        }
    }

    /// <summary>
    /// The tower's last-lap column reads `PreviousLapValid`, and the accumulator is the only thing
    /// that knows it: the flag on the snapshot reporting the count going up already belongs to the
    /// new lap, so the completed lap's validity is the one observed a tick earlier.
    /// </summary>
    [Fact]
    public void The_tower_carries_the_completed_laps_own_validity()
    {
        var (hub, room, _) = RoomWith(
            // Lap 5 is driven cleanly, so the flag while it is in progress is true...
            [Car(completedLaps: 4, currentLapValid: true)],
            // ...and by the snapshot that reports it finished, the flag has moved on to lap 6, which
            // the driver has already ruined. The tower must strike lap 5's successor, not lap 5.
            [Car(completedLaps: 5, currentLapValid: false)]);

        var row = room.Snapshot(hub.Time.GetUtcNow()).ShouldNotBeNull().Drivers.ShouldHaveSingleItem();

        row.PreviousLapValid.ShouldBe(true);
        row.CurrentLapValid.ShouldBe(false);
    }

    [Fact]
    public void An_invalidated_lap_is_marked_invalid_on_the_tower()
    {
        var (hub, room, _) = RoomWith(
            [Car(completedLaps: 4, currentLapValid: false)],
            [Car(completedLaps: 5, currentLapValid: true)]);

        room.Snapshot(hub.Time.GetUtcNow()).ShouldNotBeNull()
            .Drivers.ShouldHaveSingleItem().PreviousLapValid.ShouldBe(false);
    }

    /// <summary>
    /// Unknown is not invalid. Before the hub has watched a driver finish a lap there is nothing to
    /// report, and reporting <see langword="false"/> would condemn a whole grid on connect.
    /// </summary>
    [Fact]
    public void A_driver_the_hub_has_not_yet_watched_finish_a_lap_has_no_reported_validity()
    {
        var (hub, room, _) = RoomWith([Car(completedLaps: 4, currentLapValid: false)]);

        room.Snapshot(hub.Time.GetUtcNow()).ShouldNotBeNull()
            .Drivers.ShouldHaveSingleItem().PreviousLapValid.ShouldBeNull();
    }

    private static List<LiveViewMessage> Drain(LiveViewer viewer)
    {
        var messages = new List<LiveViewMessage>();
        while (viewer.Queue.TryRead() is { } message)
        {
            messages.Add(message);
        }

        return messages;
    }
}
