using System.Buffers;
using System.Net.WebSockets;

namespace RaceIntelligence.Web.Live;

/// <summary>
/// Adapts an ASP.NET Core <see cref="WebSocket"/> to <see cref="ILiveSocket"/>: reassembles
/// fragments into whole messages and enforces a size cap while doing so.
/// </summary>
/// <remarks>
/// <para>
/// The cap is checked <b>as fragments arrive</b>, not after reassembly. Checking afterwards would
/// require having already buffered the whole thing, which is precisely what an unauthenticated
/// viewer socket must not be able to make the hub do.
/// </para>
/// <para>
/// Not thread-safe, and does not need to be. Each socket has exactly one reader and one writer:
/// the session's receive loop and its send pump. A <see cref="WebSocket"/> permits one concurrent
/// send and one concurrent receive, which is the same shape.
/// </para>
/// </remarks>
public sealed class WebSocketLiveSocket(WebSocket socket, int maxMessageBytes) : ILiveSocket
{
    /// <summary>
    /// Size of one read from the socket. Not the message limit — a message larger than this is
    /// simply delivered in several fragments and reassembled.
    /// </summary>
    private const int ReceiveChunkBytes = 8 * 1024;

    private readonly byte[] _receiveChunk = ArrayPool<byte>.Shared.Rent(ReceiveChunkBytes);
    private bool _disposed;

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken)
    {
        ArrayBufferWriter<byte>? assembled = null;

        while (true)
        {
            // The Memory<byte> overload returns a ValueWebSocketReceiveResult — a struct, so this
            // loop allocates nothing per fragment.
            ValueWebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(_receiveChunk.AsMemory(0, ReceiveChunkBytes), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                // An abruptly dropped connection — a gaming PC going to sleep, a tunnel restarting.
                // Routine on this path, and indistinguishable from a clean close to every caller.
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            // The common case by a wide margin: a live frame is a few kilobytes and arrives whole.
            // Handled without ever allocating an assembly buffer.
            if (result.EndOfMessage && assembled is null)
            {
                if (result.Count > maxMessageBytes)
                {
                    throw new LiveSocketMessageTooLargeException(maxMessageBytes);
                }

                return _receiveChunk.AsMemory(0, result.Count);
            }

            assembled ??= new ArrayBufferWriter<byte>(ReceiveChunkBytes);

            if (assembled.WrittenCount + result.Count > maxMessageBytes)
            {
                throw new LiveSocketMessageTooLargeException(maxMessageBytes);
            }

            assembled.Write(_receiveChunk.AsSpan(0, result.Count));

            if (result.EndOfMessage)
            {
                return assembled.WrittenMemory;
            }
        }
    }

    /// <inheritdoc />
    public ValueTask SendAsync(ReadOnlyMemory<byte> payload, bool binary, CancellationToken cancellationToken) =>
        socket.SendAsync(
            payload,
            binary ? WebSocketMessageType.Binary : WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);

    /// <inheritdoc />
    public async ValueTask CloseAsync(
        WebSocketCloseStatus status,
        string description,
        CancellationToken cancellationToken)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        try
        {
            await socket.CloseAsync(status, description, cancellationToken).ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            // The peer is already gone. Nothing to tell it, and nothing the caller can do.
        }
        catch (OperationCanceledException)
        {
            // Shutting down. The socket is about to be disposed regardless.
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            ArrayPool<byte>.Shared.Return(_receiveChunk);
        }

        return ValueTask.CompletedTask;
    }
}
