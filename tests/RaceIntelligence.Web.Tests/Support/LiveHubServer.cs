using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MessagePack;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceIntelligence.Live.Contracts.Publish;
using RaceIntelligence.Live.Contracts.View;
using RaceIntelligence.Web.Live;
using Shouldly;

namespace RaceIntelligence.Web.Tests.Support;

/// <summary>
/// The real hub on a real port, for the handful of tests that have to cross an actual WebSocket.
/// </summary>
/// <remarks>
/// <para>
/// Built with <see cref="LiveHubRegistration.AddLiveHub"/> — the same call <c>Program.cs</c> makes —
/// so this exercises the graph the application actually assembles rather than a re-declaration of
/// it that could drift.
/// </para>
/// <para>
/// Most hub behaviour is tested against <see cref="FakeLiveSocket"/> instead, and should be: it is
/// faster and can provoke a stalled reader deterministically. What only a real socket covers is the
/// part underneath — the upgrade, the API key check on it, and
/// <see cref="WebSocketLiveSocket"/>'s framing — none of which a fake can vouch for.
/// </para>
/// </remarks>
public sealed class LiveHubServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private LiveHubServer(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    /// <summary>The address the server bound to, including the port the OS chose.</summary>
    public Uri BaseAddress { get; }

    /// <summary>The publishing key this server was started with.</summary>
    public const string ApiKey = "end-to-end-key";

    /// <summary>Starts the hub on a free loopback port.</summary>
    public static async Task<LiveHubServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Live:ApiKey"] = ApiKey,
        });

        // Port 0 lets the OS pick, so parallel test runs cannot collide on a fixed one.
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddLiveHub(builder.Configuration);

        var app = builder.Build();
        app.UseLiveWebSockets();
        app.MapLiveEndpoints();

        await app.StartAsync();

        string address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return new LiveHubServer(app, new Uri(address));
    }

    /// <summary>Opens a publishing socket, optionally with a wrong or missing key.</summary>
    public async Task<ClientWebSocket> ConnectPublisherAsync(string? apiKey = ApiKey)
    {
        var socket = new ClientWebSocket();
        if (apiKey is not null)
        {
            socket.Options.SetRequestHeader(LiveApiKeyGate.HeaderName, apiKey);
        }

        await socket.ConnectAsync(SocketUri(LiveEndpoints.PublishPath), TestContext.Current.CancellationToken);
        return socket;
    }

    /// <summary>Opens a viewing socket. No key — the endpoint is open.</summary>
    public async Task<ClientWebSocket> ConnectViewerAsync()
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(SocketUri(LiveEndpoints.ViewPath), TestContext.Current.CancellationToken);
        return socket;
    }

    /// <summary>Sends a MessagePack publisher message over a real socket.</summary>
    public static Task SendAsync(ClientWebSocket socket, LivePublisherMessage message) =>
        socket.SendAsync(
            MessagePackSerializer.Serialize(message, LiveMessagePackOptions.Default),
            WebSocketMessageType.Binary,
            endOfMessage: true,
            TestContext.Current.CancellationToken);

    /// <summary>Sends a JSON viewer command over a real socket.</summary>
    public static Task SendAsync(ClientWebSocket socket, LiveViewCommand command) =>
        socket.SendAsync(
            JsonSerializer.SerializeToUtf8Bytes(command, LiveViewJson.Default),
            WebSocketMessageType.Text,
            endOfMessage: true,
            TestContext.Current.CancellationToken);

    /// <summary>Reads view messages from a real socket until one matches, then returns it.</summary>
    /// <remarks>
    /// Bounded by its own timeout rather than relying on the run-wide one. A routing bug means no
    /// message ever arrives, and an unbounded receive would turn that into a hung suite instead of
    /// a named failing test.
    /// </remarks>
    public static async Task<T> ReceiveUntilAsync<T>(ClientWebSocket socket, Func<T, bool>? predicate = null)
        where T : LiveViewMessage
    {
        byte[] buffer = new byte[64 * 1024];

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        for (int i = 0; i < 50; i++)
        {
            ValueWebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    $"The hub sent no {typeof(T).Name} within the timeout.");
            }

            result.MessageType.ShouldNotBe(WebSocketMessageType.Close, "the hub closed the viewing socket.");

            // Text, not binary: browsers surface the two differently, and a Blob where a string was
            // expected is a bug the dashboard would only find at runtime.
            result.MessageType.ShouldBe(WebSocketMessageType.Text);

            var message = JsonSerializer.Deserialize<LiveViewMessage>(
                buffer.AsSpan(0, result.Count), LiveViewJson.Default);

            if (message is T typed && (predicate is null || predicate(typed)))
            {
                return typed;
            }
        }

        throw new InvalidOperationException($"The hub never sent a {typeof(T).Name}.");
    }

    /// <summary>Reads the room list over plain HTTP, as the dashboard's first paint does.</summary>
    public async Task<string> GetRoomListJsonAsync()
    {
        using var client = new HttpClient { BaseAddress = BaseAddress };
        return await client.GetStringAsync("api/v1/live/rooms", TestContext.Current.CancellationToken);
    }

    private Uri SocketUri(string path) =>
        new UriBuilder(BaseAddress) { Scheme = "ws", Path = path }.Uri;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>Encoding helper for the oversized-command test.</summary>
    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
}
