using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MessagePack;
using RaceIntelligence.Live.Contracts.Publish;
using RaceIntelligence.Live.Contracts.View;
using RaceIntelligence.Web.Live;
using Shouldly;

namespace RaceIntelligence.Web.Tests.Support;

/// <summary>
/// A socket a test drives directly: messages are pushed in, sent messages are read back, and a
/// send can be made to hang on demand.
/// </summary>
/// <remarks>
/// The last of those is why this exists rather than a real loopback WebSocket. The behaviour worth
/// testing hardest on this path is what the hub does when a viewer stops reading, and provoking
/// that through a real socket means filling an OS buffer of unspecified size — slow, platform
/// dependent, and flaky. Here it is one flag.
/// </remarks>
public sealed class FakeLiveSocket : ILiveSocket
{
    private readonly Channel<ReadOnlyMemory<byte>?> _inbound = Channel.CreateUnbounded<ReadOnlyMemory<byte>?>();
    private readonly Channel<byte[]> _outbound = Channel.CreateUnbounded<byte[]>();

    private TaskCompletionSource _sendGate = Completed();

    /// <summary>Set to have <see cref="ReceiveAsync"/> throw as an oversized message would.</summary>
    public bool FailNextReceiveAsTooLarge { get; set; }

    /// <summary>The close status the hub asked for, if it has closed the socket.</summary>
    public WebSocketCloseStatus? ClosedWith { get; private set; }

    /// <summary>The description sent with the close.</summary>
    public string? CloseDescription { get; private set; }

    /// <summary>How many messages the hub has sent.</summary>
    public int SentCount => _outbound.Reader.Count;

    /// <summary>Queues a MessagePack publisher message for the hub to receive.</summary>
    public void Push(LivePublisherMessage message) =>
        Push(MessagePackSerializer.Serialize(message, LiveMessagePackOptions.Default));

    /// <summary>Queues raw bytes for the hub to receive.</summary>
    public void Push(byte[] payload) => _inbound.Writer.TryWrite(payload);

    /// <summary>Queues a JSON viewer command for the hub to receive.</summary>
    public void PushCommand(LiveViewCommand command) =>
        Push(JsonSerializer.SerializeToUtf8Bytes(command, LiveViewJson.Default));

    /// <summary>Queues raw text for the hub to receive, for malformed-input tests.</summary>
    public void PushText(string text) => Push(Encoding.UTF8.GetBytes(text));

    /// <summary>Signals that the peer has disconnected, ending the hub's receive loop.</summary>
    public void PushClose() => _inbound.Writer.TryWrite(null);

    /// <summary>
    /// Blocks the hub's send pump until <see cref="ReleaseSends"/>. Models a viewer that has stopped
    /// reading — the condition every conflation guarantee is about.
    /// </summary>
    public void BlockSends() => _sendGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Lets a blocked send complete.</summary>
    public void ReleaseSends() => _sendGate.TrySetResult();

    /// <summary>Waits for the hub to have sent at least <paramref name="count"/> messages.</summary>
    public async Task WaitForSendsAsync(int count)
    {
        for (int i = 0; i < 400 && _outbound.Reader.Count < count; i++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        _outbound.Reader.Count.ShouldBeGreaterThanOrEqualTo(count, "the hub did not send what was expected.");
    }

    /// <summary>Reads the next sent message, decoded as a view message.</summary>
    public async Task<LiveViewMessage> ReadViewMessageAsync()
    {
        byte[] payload = await _outbound.Reader.ReadAsync(TestContext.Current.CancellationToken);
        return JsonSerializer.Deserialize<LiveViewMessage>(payload, LiveViewJson.Default)
            ?? throw new InvalidOperationException("The hub sent a null view message.");
    }

    /// <summary>
    /// Collects sent messages until one matches <paramref name="predicate"/>, and returns
    /// everything collected.
    /// </summary>
    /// <remarks>
    /// The way to wait for the hub to have reached a state, in preference to counting sends.
    /// Counting is unreliable by design: the outbound queue conflates, so two tower snapshots
    /// offered in quick succession legitimately arrive as one, and a test that expected three
    /// messages would fail intermittently for the very reason the queue exists.
    /// </remarks>
    public async Task<List<LiveViewMessage>> CollectUntilAsync(Func<LiveViewMessage, bool> predicate)
    {
        var collected = new List<LiveViewMessage>();

        for (int i = 0; i < 400; i++)
        {
            collected.AddRange(DrainViewMessages());
            if (collected.Any(predicate))
            {
                return collected;
            }

            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        collected.Any(predicate).ShouldBeTrue("the hub never sent the message the test was waiting for.");
        return collected;
    }

    /// <summary>Reads every message sent so far, without waiting for more.</summary>
    public IReadOnlyList<LiveViewMessage> DrainViewMessages()
    {
        var messages = new List<LiveViewMessage>();
        while (_outbound.Reader.TryRead(out byte[]? payload))
        {
            var message = JsonSerializer.Deserialize<LiveViewMessage>(payload, LiveViewJson.Default);
            if (message is not null)
            {
                messages.Add(message);
            }
        }

        return messages;
    }

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken)
    {
        if (FailNextReceiveAsTooLarge)
        {
            FailNextReceiveAsTooLarge = false;
            throw new LiveSocketMessageTooLargeException(1024);
        }

        return await _inbound.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask SendAsync(ReadOnlyMemory<byte> payload, bool binary, CancellationToken cancellationToken)
    {
        // Copied because the hub reuses its serialization buffer between sends, exactly as a real
        // socket's caller may. Holding the memory would make every recorded message show the last
        // one's contents.
        byte[] copy = payload.ToArray();

        await _sendGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        _outbound.Writer.TryWrite(copy);
    }

    /// <inheritdoc />
    public ValueTask CloseAsync(WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
    {
        ClosedWith = status;
        CloseDescription = description;
        _inbound.Writer.TryWrite(null);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static TaskCompletionSource Completed()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
