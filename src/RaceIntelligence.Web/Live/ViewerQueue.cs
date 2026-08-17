using System.Collections.Concurrent;
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

    /// <summary>
    /// The newest lap history per driver, conflating within a driver but never across drivers.
    /// </summary>
    /// <remarks>
    /// One slot per driver rather than one slot in total, because a viewer may have several rows
    /// expanded and a single slot would let a busy driver's history evict another's indefinitely.
    /// Conflation within a driver is free of consequence: each message is a full history, so the one
    /// that survives contains everything the ones it replaced said.
    /// </remarks>
    private readonly ConcurrentDictionary<string, LapHistoryMessage> _lapHistories =
        new(StringComparer.Ordinal);

    private RoomListMessage? _latestRoomList;
    private TowerSnapshotMessage? _latestTower;
    private FocusFrameMessage? _latestFocus;
    private ExtrasFrameMessage? _latestExtras;

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

    /// <summary>Offers the focused driver's extras document, replacing any not yet sent.</summary>
    public void OfferExtras(ExtrasFrameMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        Interlocked.Exchange(ref _latestExtras, message);
        Wake();
    }

    /// <summary>
    /// Offers a driver's lap history, replacing any for the same driver not yet sent.
    /// </summary>
    /// <remarks>
    /// No drop counter, deliberately, where the tower and focus streams have one. Those count
    /// frames a viewer never saw; a replaced lap history is not a frame missed, because the message
    /// that replaced it restates every lap the old one carried. Counting it would report a viewer as
    /// falling behind when it has lost nothing.
    /// </remarks>
    public void OfferLapHistory(LapHistoryMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        _lapHistories[message.DriverKey] = message;
        Wake();
    }

    /// <summary>Discards any lap history for a driver this viewer has stopped following.</summary>
    public void ClearLapHistory(string driverKey)
    {
        ArgumentNullException.ThrowIfNull(driverKey);

        _lapHistories.TryRemove(driverKey, out _);
    }

    /// <summary>Discards every lap history not yet sent — a viewer leaving a room, or collapsing everything.</summary>
    public void ClearLapHistory() => _lapHistories.Clear();

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
    public void ClearFocus()
    {
        Interlocked.Exchange(ref _latestFocus, null);

        // The extras slot follows the focus for the same reason. A damage panel showing the previous
        // driver's car after a switch is worse than one showing nothing, because it looks current.
        Interlocked.Exchange(ref _latestExtras, null);
    }

    /// <summary>Takes the next message to send, waiting until one exists.</summary>
    /// <remarks>
    /// Priority is errors, then the room list, then the tower, then lap histories, then the focus
    /// stream, then extras. The ordering is by how replaceable each message is rather than by
    /// importance: a focus frame skipped now is replaced within milliseconds, a tower snapshot
    /// within a tenth of a second, and a lap history not until the driver finishes another lap — so
    /// preferring the fastest stream would let a slow viewer starve the slower ones indefinitely.
    /// Lap history sits below the tower only because the tower is what makes a session legible at
    /// all.
    /// <para>
    /// Extras sit at the bottom, below even the 60 Hz stream, mirroring the collector's outbox.
    /// A once-a-second document interrupting the trace a race engineer is reading, to deliver a
    /// number that will look the same next second, is the one trade this ladder is not worth making.
    /// </para>
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

        // No ordering between drivers: each message is independent and complete, so whichever the
        // dictionary hands back first is as good an answer as any. TryRemove is what makes taking
        // one safe against another thread offering a newer history for the same driver — the loser
        // of that race is the one this viewer never needed.
        foreach (var driverKey in _lapHistories.Keys)
        {
            if (_lapHistories.TryRemove(driverKey, out var lapHistory))
            {
                return lapHistory;
            }
        }

        if (Interlocked.Exchange(ref _latestFocus, null) is { } focus)
        {
            return focus;
        }

        return Interlocked.Exchange(ref _latestExtras, null);
    }

    private void Wake() => _wake.Writer.TryWrite(0);
}
