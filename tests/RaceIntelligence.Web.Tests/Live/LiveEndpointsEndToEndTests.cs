using System.Net;
using System.Net.WebSockets;
using RaceIntelligence.Live.Contracts.View;
using RaceIntelligence.Web.Live;
using RaceIntelligence.Web.Tests.Support;
using Shouldly;

namespace RaceIntelligence.Web.Tests.Live;

/// <summary>
/// The spine end to end, over real sockets: a collector publishes, a race engineer watches.
/// </summary>
/// <remarks>
/// Deliberately few. Everything about the hub's behaviour is faster and more precisely stated
/// against <see cref="FakeLiveSocket"/>; what these add is the layer underneath it — the HTTP
/// upgrade, the key check on that upgrade, and real WebSocket framing — which no fake can vouch
/// for.
/// </remarks>
public sealed class LiveEndpointsEndToEndTests
{
    /// <summary>
    /// The whole first slice in one test: bytes from a collector become a timing tower in a browser.
    /// </summary>
    [Fact]
    public async Task A_published_session_becomes_a_timing_tower_in_a_browser()
    {
        await using var server = await LiveHubServer.StartAsync();

        using var publisher = await server.ConnectPublisherAsync();
        await LiveHubServer.SendAsync(publisher, LiveDtoFactory.Hello());
        await LiveHubServer.SendAsync(publisher, LiveDtoFactory.SessionFrame(track: "Spa", localSimDriverId: "2"));

        using var viewer = await server.ConnectViewerAsync();

        // The room list arrives unprompted — it is the dashboard's landing view.
        var rooms = await LiveHubServer.ReceiveUntilAsync<RoomListMessage>(
            viewer, message => message.Rooms.Count > 0);
        var room = rooms.Rooms.ShouldHaveSingleItem();
        room.TrackName.ShouldBe("Spa");

        await LiveHubServer.SendAsync(viewer, new WatchRoomCommand(room.RoomId));
        await LiveHubServer.SendAsync(publisher, LiveDtoFactory.StandingsFrame(driverCount: 3));

        var tower = await LiveHubServer.ReceiveUntilAsync<TowerSnapshotMessage>(
            viewer, message => message.Drivers.Count == 3);

        // The driver whose own machine is publishing is the one that can be opened into a telemetry
        // panel, and the tier is how the dashboard knows.
        tower.Drivers.Single(row => row.DriverKey == "id:2").Tier.ShouldBe(LiveDataTier.Self);
        tower.Drivers.Count(row => row.Tier == LiveDataTier.Observed).ShouldBe(2);
    }

    /// <summary>
    /// The rich channels no observer can see, arriving at the publisher's full rate for the one
    /// driver a viewer selected.
    /// </summary>
    [Fact]
    public async Task Focusing_a_publishing_driver_streams_their_pedals_and_tyres()
    {
        await using var server = await LiveHubServer.StartAsync();

        using var publisher = await server.ConnectPublisherAsync();
        await LiveHubServer.SendAsync(publisher, LiveDtoFactory.Hello());
        await LiveHubServer.SendAsync(publisher, LiveDtoFactory.SessionFrame(localSimDriverId: "2"));
        await LiveHubServer.SendAsync(publisher, LiveDtoFactory.StandingsFrame(driverCount: 3));

        using var viewer = await server.ConnectViewerAsync();
        var rooms = await LiveHubServer.ReceiveUntilAsync<RoomListMessage>(
            viewer, message => message.Rooms.Count > 0);

        await LiveHubServer.SendAsync(viewer, new WatchRoomCommand(rooms.Rooms[0].RoomId));
        await LiveHubServer.ReceiveUntilAsync<TowerSnapshotMessage>(viewer);
        await LiveHubServer.SendAsync(viewer, new FocusDriverCommand("id:2"));

        // Published repeatedly: the focus command and this frame race, and only frames sent after
        // the subscription lands are routed. A real collector publishes at 60 Hz, so the first one
        // to miss costs 16 ms.
        FocusFrameMessage? focus = null;
        for (int i = 0; i < 20 && focus is null; i++)
        {
            await LiveHubServer.SendAsync(publisher, LiveDtoFactory.SelfFrame(simDriverId: "2"));
            focus = await LiveHubServer.ReceiveUntilAsync<LiveViewMessage>(viewer) as FocusFrameMessage;
        }

        focus.ShouldNotBeNull();
        focus.DriverKey.ShouldBe("id:2");
        focus.Throttle.ShouldBe(1f);
        focus.TyreWear.Count.ShouldBe(4);
        focus.TyrePressureKpa[0].ShouldBe(180f);
    }

    /// <summary>
    /// The upgrade itself is refused, before any socket exists. Checking after
    /// <c>AcceptWebSocketAsync</c> would be too late — the response is committed by then and there
    /// is no 401 left to return.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Publishing_without_the_right_key_is_refused_at_the_upgrade(string? apiKey)
    {
        await using var server = await LiveHubServer.StartAsync();

        var connecting = server.ConnectPublisherAsync(apiKey);

        var failure = await Should.ThrowAsync<WebSocketException>(connecting);
        failure.Message.ShouldContain("401");
    }

    /// <summary>Viewing needs no key at all — that is the whole point of the split.</summary>
    [Fact]
    public async Task Viewing_needs_no_key()
    {
        await using var server = await LiveHubServer.StartAsync();

        using var viewer = await server.ConnectViewerAsync();

        (await LiveHubServer.ReceiveUntilAsync<RoomListMessage>(viewer)).Rooms.ShouldBeEmpty();
    }

    /// <summary>
    /// The REST room list is the same message the socket opens with, so the dashboard can paint
    /// before it has a socket without needing a second parser.
    /// </summary>
    [Fact]
    public async Task The_room_list_is_readable_over_plain_http_in_the_same_shape()
    {
        await using var server = await LiveHubServer.StartAsync();

        using var publisher = await server.ConnectPublisherAsync();
        await LiveHubServer.SendAsync(publisher, LiveDtoFactory.Hello());
        await LiveHubServer.SendAsync(publisher, LiveDtoFactory.SessionFrame(track: "Monza"));

        // The publisher's frames cross a real socket, so give the hub a moment to apply them.
        string json = string.Empty;
        for (int i = 0; i < 100 && !json.Contains("Monza", StringComparison.Ordinal); i++)
        {
            json = await server.GetRoomListJsonAsync();
            if (!json.Contains("Monza", StringComparison.Ordinal))
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
        }

        json.ShouldContain("\"type\":\"roomList\"");
        json.ShouldContain("Monza");
    }

    /// <summary>
    /// A malformed upgrade is a 400, not an unhandled exception — anyone can reach the viewing
    /// endpoint with a plain browser request.
    /// </summary>
    [Fact]
    public async Task A_plain_get_on_a_socket_endpoint_is_a_bad_request()
    {
        await using var server = await LiveHubServer.StartAsync();

        using var client = new HttpClient { BaseAddress = server.BaseAddress };
        var response = await client.GetAsync(
            LiveEndpoints.ViewPath.TrimStart('/'), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
