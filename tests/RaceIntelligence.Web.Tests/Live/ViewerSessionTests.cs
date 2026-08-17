using System.Net.WebSockets;
using RaceIntelligence.Live.Contracts.View;
using RaceIntelligence.Web.Live;
using RaceIntelligence.Web.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Web.Tests.Live;

/// <summary>
/// Covers the open viewing endpoint: what a browser is sent unprompted, what its commands do, and
/// what it is told when it asks for something that is not there.
/// </summary>
public sealed class ViewerSessionTests
{
    /// <summary>
    /// Runs a viewer session against a scripted socket, ending it once the expected traffic has
    /// arrived.
    /// </summary>
    private static async Task<IReadOnlyList<LiveViewMessage>> RunAsync(
        LiveHubFixture hub,
        FakeLiveSocket socket,
        int expectedMessages)
    {
        var running = hub.CreateViewerSession().RunAsync(socket, TestContext.Current.CancellationToken);

        await socket.WaitForSendsAsync(expectedMessages);
        socket.PushClose();
        await running;

        return socket.DrainViewMessages();
    }

    /// <summary>
    /// Sent before any command: the room list is the dashboard's landing view, and a viewer that had
    /// to ask for it would render empty until its first request completed.
    /// </summary>
    [Fact]
    public async Task A_viewer_is_sent_the_room_list_on_connecting()
    {
        var hub = new LiveHubFixture();
        hub.AnnounceRoom(LiveDtoFactory.Identity(), track: "Spa");

        var messages = await RunAsync(hub, new FakeLiveSocket(), expectedMessages: 1);

        messages[0].ShouldBeOfType<RoomListMessage>()
            .Rooms.ShouldHaveSingleItem().TrackName.ShouldBe("Spa");
    }

    /// <summary>
    /// Sent immediately rather than at the next publisher frame. The wait would only be ~100 ms, but
    /// a viewer switching rooms would see an empty table for it — and a room whose publisher has
    /// gone quiet would show nothing at all.
    /// </summary>
    [Fact]
    public async Task Watching_a_room_answers_with_its_current_tower()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity);
        hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrame(driverCount: 6));

        var socket = new FakeLiveSocket();
        socket.PushCommand(new WatchRoomCommand(room.RoomId));

        var messages = await RunAsync(hub, socket, expectedMessages: 2);

        messages.OfType<TowerSnapshotMessage>().ShouldHaveSingleItem().Drivers.Count.ShouldBe(6);
    }

    [Fact]
    public async Task Watching_an_unknown_room_is_answered_with_an_error()
    {
        var hub = new LiveHubFixture();
        var socket = new FakeLiveSocket();
        socket.PushCommand(new WatchRoomCommand("no-such-room"));

        var messages = await RunAsync(hub, socket, expectedMessages: 2);

        messages.OfType<LiveErrorMessage>().ShouldHaveSingleItem().Code.ShouldBe(LiveErrorCodes.UnknownRoom);
    }

    [Fact]
    public async Task Focusing_a_driver_whose_machine_is_publishing_starts_their_stream()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "2");
        hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrame(driverCount: 3));

        var socket = new FakeLiveSocket();
        socket.PushCommand(new WatchRoomCommand(room.RoomId));
        socket.PushCommand(new FocusDriverCommand("id:2"));

        var running = hub.CreateViewerSession().RunAsync(socket, TestContext.Current.CancellationToken);

        // Room list, then the tower answering the watch. Once both have arrived the focus command
        // has necessarily been processed too, since commands are handled in order.
        await socket.WaitForSendsAsync(2);
        hub.Rooms.ApplySelf(identity.ClientId, LiveDtoFactory.SelfFrame(simDriverId: "2"));

        await socket.WaitForSendsAsync(3);
        socket.PushClose();
        await running;

        socket.DrainViewMessages().OfType<FocusFrameMessage>()
            .ShouldHaveSingleItem().DriverKey.ShouldBe("id:2");
    }

    /// <summary>
    /// Answered explicitly rather than by silence. A driver with no client running looks identical
    /// in the tower to one whose telemetry has simply not arrived yet, and a viewer that clicked a
    /// row deserves to be told which it is instead of watching an empty panel.
    /// </summary>
    [Fact]
    public async Task Focusing_a_driver_nobody_is_publishing_for_is_answered_with_an_error()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "2");
        hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrame(driverCount: 3));

        var socket = new FakeLiveSocket();
        socket.PushCommand(new WatchRoomCommand(room.RoomId));
        socket.PushCommand(new FocusDriverCommand("id:3"));

        var messages = await RunAsync(hub, socket, expectedMessages: 3);

        messages.OfType<LiveErrorMessage>().ShouldHaveSingleItem()
            .Code.ShouldBe(LiveErrorCodes.NoTelemetryForDriver);
    }

    [Fact]
    public async Task Focusing_a_driver_who_is_not_in_the_session_is_answered_with_an_error()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "2");
        hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrame(driverCount: 3));

        var socket = new FakeLiveSocket();
        socket.PushCommand(new WatchRoomCommand(room.RoomId));
        socket.PushCommand(new FocusDriverCommand("id:nobody"));

        var messages = await RunAsync(hub, socket, expectedMessages: 3);

        messages.OfType<LiveErrorMessage>().ShouldHaveSingleItem().Code.ShouldBe(LiveErrorCodes.UnknownDriver);
    }

    /// <summary>
    /// Answered from what the hub already holds rather than at the driver's next lap, which on a long
    /// circuit is minutes of an empty panel — and never at all for a driver who has finished.
    /// </summary>
    [Fact]
    public async Task Subscribing_to_lap_history_answers_immediately()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "2");
        hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrame(driverCount: 3));

        var socket = new FakeLiveSocket();
        socket.PushCommand(new WatchRoomCommand(room.RoomId));
        socket.PushCommand(new SubscribeLapHistoryCommand("id:2"));

        var messages = await RunAsync(hub, socket, expectedMessages: 3);

        messages.OfType<LapHistoryMessage>().ShouldHaveSingleItem().DriverKey.ShouldBe("id:2");
    }

    /// <summary>
    /// The difference between lap history and a focus stream. Lap history is read out of the
    /// standings snapshot, which describes every car in the session, so a driver who is not running
    /// a collector — the tier that gets <c>noTelemetryForDriver</c> when focused — has a full
    /// history like anyone else.
    /// </summary>
    [Fact]
    public async Task Lap_history_works_for_an_observed_tier_driver_too()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "2");
        hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrame(driverCount: 3));

        var socket = new FakeLiveSocket();
        socket.PushCommand(new WatchRoomCommand(room.RoomId));

        // "id:3" is in the field but nobody publishes their machine, so focusing them is refused.
        socket.PushCommand(new SubscribeLapHistoryCommand("id:3"));

        var messages = await RunAsync(hub, socket, expectedMessages: 3);

        messages.OfType<LiveErrorMessage>().ShouldBeEmpty();
        messages.OfType<LapHistoryMessage>().ShouldHaveSingleItem().DriverKey.ShouldBe("id:3");
    }

    [Fact]
    public async Task Subscribing_to_lap_history_for_an_unknown_driver_is_answered_with_an_error()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "2");
        hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrame(driverCount: 3));

        var socket = new FakeLiveSocket();
        socket.PushCommand(new WatchRoomCommand(room.RoomId));
        socket.PushCommand(new SubscribeLapHistoryCommand("id:nobody"));

        // The connection stays open: one bad subscription costs that subscription and nothing else,
        // which is why a later command is still answered.
        socket.PushCommand(new SubscribeLapHistoryCommand("id:2"));

        var messages = await RunAsync(hub, socket, expectedMessages: 4);

        messages.OfType<LiveErrorMessage>().ShouldHaveSingleItem().Code.ShouldBe(LiveErrorCodes.UnknownDriver);
        messages.OfType<LapHistoryMessage>().ShouldHaveSingleItem().DriverKey.ShouldBe("id:2");
    }

    [Fact]
    public async Task Subscribing_to_lap_history_before_watching_a_room_is_answered_with_an_error()
    {
        var hub = new LiveHubFixture();
        var socket = new FakeLiveSocket();
        socket.PushCommand(new SubscribeLapHistoryCommand("id:2"));

        var messages = await RunAsync(hub, socket, expectedMessages: 2);

        messages.OfType<LiveErrorMessage>().ShouldHaveSingleItem().Code.ShouldBe(LiveErrorCodes.NotWatchingRoom);
    }

    [Fact]
    public async Task Focusing_before_watching_a_room_is_answered_with_an_error()
    {
        var hub = new LiveHubFixture();
        var socket = new FakeLiveSocket();
        socket.PushCommand(new FocusDriverCommand("id:2"));

        var messages = await RunAsync(hub, socket, expectedMessages: 2);

        messages.OfType<LiveErrorMessage>().ShouldHaveSingleItem().Code.ShouldBe(LiveErrorCodes.NotWatchingRoom);
    }

    /// <summary>
    /// A driver key is only meaningful within a room, so a focus must not survive a room switch.
    /// </summary>
    /// <remarks>
    /// The case that makes this matter, and the reason the test uses two rooms containing the same
    /// driver key rather than simply leaving the first room: the danger is not a focus that resolves
    /// to nobody — that is harmless — it is a focus that silently re-resolves to a <i>different</i>
    /// driver who happens to share an id. A viewer that switched sessions would start receiving
    /// someone else's pedal traces under the name it had selected, with nothing on screen to say so.
    /// </remarks>
    [Fact]
    public async Task Switching_rooms_does_not_carry_a_focus_onto_a_driver_with_the_same_key()
    {
        var hub = new LiveHubFixture();

        var first = LiveDtoFactory.Identity();
        var firstRoom = hub.AnnounceRoom(first, track: "Spa", localSimDriverId: "2");
        hub.Rooms.ApplyStandings(first.ClientId, LiveDtoFactory.StandingsFrame(driverCount: 3));

        // A different session whose publisher happens to carry the same simulator driver id.
        var second = LiveDtoFactory.Identity();
        var secondRoom = hub.AnnounceRoom(second, track: "Monza", localSimDriverId: "2");
        hub.Rooms.ApplyStandings(second.ClientId, LiveDtoFactory.StandingsFrame(driverCount: 3));

        var socket = new FakeLiveSocket();
        socket.PushCommand(new WatchRoomCommand(firstRoom.RoomId));
        socket.PushCommand(new FocusDriverCommand("id:2"));
        socket.PushCommand(new WatchRoomCommand(secondRoom.RoomId));

        var running = hub.CreateViewerSession().RunAsync(socket, TestContext.Current.CancellationToken);

        // Waited for by content, not by count: commands are handled in order, so the second room's
        // tower arriving proves all three have been processed. Counting sends would be wrong here —
        // the queue conflates, so the two towers may legitimately arrive as one.
        var collected = await socket.CollectUntilAsync(message =>
            message is TowerSnapshotMessage tower && tower.RoomId == secondRoom.RoomId);

        hub.Rooms.ApplySelf(second.ClientId, LiveDtoFactory.SelfFrame(simDriverId: "2"));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        socket.PushClose();
        await running;
        collected.AddRange(socket.DrainViewMessages());

        // The viewer is watching the room this telemetry belongs to and the key matches — the only
        // thing keeping it out is that the focus was cleared on the switch.
        collected.OfType<FocusFrameMessage>().ShouldBeEmpty();
    }

    /// <summary>
    /// Unlike a publisher's undecodable frame, this does not end the connection. A browser's
    /// commands are discrete requests rather than a position in a stream, so one bad command costs
    /// that command and nothing else.
    /// </summary>
    [Fact]
    public async Task A_malformed_command_is_answered_without_dropping_the_connection()
    {
        var hub = new LiveHubFixture();
        var socket = new FakeLiveSocket();
        socket.PushText("{ not json");

        var messages = await RunAsync(hub, socket, expectedMessages: 2);

        messages.OfType<LiveErrorMessage>().ShouldHaveSingleItem().Code.ShouldBe(LiveErrorCodes.MalformedCommand);
        socket.ClosedWith.ShouldBeNull();
    }

    /// <summary>
    /// Nothing a real dashboard sends comes close to the cap, so this is a broken or hostile client
    /// — and the endpoint is the one anybody on the internet can open.
    /// </summary>
    [Fact]
    public async Task An_oversized_command_closes_the_connection()
    {
        var hub = new LiveHubFixture();
        var socket = new FakeLiveSocket();
        socket.FailNextReceiveAsTooLarge = true;

        var running = hub.CreateViewerSession().RunAsync(socket, TestContext.Current.CancellationToken);
        await running;

        socket.ClosedWith.ShouldBe(WebSocketCloseStatus.MessageTooBig);
    }

    /// <summary>
    /// The guarantee the whole design exists for: a viewer that has stopped reading loses frames,
    /// and the publisher's path stays clear. If the hub blocked here, a single browser on a bad
    /// connection would stall the live view for everyone in the room.
    /// </summary>
    [Fact]
    public async Task A_viewer_that_stops_reading_loses_frames_instead_of_stalling_the_publisher()
    {
        var hub = new LiveHubFixture();
        var identity = LiveDtoFactory.Identity();
        var room = hub.AnnounceRoom(identity, localSimDriverId: "1");

        var socket = new FakeLiveSocket();
        socket.PushCommand(new WatchRoomCommand(room.RoomId));

        var running = hub.CreateViewerSession().RunAsync(socket, TestContext.Current.CancellationToken);
        await socket.WaitForSendsAsync(1);

        socket.BlockSends();

        // A thousand snapshots into a viewer that is not reading. Each of these returns immediately
        // or the test would never finish.
        for (int i = 0; i < 1_000; i++)
        {
            hub.Rooms.ApplyStandings(identity.ClientId, LiveDtoFactory.StandingsFrame(driverCount: 20));
        }

        // Released and given time to drain everything it holds *before* the socket closes. Closing
        // straight away would end the send pump early and make this pass on a queue that had
        // buffered all thousand — the count would be low because the pump was cut off, not because
        // the frames were conflated.
        socket.ReleaseSends();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        socket.PushClose();
        await running;

        // Nowhere near a thousand: the queue holds one tower snapshot, so the backlog collapsed
        // into the newest rather than accumulating.
        socket.DrainViewMessages().OfType<TowerSnapshotMessage>().Count().ShouldBeLessThan(10);
    }
}
