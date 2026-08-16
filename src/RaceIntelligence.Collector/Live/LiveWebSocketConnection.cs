using System.Net.WebSockets;
using System.Reflection;
using MessagePack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaceIntelligence.Live.Contracts;
using RaceIntelligence.Live.Contracts.Publish;

namespace RaceIntelligence.Collector.Live;

/// <summary>
/// A publishing connection to the live hub over a WebSocket.
/// </summary>
/// <remarks>
/// <para>
/// <b>Outbound only, and that is the whole point.</b> The collector runs on a gaming PC behind a
/// home router; the hub runs on a server the driver controls. Having the collector dial out means
/// nothing needs forwarding, no firewall rule is added to the machine the driver plays on, and NAT
/// is simply not involved. It is also why this is a WebSocket rather than the UDP broadcast the
/// idea usually starts as: UDP does not leave the subnet, does not survive NAT, and does not pass
/// through an HTTP tunnel.
/// </para>
/// <para>
/// One frame per message, MessagePack-encoded, no fragmentation: live frames are a few kilobytes
/// at most.
/// </para>
/// </remarks>
public sealed class LiveWebSocketConnection : ILiveConnection
{
    private readonly WebSocket _socket;
    private readonly ILogger _logger;

    /// <summary>
    /// Serializes sends. A WebSocket permits only one send at a time, and violating that faults the
    /// socket rather than interleaving — cheap insurance even though the drain loop is the only
    /// caller today.
    /// </summary>
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    internal LiveWebSocketConnection(WebSocket socket, ILogger logger)
    {
        _socket = socket;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask SendAsync(LivePublisherMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        byte[] payload = MessagePackSerializer.Serialize(message, LiveMessagePackOptions.Default, cancellationToken);

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(payload, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                // A brief, bounded close handshake. A hub that has already gone away will not
                // answer, and waiting on it would stall collector shutdown for nothing.
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, statusDescription: null, closeTimeout.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            _logger.LogDebug(ex, "The live connection did not close cleanly; disposing it anyway.");
        }
        finally
        {
            _socket.Dispose();
            _sendLock.Dispose();
        }
    }
}

/// <summary>Opens <see cref="LiveWebSocketConnection"/>s from <see cref="LiveOptions"/>.</summary>
public sealed class LiveWebSocketConnectionFactory(
    IOptions<CollectorOptions> options,
    ILogger<LiveWebSocketConnectionFactory> logger) : ILiveConnectionFactory
{
    /// <summary>The hub path a collector publishes to. The viewing endpoint is separate and open.</summary>
    private const string PublishPath = "live/publish";

    private readonly LiveOptions _live = options.Value.Live;

    /// <summary>
    /// This installation's stable id. Generated once per process when unconfigured, so at least a
    /// single run is consistent — see <see cref="LiveOptions.ClientId"/>.
    /// </summary>
    private readonly Guid _clientId = options.Value.Live.ClientId ?? Guid.NewGuid();

    /// <inheritdoc />
    public async Task<ILiveConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        try
        {
            socket.Options.SetRequestHeader("X-Api-Key", _live.ApiKey);

            await socket.ConnectAsync(BuildUri(), cancellationToken).ConfigureAwait(false);

            var connection = new LiveWebSocketConnection(socket, logger);

            // Announce before returning, so a hub that rejects this client's schema version or key
            // fails here rather than on the first frame — the caller's retry loop then treats it
            // like any other connection failure instead of publishing into a void.
            await connection.SendAsync(
                new LiveHello(
                    LiveSchemaVersion.Current,
                    _clientId,
                    _live.ClientName ?? Environment.MachineName,
                    ClientVersion,
                    GameKey,
                    Capabilities),
                cancellationToken).ConfigureAwait(false);

            logger.LogInformation("Publishing live telemetry to {Uri} as client {ClientId}.", BuildUri(), _clientId);
            return connection;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Turns the configured HTTP base address into the WebSocket URL of the publishing endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scheme is switched here rather than being configured as <c>ws</c>/<c>wss</c> so the hub's
    /// address is written the same way every other endpoint in the configuration is.
    /// </para>
    /// <para>
    /// <b>Service discovery does not apply to this address.</b> A <see cref="ClientWebSocket"/> dials
    /// the URI directly and never passes through an <c>HttpClient</c>, which is where
    /// ServiceDefaults attaches the resolving handler — so a logical name such as
    /// <c>https+http://web/</c> would reach DNS unresolved and fail. Unlike
    /// <c>Collector:Ingest:BaseUrl</c>, this must be given a real address; the AppHost supplies the
    /// hub's resolved endpoint for exactly this reason.
    /// </para>
    /// </remarks>
    private Uri BuildUri()
    {
        var http = new Uri(new Uri(_live.BaseUrl), PublishPath);
        var builder = new UriBuilder(http)
        {
            Scheme = http.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
        };

        return builder.Uri;
    }

    /// <summary>
    /// Set by the collector at startup. A property rather than a constructor parameter because the
    /// simulator is chosen in <c>Program.cs</c>, and threading it through configuration would make
    /// it settable to something that disagrees with the connector actually registered.
    /// </summary>
    public string GameKey { get; init; } = string.Empty;

    /// <summary>The connector's declared <see cref="Core.Capabilities.SimCapabilities"/>, as a bitmask.</summary>
    public ulong Capabilities { get; init; }

    private static string ClientVersion { get; } =
        typeof(LiveWebSocketConnectionFactory).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";
}
