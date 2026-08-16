using System.Net.WebSockets;

namespace RaceIntelligence.Web.Live;

/// <summary>
/// The hub's view of one connected socket: whole messages in, whole messages out.
/// </summary>
/// <remarks>
/// <para>
/// An abstraction over <see cref="WebSocket"/> rather than direct use of it, for one reason that
/// justifies the indirection on its own: the behaviour that matters most on this path is what
/// happens under backpressure — a viewer that stops reading must cause frames to be dropped and
/// must never stall a publisher. Provoking that through a real socket means filling an OS send
/// buffer, which is slow, platform-dependent and flaky. Against this interface a test simply
/// declines to complete a send.
/// </para>
/// <para>
/// It also keeps message framing in exactly one place. A WebSocket delivers fragments, not
/// messages, and every consumer that reassembles them itself is another place to get the size cap
/// or the continuation flag wrong.
/// </para>
/// </remarks>
public interface ILiveSocket : IAsyncDisposable
{
    /// <summary>
    /// Reads the next whole message.
    /// </summary>
    /// <returns>
    /// The message payload, or <see langword="null"/> once the peer has closed the connection.
    /// </returns>
    /// <remarks>
    /// <b>The returned memory is only valid until the next call.</b> Implementations are free to
    /// hand back a reused buffer, and the real one does — a live frame arriving whole is the
    /// overwhelmingly common case, and copying every one of them to hand out ownership would
    /// allocate on the hottest path in the hub for no benefit. Callers decode immediately and keep
    /// the decoded object, never the bytes.
    /// </remarks>
    /// <exception cref="LiveSocketMessageTooLargeException">
    /// The peer sent a message larger than this socket's configured limit. The caller is expected
    /// to close the connection rather than resynchronise: a message that overran the cap has been
    /// discarded, so what follows cannot be interpreted.
    /// </exception>
    ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>Sends one whole message.</summary>
    /// <param name="payload">The bytes to send.</param>
    /// <param name="binary">
    /// <see langword="true"/> for a binary frame (MessagePack, to a collector),
    /// <see langword="false"/> for a text frame (JSON, to a browser). Browsers surface the two
    /// differently — a text frame arrives as a string and a binary one as a Blob — so this is not
    /// cosmetic.
    /// </param>
    ValueTask SendAsync(ReadOnlyMemory<byte> payload, bool binary, CancellationToken cancellationToken);

    /// <summary>
    /// Closes the connection, telling the peer why.
    /// </summary>
    /// <remarks>
    /// Never throws. A close is always the last thing that happens on a connection, and a peer that
    /// has already vanished is the ordinary case rather than an error worth propagating — the
    /// caller has nothing useful to do with the exception either way.
    /// </remarks>
    ValueTask CloseAsync(WebSocketCloseStatus status, string description, CancellationToken cancellationToken);
}

/// <summary>Thrown when a peer sends a message larger than the socket's configured limit.</summary>
public sealed class LiveSocketMessageTooLargeException(int limitBytes)
    : Exception($"The peer sent a message larger than the {limitBytes} byte limit.")
{
    /// <summary>The limit that was exceeded, in bytes.</summary>
    public int LimitBytes { get; } = limitBytes;
}
