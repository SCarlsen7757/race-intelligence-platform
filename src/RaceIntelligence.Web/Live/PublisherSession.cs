using System.Net.WebSockets;
using MessagePack;
using RaceIntelligence.Live.Contracts;
using RaceIntelligence.Live.Contracts.Publish;

namespace RaceIntelligence.Web.Live;

/// <summary>
/// The hub's side of one collector's publishing connection: handshake, then a receive loop.
/// </summary>
/// <remarks>
/// <para>
/// Receive-only after the handshake. The hub sends a collector nothing but a close frame, which is
/// what keeps this loop as tight as it is — there is no send pump, no queue, and no way for a slow
/// hub-to-client path to delay reading the next frame.
/// </para>
/// <para>
/// <b>Nothing in the dispatch blocks.</b> Every registry call fans out through per-viewer
/// conflating queues that drop rather than wait, so a viewer on a bad connection cannot slow this
/// loop down. If it could, the collector's socket buffer would fill, and the collector would start
/// dropping frames it could otherwise have sent — one stalled browser degrading the live view for
/// everyone.
/// </para>
/// </remarks>
public sealed class PublisherSession(
    LiveRoomRegistry rooms,
    TimeProvider timeProvider,
    ILogger<PublisherSession> logger)
{
    /// <summary>
    /// Runs the connection until the collector disconnects or the host shuts down.
    /// </summary>
    public async Task RunAsync(ILiveSocket socket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(socket);

        LivePublisherIdentity? identity = null;

        try
        {
            identity = await HandshakeAsync(socket, cancellationToken).ConfigureAwait(false);
            if (identity is null)
            {
                return;
            }

            logger.LogInformation(
                "Publisher {ClientName} ({ClientId}) connected, running client version {ClientVersion}.",
                identity.ClientName, identity.ClientId, identity.ClientVersion);

            await PumpAsync(socket, identity, cancellationToken).ConfigureAwait(false);
        }
        catch (LiveSocketMessageTooLargeException ex)
        {
            logger.LogWarning("A publisher sent an oversized message; closing the connection. {Message}", ex.Message);
            await CloseQuietlyAsync(socket, WebSocketCloseStatus.MessageTooBig, "Message too large.").ConfigureAwait(false);
        }
        catch (MessagePackSerializationException ex)
        {
            // Undecodable bytes mean the stream position is no longer trustworthy, so there is
            // nothing to resynchronise to — the connection ends and the collector reconnects.
            logger.LogWarning(ex, "A publisher sent a frame that could not be decoded; closing the connection.");
            await CloseQuietlyAsync(socket, WebSocketCloseStatus.InvalidPayloadData, "Malformed frame.").ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is shutting down.
        }
        finally
        {
            if (identity is not null)
            {
                rooms.RemovePublisher(identity.ClientId);
                logger.LogInformation(
                    "Publisher {ClientName} ({ClientId}) disconnected.", identity.ClientName, identity.ClientId);
            }
        }
    }

    /// <summary>
    /// Reads the opening <see cref="LiveHello"/> and accepts or refuses the connection.
    /// </summary>
    /// <remarks>
    /// The version check happens here rather than on the first data frame, so a client built
    /// against a schema this hub does not understand is told so immediately and with a reason,
    /// instead of appearing to connect and then silently publishing nothing.
    /// </remarks>
    private async Task<LivePublisherIdentity?> HandshakeAsync(ILiveSocket socket, CancellationToken cancellationToken)
    {
        var payload = await socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        if (payload is null)
        {
            return null;
        }

        var message = Deserialize(payload.Value);
        if (message is not LiveHello hello)
        {
            logger.LogWarning(
                "A publisher opened with {MessageType} instead of a hello; closing the connection.",
                message.GetType().Name);
            await CloseQuietlyAsync(socket, WebSocketCloseStatus.PolicyViolation, "Expected a hello.").ConfigureAwait(false);
            return null;
        }

        if (!LiveSchemaVersion.IsSupported(hello.SchemaVersion))
        {
            logger.LogWarning(
                "Publisher {ClientName} speaks live schema {SchemaVersion}; this hub speaks {Supported}. Refusing.",
                hello.ClientName, hello.SchemaVersion, LiveSchemaVersion.Current);
            await CloseQuietlyAsync(
                socket,
                WebSocketCloseStatus.PolicyViolation,
                $"Unsupported live schema version {hello.SchemaVersion}; this hub supports {LiveSchemaVersion.Current}.")
                .ConfigureAwait(false);
            return null;
        }

        return new LivePublisherIdentity(
            hello.ClientId,
            hello.ClientName,
            hello.ClientVersion,
            timeProvider.GetUtcNow(),
            hello.Capabilities);
    }

    private async Task PumpAsync(ILiveSocket socket, LivePublisherIdentity identity, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var payload = await socket.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (payload is null)
            {
                return;
            }

            Dispatch(identity, Deserialize(payload.Value));
        }
    }

    private void Dispatch(LivePublisherIdentity identity, LivePublisherMessage message)
    {
        switch (message)
        {
            case LiveSessionFrame session:
                rooms.Announce(identity, session);
                break;

            case LiveStandingsFrame standings:
                rooms.ApplyStandings(identity.ClientId, standings);
                break;

            case LiveSelfFrame self:
                rooms.ApplySelf(identity.ClientId, self);
                break;

            case LiveStintFrame stint:
                rooms.ApplyStint(identity.ClientId, stint);
                break;

            case LiveExtrasFrame extras:
                rooms.ApplyExtras(identity.ClientId, extras);
                break;

            case LiveGoodbye goodbye:
                // The publisher is leaving the session, not the connection: it stays connected and
                // will announce the next session on the same socket.
                logger.LogInformation(
                    "Publisher {ClientId} ended session {SessionId}. {Reason}",
                    identity.ClientId, goodbye.SessionId, goodbye.Reason ?? "No reason given.");
                rooms.RemovePublisher(identity.ClientId);
                break;

            case LiveHello:
                // A second hello on an established connection. Ignored rather than treated as an
                // error: nothing in it can change mid-connection, so there is nothing to apply and
                // nothing worth dropping a live race's telemetry over.
                logger.LogDebug("Publisher {ClientId} sent a duplicate hello; ignoring.", identity.ClientId);
                break;

            default:
                // A message type added to the union by a newer client. Skipping it is the correct
                // reading of a versioned wire format: the hub already accepted the schema version,
                // and refusing everything it does not recognise would break forward compatibility
                // the union was designed to allow.
                logger.LogDebug(
                    "Publisher {ClientId} sent an unhandled {MessageType}; ignoring.",
                    identity.ClientId, message.GetType().Name);
                break;
        }
    }

    private static LivePublisherMessage Deserialize(ReadOnlyMemory<byte> payload) =>
        MessagePackSerializer.Deserialize<LivePublisherMessage>(payload, LiveMessagePackOptions.Default);

    private static async Task CloseQuietlyAsync(ILiveSocket socket, WebSocketCloseStatus status, string description)
    {
        // Not the request's cancellation token: it is already cancelled in some of the paths that
        // get here, and a close frame the peer never receives leaves it waiting for a timeout
        // instead of reconnecting. Bounded so a peer that has stopped reading cannot hang shutdown.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await socket.CloseAsync(status, description, timeout.Token).ConfigureAwait(false);
    }
}
