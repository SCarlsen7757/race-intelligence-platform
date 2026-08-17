using Microsoft.Extensions.Options;
using RaceIntelligence.Live.Contracts.View;

namespace RaceIntelligence.Web.Live;

/// <summary>
/// Maps the hub's two WebSocket endpoints and the room list.
/// </summary>
/// <remarks>
/// <b>The two sockets have opposite auth postures, and that is the whole reason they are separate
/// endpoints rather than one with a mode flag.</b> Publishing writes what every race engineer's
/// decisions are based on and needs a key; viewing is read-only and open. Making that split
/// structural means it cannot be lost in a refactor of a shared handler.
/// </remarks>
public static class LiveEndpoints
{
    /// <summary>Where a collector connects to publish. Must match the collector's own path constant.</summary>
    public const string PublishPath = "/live/publish";

    /// <summary>Where a browser connects to watch. Open.</summary>
    public const string ViewPath = "/live/view";

    /// <summary>Registers the live endpoints.</summary>
    public static void MapLiveEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Map(PublishPath, PublishAsync);
        app.Map(ViewPath, ViewAsync);

        // A plain read of the same data the viewing socket opens with, so the dashboard's landing
        // page can render before it has a socket, and so the hub can be checked with curl.
        //
        // Serialized through the shared view options and as the base type, so this is byte-identical
        // to what arrives over the socket — including the "type" discriminator. A browser that had
        // to parse two shapes of the same message would need two code paths for one thing.
        //
        // Under the dashboard's CORS policy, because the dashboard is a separate origin now. The
        // socket needs no such policy — CORS does not apply to a WebSocket upgrade — which is why
        // the origin check for that lives on WebSocketOptions instead. Two mechanisms, one list.
        app.MapGet("/api/v1/live/rooms", (LiveRoomRegistry rooms) =>
            Results.Json<LiveViewMessage>(rooms.BuildRoomList(), LiveViewJson.Default))
            .RequireCors(LiveHubRegistration.DashboardCorsPolicy);
    }

    private static async Task PublishAsync(
        HttpContext context,
        LiveApiKeyGate apiKeyGate,
        PublisherSession session,
        IOptions<LiveHubOptions> options)
    {
        // Checked before the upgrade is accepted. Once the socket has been accepted the response is
        // already committed and there is no longer a 401 to return.
        if (!apiKeyGate.IsValid(context.Request.Headers[LiveApiKeyGate.HeaderName]))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        await using var socket = new WebSocketLiveSocket(webSocket, options.Value.MaxPublisherMessageBytes);

        await session.RunAsync(socket, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task ViewAsync(HttpContext context, ViewerSession session)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);

        // A much smaller cap than a publisher's: a viewer only ever sends two tiny commands, and
        // this endpoint is the one anybody on the internet can open.
        await using var socket = new WebSocketLiveSocket(webSocket, LiveViewJson.MaxCommandBytes);

        await session.RunAsync(socket, context.RequestAborted).ConfigureAwait(false);
    }
}
