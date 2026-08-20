using System.Net;
using System.Net.WebSockets;
using Microsoft.Extensions.Options;
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

        // Published continuously in the background, the way a collector does, because the focus
        // command and the frames race: only frames sent after the subscription lands are routed,
        // and a single frame sent at the wrong moment is simply dropped. At 60 Hz a real collector
        // loses nothing but 16 ms to that; a test that published once would lose the whole run.
        using var publishing = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var pump = Task.Run(
            async () =>
            {
                while (!publishing.IsCancellationRequested)
                {
                    await LiveHubServer.SendAsync(publisher, LiveDtoFactory.SelfFrame(simDriverId: "2"));
                    await Task.Delay(16, publishing.Token);
                }
            },
            publishing.Token);

        var focus = await LiveHubServer.ReceiveUntilAsync<FocusFrameMessage>(viewer);

        await publishing.CancelAsync();
        await pump.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
            .ContinueWith(_ => { }, TaskScheduler.Default);

        focus.ShouldNotBeNull();
        focus.DriverKey.ShouldBe("id:2");
        focus.Throttle.ShouldBe(1f);
        focus.BrakePressureKiloNewtons.Count.ShouldBe(4);
        focus.BrakePressureKiloNewtons[0].ShouldBe(3.1f);
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

    /// <summary>
    /// The viewing socket is open — no key — so the origin check is the only thing standing between
    /// a session's live timing and any page that cares to open a socket at it. Before the dashboard
    /// moved to its own origin this was unset, which means every origin was accepted.
    /// </summary>
    [Fact]
    public async Task A_browser_on_an_unlisted_origin_is_refused_the_upgrade()
    {
        await using var server = await LiveHubServer.StartAsync();

        var refused = await Should.ThrowAsync<WebSocketException>(
            () => server.ConnectViewerAsync("http://not-the-dashboard.test"));

        refused.Message.ShouldContain("403");
    }

    /// <summary>
    /// The configured origin still gets in, and a client that sends no <c>Origin</c> at all — a
    /// collector, curl, a test harness — is unaffected. Origin is a browser's self-report about
    /// which page opened the connection; it says nothing about a program, and treating its absence
    /// as a rejection would break every non-browser client for no gain.
    /// </summary>
    [Fact]
    public async Task The_configured_origin_and_clients_that_send_none_are_both_accepted()
    {
        await using var server = await LiveHubServer.StartAsync();

        using var dashboard = await server.ConnectViewerAsync(LiveHubServer.AllowedOrigin);
        dashboard.State.ShouldBe(WebSocketState.Open);

        using var collector = await server.ConnectViewerAsync();
        collector.State.ShouldBe(WebSocketState.Open);
    }

    /// <summary>
    /// The room list is the dashboard's first paint, and it now happens across origins. Without the
    /// header the browser discards a perfectly good 200 and the landing page stays empty.
    /// </summary>
    [Fact]
    public async Task The_room_list_carries_a_cors_header_for_the_dashboard_origin()
    {
        await using var server = await LiveHubServer.StartAsync();

        using var client = new HttpClient { BaseAddress = server.BaseAddress };
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/live/rooms");
        request.Headers.Add("Origin", LiveHubServer.AllowedOrigin);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("Access-Control-Allow-Origin")
            .ShouldHaveSingleItem()
            .ShouldBe(LiveHubServer.AllowedOrigin);
    }

    /// <summary>
    /// And an unlisted origin gets no such header, so the browser refuses to hand the body to the
    /// page. The response is still a 200 — CORS is enforced in the browser, not by the server — so
    /// the absence of the header is the whole assertion.
    /// </summary>
    [Fact]
    public async Task The_room_list_carries_no_cors_header_for_an_unlisted_origin()
    {
        await using var server = await LiveHubServer.StartAsync();

        using var client = new HttpClient { BaseAddress = server.BaseAddress };
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/live/rooms");
        request.Headers.Add("Origin", "http://not-the-dashboard.test");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    /// <summary>
    /// An empty origin list means "accept every origin" as far as ASP.NET Core is concerned, so a
    /// hub that forgot the setting would look configured and be open to every page on the internet.
    /// Refusing to start is the honest outcome, and the same call already made for the API key.
    /// </summary>
    [Fact]
    public async Task The_hub_refuses_to_start_with_no_allowed_origins()
    {
        var failure = await Should.ThrowAsync<OptionsValidationException>(
            () => LiveHubServer.StartAsync(new Dictionary<string, string?>
            {
                ["Live:AllowedOrigins:0"] = null,
            }));

        failure.Message.ShouldContain("Live:AllowedOrigins");
    }

    /// <summary>
    /// The hub serves no UI any more. A request for anything that is not an API or a socket used to
    /// come back as 200 and a page of HTML — including a typo in a fetch URL, which is a far worse
    /// thing to debug than a status code.
    /// </summary>
    [Fact]
    public async Task An_unmatched_route_is_a_not_found_rather_than_an_spa_fallback()
    {
        await using var server = await LiveHubServer.StartAsync();

        using var client = new HttpClient { BaseAddress = server.BaseAddress };
        var response = await client.GetAsync("rooms/abc123", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
