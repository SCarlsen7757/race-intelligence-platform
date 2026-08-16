using RaceIntelligence.Live.Contracts.Publish;

namespace RaceIntelligence.Collector.Live;

/// <summary>
/// One connection to the live hub, for the lifetime of which frames can be sent.
/// </summary>
/// <remarks>
/// The seam that lets <see cref="LivePublishService"/>'s reconnect and drain logic be tested
/// without a hub, a socket, or a network — the same role
/// <c>Connectors.RaceRoom.Interop.ISharedMemoryView</c> plays for the shared-memory reader.
/// Production uses <see cref="LiveWebSocketConnection"/>.
/// </remarks>
public interface ILiveConnection : IAsyncDisposable
{
    /// <summary>
    /// Sends one message. Throws when the connection has failed, which is how the caller learns to
    /// reconnect.
    /// </summary>
    ValueTask SendAsync(LivePublisherMessage message, CancellationToken cancellationToken);
}

/// <summary>Opens connections to the live hub.</summary>
/// <remarks>
/// Separate from <see cref="ILiveConnection"/> because reconnecting means getting a <i>new</i>
/// connection: a failed WebSocket cannot be reopened, only replaced.
/// </remarks>
public interface ILiveConnectionFactory
{
    /// <summary>
    /// Opens a connection and completes the handshake, so a returned connection is one the hub has
    /// accepted.
    /// </summary>
    /// <remarks>
    /// The handshake — sending <see cref="LiveHello"/> — happens here rather than in the caller so
    /// that a hub which rejects this client's schema version or key fails the connect, not the
    /// first frame. The caller's retry loop then treats it like any other connection failure.
    /// </remarks>
    Task<ILiveConnection> ConnectAsync(CancellationToken cancellationToken);
}
