using System.Net.WebSockets;
using RaceIntelligence.Live.Contracts;
using RaceIntelligence.Live.Contracts.Publish;
using RaceIntelligence.Live.Contracts.View;
using RaceIntelligence.Web.Live;
using RaceIntelligence.Web.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Web.Tests.Live;

/// <summary>
/// Covers the hub's side of a collector's publishing connection: the handshake, what a frame does,
/// and what happens when the socket goes away.
/// </summary>
public sealed class PublisherSessionTests
{
    [Fact]
    public async Task A_hello_then_a_session_frame_opens_a_room()
    {
        var hub = new LiveHubFixture();
        var socket = new FakeLiveSocket();
        var clientId = Guid.NewGuid();

        socket.Push(LiveDtoFactory.Hello(clientId));
        socket.Push(LiveDtoFactory.SessionFrame(track: "Monza"));
        socket.Push(LiveDtoFactory.StandingsFrame(driverCount: 5));
        socket.PushClose();

        await hub.CreatePublisherSession().RunAsync(socket, TestContext.Current.CancellationToken);

        // The publisher has disconnected, so the room has no publishers — but it survives its
        // publisher leaving, which is what a reconnect depends on.
        var room = hub.Rooms.BuildRoomList().Rooms.ShouldHaveSingleItem();
        room.TrackName.ShouldBe("Monza");
    }

    /// <summary>
    /// Rejected at the handshake rather than on the first frame, so a client built against a schema
    /// this hub does not understand is told immediately and with a reason — instead of appearing to
    /// connect and then silently publishing nothing.
    /// </summary>
    [Fact]
    public async Task A_client_speaking_an_unsupported_schema_is_refused_with_a_reason()
    {
        var hub = new LiveHubFixture();
        var socket = new FakeLiveSocket();

        socket.Push(LiveDtoFactory.Hello(schemaVersion: 99));

        await hub.CreatePublisherSession().RunAsync(socket, TestContext.Current.CancellationToken);

        socket.ClosedWith.ShouldBe(WebSocketCloseStatus.PolicyViolation);
        socket.CloseDescription.ShouldNotBeNull().ShouldContain("99");
        hub.Rooms.Count.ShouldBe(0);
    }

    /// <summary>
    /// The specific mismatch the schema bump to 2 exists to catch. A version 1 collector's frames
    /// would decode against this hub right up until it sent an extras frame, whose union key it has
    /// no member for — so the failure is moved forward into the handshake, where it names both
    /// versions instead of arriving as a decode error mid-race.
    /// </summary>
    [Fact]
    public async Task A_version_1_collector_is_refused_by_a_version_2_hub()
    {
        var hub = new LiveHubFixture();
        var socket = new FakeLiveSocket();

        socket.Push(LiveDtoFactory.Hello(schemaVersion: 1));

        await hub.CreatePublisherSession().RunAsync(socket, TestContext.Current.CancellationToken);

        socket.ClosedWith.ShouldBe(WebSocketCloseStatus.PolicyViolation);
        socket.CloseDescription.ShouldNotBeNull()
            .ShouldContain("1", Case.Sensitive, "the refusal names the version the client speaks");
        socket.CloseDescription.ShouldContain(
            LiveSchemaVersion.Current.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Case.Sensitive,
            "and the version this hub speaks, so the operator knows what to upgrade to");
        hub.Rooms.Count.ShouldBe(0);
    }

    [Fact]
    public async Task A_connection_that_does_not_open_with_a_hello_is_refused()
    {
        var hub = new LiveHubFixture();
        var socket = new FakeLiveSocket();

        socket.Push(LiveDtoFactory.SessionFrame());

        await hub.CreatePublisherSession().RunAsync(socket, TestContext.Current.CancellationToken);

        socket.ClosedWith.ShouldBe(WebSocketCloseStatus.PolicyViolation);
        hub.Rooms.Count.ShouldBe(0);
    }

    /// <summary>
    /// Undecodable bytes leave the stream position untrustworthy, so there is nothing to
    /// resynchronise to — the connection ends and the collector's own retry loop reconnects.
    /// </summary>
    [Fact]
    public async Task An_undecodable_frame_closes_the_connection()
    {
        var hub = new LiveHubFixture();
        var socket = new FakeLiveSocket();

        socket.Push(LiveDtoFactory.Hello());
        socket.Push([0xC1, 0xC1, 0xC1]);

        await hub.CreatePublisherSession().RunAsync(socket, TestContext.Current.CancellationToken);

        socket.ClosedWith.ShouldBe(WebSocketCloseStatus.InvalidPayloadData);
    }

    [Fact]
    public async Task An_oversized_message_closes_the_connection()
    {
        var hub = new LiveHubFixture();
        var socket = new FakeLiveSocket();

        socket.Push(LiveDtoFactory.Hello());
        socket.FailNextReceiveAsTooLarge = true;

        await hub.CreatePublisherSession().RunAsync(socket, TestContext.Current.CancellationToken);

        socket.ClosedWith.ShouldBe(WebSocketCloseStatus.MessageTooBig);
    }

    /// <summary>
    /// A goodbye ends the session, not the connection: the collector stays connected and announces
    /// the next session on the same socket.
    /// </summary>
    [Fact]
    public async Task A_goodbye_removes_the_publisher_but_leaves_the_connection_open()
    {
        var hub = new LiveHubFixture();
        var socket = new FakeLiveSocket();
        var clientId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        socket.Push(LiveDtoFactory.Hello(clientId));
        socket.Push(LiveDtoFactory.SessionFrame(sessionId, sessionType: 2));
        socket.Push(new LiveGoodbye(sessionId, "session ended"));
        socket.Push(LiveDtoFactory.SessionFrame(sessionType: 3));
        socket.PushClose();

        await hub.CreatePublisherSession().RunAsync(socket, TestContext.Current.CancellationToken);

        // Both sessions were seen on the one connection.
        hub.Rooms.BuildRoomList().Rooms.Select(room => room.SessionType).ShouldBe([2, 3], ignoreOrder: true);
    }

    /// <summary>
    /// Nothing in a hello can change mid-connection, so there is nothing to apply — and nothing
    /// worth dropping a live race's telemetry over either.
    /// </summary>
    [Fact]
    public async Task A_duplicate_hello_is_ignored_rather_than_ending_the_connection()
    {
        var hub = new LiveHubFixture();
        var socket = new FakeLiveSocket();

        socket.Push(LiveDtoFactory.Hello());
        socket.Push(LiveDtoFactory.Hello());
        socket.Push(LiveDtoFactory.SessionFrame());
        socket.PushClose();

        await hub.CreatePublisherSession().RunAsync(socket, TestContext.Current.CancellationToken);

        socket.ClosedWith.ShouldBeNull();
        hub.Rooms.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_dropped_connection_removes_the_publisher_from_its_room()
    {
        var hub = new LiveHubFixture();
        var socket = new FakeLiveSocket();

        socket.Push(LiveDtoFactory.Hello());
        socket.Push(LiveDtoFactory.SessionFrame());
        socket.PushClose();

        await hub.CreatePublisherSession().RunAsync(socket, TestContext.Current.CancellationToken);

        hub.Rooms.BuildRoomList().Rooms.ShouldHaveSingleItem().Publishers.ShouldBeEmpty();
    }

    /// <summary>
    /// The end-to-end shape of the live path, from the bytes a collector writes to the message a
    /// browser reads.
    /// </summary>
    [Fact]
    public async Task Standings_published_on_the_socket_reach_a_watching_viewer()
    {
        var hub = new LiveHubFixture();
        var socket = new FakeLiveSocket();
        var clientId = Guid.NewGuid();

        socket.Push(LiveDtoFactory.Hello(clientId));
        socket.Push(LiveDtoFactory.SessionFrame(localSimDriverId: "2"));

        // Announce first, so the viewer can subscribe to a room that exists before standings arrive.
        var publisher = hub.CreatePublisherSession();
        var running = publisher.RunAsync(socket, TestContext.Current.CancellationToken);

        for (int i = 0; i < 200 && hub.Rooms.Count == 0; i++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        hub.Rooms.Count.ShouldBe(1, "the session announcement should have opened a room.");
        var viewer = hub.AddViewer(hub.Rooms.BuildRoomList().Rooms.Single().RoomId);

        socket.Push(LiveDtoFactory.StandingsFrame(driverCount: 3));
        socket.Push(LiveDtoFactory.SelfFrame(simDriverId: "2"));

        TowerSnapshotMessage? tower = null;
        for (int i = 0; i < 200 && tower is null; i++)
        {
            tower = viewer.Queue.TryRead() as TowerSnapshotMessage;
            if (tower is null)
            {
                await Task.Delay(5, TestContext.Current.CancellationToken);
            }
        }

        socket.PushClose();
        await running;

        tower.ShouldNotBeNull();
        tower.Drivers.Count.ShouldBe(3);
        tower.Drivers.Single(row => row.DriverKey == "id:2").Tier.ShouldBe(LiveDataTier.Self);
    }
}
