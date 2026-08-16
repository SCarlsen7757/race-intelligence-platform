using System.Threading.Channels;
using RaceIntelligence.Live.Contracts.View;

namespace RaceIntelligence.Web.Live;

/// <summary>
/// One viewer's outbound queue: reliable for messages that say something unrepeatable, conflating
/// for the streams that describe a moment.
/// </summary>
/// <remarks>
/// <para>
/// The mirror image of the collector's <c>LiveOutbox</c>, and for the same reason. A viewer on a
/// phone over mobile data reads slower than a race produces frames, and the only two options are
/// to slow the race down or to skip frames. Skipping is not a degraded mode here — the newest
/// tower snapshot is strictly more useful than the one before it, so the frames being dropped are
/// exactly the ones nobody wanted.
/// </para>
/// <para>
/// <b>Nothing here ever blocks.</b> The caller is a publisher's receive loop, fanning one standings
/// frame out to every viewer watching the room. A single stalled viewer that could block that loop
/// would stall the room for everyone else in it — and, upstream of that, would stop the hub reading
/// from the collector's socket at all.
/// </para>
/// <para>
/// Conflation while a send is in flight falls out of the design rather than needing its own
/// mechanism: the send pump takes a message, awaits the socket, and only then looks again. Whatever
/// arrived during that await has already collapsed into the slots.
/// </para>
/// </remarks>
public sealed class ViewerQueue
{
    /// <summary>
    /// How many errors may queue before the oldest are dropped.
    /// </summary>
    /// <remarks>
    /// Errors answer a viewer's own commands, so the real rate is bounded by how fast a person can
    /// click. The capacity is a backstop against a scripted client, not a limit normal use reaches.
    /// </remarks>
    private const int ErrorCapacity = 8;

    private readonly Channel<LiveViewMessage> _errors =
        Channel.CreateBounded<LiveViewMessage>(new BoundedChannelOptions(ErrorCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    /// <summary>
    /// A one-slot wake signal. <see cref="BoundedChannelFullMode.DropWrite"/> makes a write a safe
    /// no-op when a wake is already pending, so a producer never throws and never waits.
    /// </summary>
    private readonly Channel<byte> _wake =
        Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    private RoomListMessage? _latestRoomList;
    private TowerSnapshotMessage? _latestTower;
    private FocusFrameMessage? _latestFocus;

    private long _droppedTower;
    private long _droppedFocus;

    /// <summary>Frames superseded before this viewer could be sent them — how far behind it is running.</summary>
    public (long Tower, long Focus) DroppedFrames =>
        (Interlocked.Read(ref _droppedTower), Interlocked.Read(ref _droppedFocus));

    /// <summary>Offers the current room list, replacing any not yet sent.</summary>
    public void OfferRoomList(RoomListMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        Volatile.Write(ref _latestRoomList, message);
        Wake();
    }

    /// <summary>Offers a timing tower snapshot, replacing any not yet sent.</summary>
    public void OfferTower(TowerSnapshotMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (Interlocked.Exchange(ref _latestTower, message) is not null)
        {
            Interlocked.Increment(ref _droppedTower);
        }

        Wake();
    }

    /// <summary>Offers a focus frame, replacing any not yet sent.</summary>
    public void OfferFocus(FocusFrameMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (Interlocked.Exchange(ref _latestFocus, message) is not null)
        {
            Interlocked.Increment(ref _droppedFocus);
        }

        Wake();
    }

    /// <summary>
    /// Queues an error. Unlike the data streams these accumulate, because each one answers a
    /// different command and a later error does not restate an earlier one.
    /// </summary>
    public void OfferError(LiveErrorMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_errors.Writer.TryWrite(message))
        {
            Wake();
        }
    }

    /// <summary>
    /// Discards any focus frame not yet sent.
    /// </summary>
    /// <remarks>
    /// Called when a viewer changes or clears its focus. Without this, the frame already waiting
    /// would still be delivered — one frame of the previous driver's telemetry arriving after the
    /// switch, which reads as a glitch in the new driver's traces.
    /// </remarks>
    public void ClearFocus() => Interlocked.Exchange(ref _latestFocus, null);

    /// <summary>Takes the next message to send, waiting until one exists.</summary>
    /// <remarks>
    /// Priority is errors, then the room list, then the tower, then the focus stream. The tower
    /// outranks focus because it arrives at a tenth of the rate: preferring the 60 Hz stream would
    /// let a slow viewer starve its timing tower completely, while a focus frame skipped now is
    /// replaced within milliseconds.
    /// </remarks>
    public async ValueTask<LiveViewMessage> ReadAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (TryRead() is { } message)
            {
                return message;
            }

            await _wake.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Takes the next message if one is ready, without waiting.</summary>
    public LiveViewMessage? TryRead()
    {
        if (_errors.Reader.TryRead(out var error))
        {
            return error;
        }

        if (Interlocked.Exchange(ref _latestRoomList, null) is { } roomList)
        {
            return roomList;
        }

        if (Interlocked.Exchange(ref _latestTower, null) is { } tower)
        {
            return tower;
        }

        return Interlocked.Exchange(ref _latestFocus, null);
    }

    private void Wake() => _wake.Writer.TryWrite(0);
}
