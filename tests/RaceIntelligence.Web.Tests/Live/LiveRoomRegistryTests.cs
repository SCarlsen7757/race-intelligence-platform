using RaceIntelligence.Live.Contracts.View;
using RaceIntelligence.Web.Live;
using RaceIntelligence.Web.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Web.Tests.Live;

/// <summary>
/// Covers how publishers are placed into rooms, how rooms end, and what viewers are told about it.
/// </summary>
public sealed class LiveRoomRegistryTests
{
    [Fact]
    public void A_session_announcement_opens_a_room()
    {
        var hub = new LiveHubFixture();

        hub.Rooms.Announce(LiveDtoFactory.Identity(), LiveDtoFactory.SessionFrame(track: "Spa"));

        var rooms = hub.Rooms.BuildRoomList().Rooms;
        rooms.ShouldHaveSingleItem().TrackName.ShouldBe("Spa");
    }

    /// <summary>
    /// The premise the whole merge rests on: two clients reporting the same track, layout, session
    /// type and iteration are describing one session and must land in one room.
    /// </summary>
    [Fact]
    public void Two_publishers_reporting_the_same_session_share_a_room()
    {
        var hub = new LiveHubFixture();

        hub.Rooms.Announce(LiveDtoFactory.Identity(clientName: "Mark"), LiveDtoFactory.SessionFrame());
        hub.Rooms.Announce(LiveDtoFactory.Identity(clientName: "Anna"), LiveDtoFactory.SessionFrame());

        var room = hub.Rooms.BuildRoomList().Rooms.ShouldHaveSingleItem();
        room.Publishers.Select(publisher => publisher.ClientName).ShouldBe(["Mark", "Anna"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("Monza", "Grand Prix", 3, 1)]
    [InlineData("Spa", "Endurance", 3, 1)]
    [InlineData("Spa", "Grand Prix", 2, 1)]
    [InlineData("Spa", "Grand Prix", 3, 2)]
    public void A_session_differing_in_any_key_component_is_a_different_room(
        string track,
        string layout,
        int sessionType,
        int sessionIteration)
    {
        var hub = new LiveHubFixture();

        hub.Rooms.Announce(LiveDtoFactory.Identity(), LiveDtoFactory.SessionFrame());
        hub.Rooms.Announce(
            LiveDtoFactory.Identity(),
            LiveDtoFactory.SessionFrame(
                track: track, layout: layout, sessionType: sessionType, sessionIteration: sessionIteration));

        hub.Rooms.Count.ShouldBe(2);
    }

    /// <summary>
    /// Both clients read the name from the same simulator, so a difference in spacing or case is a
    /// transport artefact rather than a different session. Splitting on one would present as "the
    /// merge does not work", which is a bad thing to debug from a race report.
    /// </summary>
    [Fact]
    public void A_name_differing_only_in_case_or_spacing_is_the_same_room()
    {
        var hub = new LiveHubFixture();

        hub.Rooms.Announce(LiveDtoFactory.Identity(), LiveDtoFactory.SessionFrame(track: "Spa"));
        hub.Rooms.Announce(LiveDtoFactory.Identity(), LiveDtoFactory.SessionFrame(track: " spa "));

        hub.Rooms.Count.ShouldBe(1);
    }

    /// <summary>
    /// A client going from qualifying into the race must stop counting as a publisher of the
    /// session it can no longer see.
    /// </summary>
    [Fact]
    public void A_publisher_changing_session_leaves_the_room_it_was_in()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();

        hub.Rooms.Announce(identity, LiveDtoFactory.SessionFrame(sessionType: 2));
        hub.Rooms.Announce(identity, LiveDtoFactory.SessionFrame(sessionType: 3));

        var rooms = hub.Rooms.BuildRoomList().Rooms;
        rooms.Count.ShouldBe(2);
        rooms.Where(room => room.Publishers.Count > 0).ShouldHaveSingleItem().SessionType.ShouldBe(3);
    }

    [Fact]
    public void Standings_reach_a_viewer_watching_the_room()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity);
        var viewer = hub.AddViewer(room.RoomId);

        hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrame(driverCount: 4));

        var tower = viewer.Queue.TryRead().ShouldBeOfType<TowerSnapshotMessage>();
        tower.Drivers.Count.ShouldBe(4);
    }

    [Fact]
    public void Standings_do_not_reach_a_viewer_watching_a_different_room()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        hub.AnnounceRoom(identity);
        var viewer = hub.AddViewer("some-other-room");

        hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrame());

        viewer.Queue.TryRead().ShouldBeNull();
    }

    /// <summary>
    /// A room is a session, and the announcement is what names one. There is nowhere to put a frame
    /// from a publisher that has not announced, and the collector re-announces on every connection,
    /// so the gap is one frame rather than a race.
    /// </summary>
    [Fact]
    public void Standings_from_a_publisher_that_never_announced_are_dropped()
    {
        var hub = new LiveHubFixture();
        var viewer = hub.AddViewer("anything");

        hub.Rooms.ApplyStandings(Guid.NewGuid(), LiveDtoFactory.StandingsFrame());

        hub.Rooms.Count.ShouldBe(0);
        viewer.Queue.TryRead().ShouldBeNull();
    }

    [Fact]
    public void A_local_car_frame_reaches_a_viewer_focused_on_that_driver()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "4242");
        var viewer = hub.AddViewer(room.RoomId, focusDriverKey: "id:4242");

        hub.Rooms.ApplySelf(identity.ClientId, LiveDtoFactory.SelfFrame(simDriverId: "4242"));

        viewer.Queue.TryRead().ShouldBeOfType<FocusFrameMessage>().DriverKey.ShouldBe("id:4242");
    }

    [Fact]
    public void Clutch_crosses_the_hub_onto_the_focus_frame()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "4242");
        var viewer = hub.AddViewer(room.RoomId, focusDriverKey: "id:4242");

        hub.Rooms.ApplySelf(identity.ClientId, LiveDtoFactory.SelfFrame(simDriverId: "4242", clutch: 0.75f));

        viewer.Queue.TryRead().ShouldBeOfType<FocusFrameMessage>().Clutch.ShouldBe(0.75f);
    }

    /// <summary>
    /// A car with an automatic clutch reports nothing, and "nothing" must not reach a race engineer
    /// as a clutch pedal sitting fully up — that is a different and confident claim.
    /// </summary>
    [Fact]
    public void A_sim_reporting_no_clutch_leaves_the_focus_frame_null_not_zero()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "4242");
        var viewer = hub.AddViewer(room.RoomId, focusDriverKey: "id:4242");

        hub.Rooms.ApplySelf(identity.ClientId, LiveDtoFactory.SelfFrame(simDriverId: "4242", clutch: null));

        viewer.Queue.TryRead().ShouldBeOfType<FocusFrameMessage>().Clutch.ShouldBeNull();
    }

    /// <summary>
    /// The channel that finally makes <c>SimCapabilities.Damage</c> mean something: it has always
    /// been advertised in the hello while no damage value ever crossed the wire.
    /// </summary>
    [Fact]
    public void An_extras_document_reaches_a_viewer_focused_on_that_driver()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "4242");
        var focused = hub.AddViewer(room.RoomId, focusDriverKey: "id:4242");
        var elsewhere = hub.AddViewer(room.RoomId, focusDriverKey: "id:9999");

        hub.Rooms.ApplyExtras(identity.ClientId, LiveDtoFactory.ExtrasFrame(simDriverId: "4242"));

        var message = focused.Queue.TryRead().ShouldBeOfType<ExtrasFrameMessage>();
        message.DriverKey.ShouldBe("id:4242");

        // Carried through as the string the connector wrote — the hub never parses it, and the -1
        // sentinel for an unreported channel arrives intact.
        message.Extras.ShouldContain("\"transmission\":-1.0");

        elsewhere.Queue.TryRead().ShouldBeNull();
    }

    /// <summary>
    /// Extras arrive about once a second, so a viewer focusing a driver between two of them would
    /// otherwise sit in front of an empty damage panel — which reads as "no damage" rather than
    /// "not known yet".
    /// </summary>
    [Fact]
    public void The_latest_extras_are_retained_for_a_viewer_that_focuses_later()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "4242");

        hub.Rooms.ApplyExtras(identity.ClientId, LiveDtoFactory.ExtrasFrame(simDriverId: "4242"));

        room.LatestExtrasFor("id:4242").ShouldNotBeNull().Extras.ShouldContain("engine");
        room.LatestExtrasFor("id:9999").ShouldBeNull();
    }

    /// <summary>
    /// The self frame's own id is preferred, but the session announcement answers the same question
    /// and is available when a frame omits it.
    /// </summary>
    [Fact]
    public void A_local_car_frame_without_an_id_falls_back_to_the_announced_driver()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "4242");
        var viewer = hub.AddViewer(room.RoomId, focusDriverKey: "id:4242");

        hub.Rooms.ApplySelf(identity.ClientId, LiveDtoFactory.SelfFrame(simDriverId: null));

        viewer.Queue.TryRead().ShouldBeOfType<FocusFrameMessage>().DriverKey.ShouldBe("id:4242");
    }

    /// <summary>
    /// With nothing identifying the local car from any source there is no tower row to attach the
    /// telemetry to, and guessing one would show a race engineer another driver's pedal traces
    /// under the wrong name.
    /// </summary>
    [Fact]
    public void A_local_car_frame_with_no_identity_anywhere_is_dropped()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(
            identity, localSimDriverId: null, localSlotId: null, playerName: null);
        var viewer = hub.AddViewer(room.RoomId, focusDriverKey: "id:4242");

        hub.Rooms.ApplySelf(identity.ClientId, LiveDtoFactory.SelfFrame(simDriverId: null));

        viewer.Queue.TryRead().ShouldBeNull();
    }

    /// <summary>
    /// An offline session, where the simulator issues no account id at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RaceRoom reports a user id only for an authenticated online session and writes 0 or -1
    /// otherwise, so every offline and single-player race announces no local identity. The tower
    /// row for that car is keyed on its slot, and the local car has to reach the same key or its
    /// rich telemetry is dropped for want of a row to attach it to.
    /// </para>
    /// <para>
    /// This is the regression that made a whole tier of the dashboard look broken: the timing tower
    /// carried on updating — it had its own <c>slot:</c> fallback — while the focused car showed no
    /// pedal inputs, no tyre data and no damage, none of which is obviously the same fault.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_local_car_with_no_identity_is_matched_by_slot_to_its_tower_row()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: null, localSlotId: 3);
        var viewer = hub.AddViewer(room.RoomId, focusDriverKey: "slot:3");

        hub.Rooms.ApplySelf(identity.ClientId, LiveDtoFactory.SelfFrame(simDriverId: null));

        viewer.Queue.TryRead().ShouldBeOfType<FocusFrameMessage>().DriverKey.ShouldBe("slot:3");
    }

    /// <summary>Damage rides the same key resolution, so it comes back with the focus stream.</summary>
    [Fact]
    public void An_extras_document_from_a_car_with_no_identity_is_matched_by_slot()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: null, localSlotId: 3);
        var viewer = hub.AddViewer(room.RoomId, focusDriverKey: "slot:3");

        hub.Rooms.ApplyExtras(identity.ClientId, LiveDtoFactory.ExtrasFrame(simDriverId: null));

        viewer.Queue.TryRead().ShouldBeOfType<ExtrasFrameMessage>().DriverKey.ShouldBe("slot:3");
    }

    /// <summary>
    /// And the row is marked <see cref="LiveDataTier.Self"/>, so the dashboard offers the focus
    /// panel at all rather than refusing with <c>noTelemetryForDriver</c>.
    /// </summary>
    [Fact]
    public void A_driver_identified_only_by_slot_is_still_marked_self_in_the_tower()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: null, localSlotId: 2);
        var viewer = hub.AddViewer(room.RoomId);

        hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrame(
            driverCount: 3, useSlotIdsInsteadOfDriverIds: true));

        var tower = viewer.Queue.TryRead().ShouldBeOfType<TowerSnapshotMessage>();
        tower.Drivers.Single(row => row.DriverKey == "slot:2").Tier.ShouldBe(LiveDataTier.Self);
        tower.Drivers.Where(row => row.DriverKey != "slot:2")
            .ShouldAllBe(row => row.Tier == LiveDataTier.Observed);
    }

    [Fact]
    public void A_driver_whose_machine_is_publishing_is_marked_self_in_the_tower()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "2");
        var viewer = hub.AddViewer(room.RoomId);

        hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrame(driverCount: 3));

        var tower = viewer.Queue.TryRead().ShouldBeOfType<TowerSnapshotMessage>();
        tower.Drivers.Single(row => row.DriverKey == "id:2").Tier.ShouldBe(LiveDataTier.Self);
        tower.Drivers.Where(row => row.DriverKey != "id:2")
            .ShouldAllBe(row => row.Tier == LiveDataTier.Observed);
    }

    /// <summary>
    /// A room outliving its last publisher is what lets a collector whose socket dropped mid-race
    /// rejoin the room it was already in — viewers keep their subscription and never see the session
    /// flicker out of the list.
    /// </summary>
    [Fact]
    public void A_room_survives_losing_its_last_publisher()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity);

        hub.Rooms.RemovePublisher(identity.ClientId);

        hub.Rooms.FindByRoomId(room.RoomId).ShouldNotBeNull();
    }

    [Fact]
    public void A_reconnecting_publisher_rejoins_the_same_room_id()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity);

        hub.Rooms.RemovePublisher(identity.ClientId);
        hub.Time.Advance(TimeSpan.FromSeconds(10));
        hub.Rooms.Announce(identity, LiveDtoFactory.SessionFrame());

        hub.Rooms.BuildRoomList().Rooms.ShouldHaveSingleItem().RoomId.ShouldBe(room.RoomId);
    }

    [Fact]
    public void A_room_expires_once_nothing_has_been_published_for_long_enough()
    {
        var hub = new LiveHubFixture(roomExpiry: TimeSpan.FromSeconds(30));
        var identity = LiveDtoFactory.Identity();
        hub.AnnounceRoom(identity);
        hub.Rooms.RemovePublisher(identity.ClientId);

        hub.Time.Advance(TimeSpan.FromSeconds(29));
        hub.Rooms.RemoveExpiredRooms().ShouldBe(0);

        hub.Time.Advance(TimeSpan.FromSeconds(2));
        hub.Rooms.RemoveExpiredRooms().ShouldBe(1);
        hub.Rooms.Count.ShouldBe(0);
    }

    /// <summary>
    /// A publisher connected but between sessions — sitting in a menu — is still a reason to keep
    /// the room. Expiring it under a live client would drop every viewer watching.
    /// </summary>
    [Fact]
    public void A_room_with_a_publisher_still_attached_never_expires()
    {
        var hub = new LiveHubFixture(roomExpiry: TimeSpan.FromSeconds(30));
        hub.AnnounceRoom(LiveDtoFactory.Identity());

        hub.Time.Advance(TimeSpan.FromHours(1));

        hub.Rooms.RemoveExpiredRooms().ShouldBe(0);
    }

    /// <summary>
    /// Without eviction a viewer sits on a room id that resolves to nothing, showing the last tower
    /// it received as though the race were still running.
    /// </summary>
    [Fact]
    public void An_expiring_room_returns_its_viewers_to_the_room_list()
    {
        var hub = new LiveHubFixture(roomExpiry: TimeSpan.FromSeconds(30));
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity);
        var viewer = hub.AddViewer(room.RoomId);
        hub.Rooms.RemovePublisher(identity.ClientId);

        hub.Time.Advance(TimeSpan.FromSeconds(31));
        hub.Rooms.RemoveExpiredRooms();

        viewer.RoomId.ShouldBeNull();
        viewer.Queue.TryRead().ShouldBeOfType<LiveErrorMessage>().Code.ShouldBe(LiveErrorCodes.RoomClosed);
    }

    [Fact]
    public void The_room_list_reaches_every_viewer_when_a_session_starts()
    {
        var hub = new LiveHubFixture();
        var watching = hub.AddViewer("some-other-room");

        hub.Rooms.Announce(LiveDtoFactory.Identity(), LiveDtoFactory.SessionFrame());

        // Unfiltered on purpose: the room list is also the "a session just started" notification,
        // so a viewer already deep in another room still wants it.
        watching.Queue.TryRead().ShouldBeOfType<RoomListMessage>();
    }

    [Fact]
    public void The_room_list_reports_the_driver_count_and_the_publishing_client()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity(clientName: "Mark's PC");
        hub.AnnounceRoom(identity, localSimDriverId: "4242");

        hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrame(driverCount: 12));

        var room = hub.Rooms.BuildRoomList().Rooms.ShouldHaveSingleItem();
        room.DriverCount.ShouldBe(12);

        var publisher = room.Publishers.ShouldHaveSingleItem();
        publisher.ClientName.ShouldBe("Mark's PC");
        publisher.SimDriverId.ShouldBe("4242");
        publisher.DriverName.ShouldBe("Mark");
    }

    /// <summary>
    /// Step 4 does not merge fields across publishers, but it must not lose cars either: the more
    /// complete view is what the tower is projected from, so nobody disappears when a second client
    /// joins with a partial roster.
    /// </summary>
    [Fact]
    public void With_two_publishers_the_more_complete_view_drives_the_tower()
    {
        var hub = new LiveHubFixture();
        var full = LiveDtoFactory.Identity();
        var partial = LiveDtoFactory.Identity();

        hub.AnnounceRoom(full);
        hub.AnnounceRoom(partial);

        hub.Rooms.ApplyStandings(full.ClientId, LiveDtoFactory.StandingsFrame(driverCount: 20));

        // Arrives later, and would win on freshness alone — but it can see only half the field.
        hub.Time.Advance(TimeSpan.FromSeconds(1));
        hub.Rooms.ApplyStandings(partial.ClientId, LiveDtoFactory.StandingsFrame(driverCount: 10));

        var viewer = hub.AddViewer(hub.Rooms.BuildRoomList().Rooms.Single().RoomId);
        hub.Time.Advance(TimeSpan.FromSeconds(1));
        hub.Rooms.ApplyStandings(partial.ClientId, LiveDtoFactory.StandingsFrame(driverCount: 10));

        viewer.Queue.TryRead().ShouldBeOfType<TowerSnapshotMessage>().Drivers.Count.ShouldBe(20);
    }
}
