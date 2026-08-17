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

    /// <summary>
    /// The newest focus frame per driver, conflating within a driver but never across drivers.
    /// </summary>
    /// <remarks>
    /// One slot per driver rather than one in total, because a viewer may be comparing two cars and
    /// a single slot would interleave them into one stream of alternating samples — each car's
    /// traces then showing every other frame of the other's. Conflation within a driver is the same
    /// bargain the single slot always made: the newest frame is strictly more useful than the one it
    /// replaced.
    /// </remarks>
    private readonly ConcurrentDictionary<string, FocusFrameMessage> _focusFrames =
        new(StringComparer.Ordinal);

    /// <summary>The newest extras document per driver, following the focus slots.</summary>
    private readonly ConcurrentDictionary<string, ExtrasFrameMessage> _extras =
        new(StringComparer.Ordinal);

    private RoomListMessage? _latestRoomList;
    private TowerSnapshotMessage? _latestTower;

    private long _droppedTower;
    private long _droppedFocus;

    /// <summary>
    /// Where the next scan of the focus slots begins, so two streams share the pump evenly.
    /// </summary>
    /// <remarks>
    /// The lap-history loop needs no such thing — a history arrives about once a minute per driver,
    /// so whichever the dictionary hands back first is always the only one waiting. Focus frames
    /// arrive sixty times a second per driver, and always scanning from the same end would let the
    /// first driver's slot refill before the second was ever reached, starving it completely.
    /// </remarks>
    private int _focusCursor;

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

    /// <summary>Offers a focus frame, replacing any for the same driver not yet sent.</summary>
    public void OfferFocus(FocusFrameMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        // TryAdd first rather than AddOrUpdate: the update factory would have to close over a local
        // to report the replacement, and a closure allocated sixty times a second per focused driver
        // is exactly the garbage the rest of this path avoids. A frame the reader took between the
        // two calls is counted as dropped when it was not — the counter is a diagnostic, and
        // understanding it as "roughly how far behind this viewer ran" survives being off by one.
        if (!_focusFrames.TryAdd(message.DriverKey, message))
        {
            _focusFrames[message.DriverKey] = message;
            Interlocked.Increment(ref _droppedFocus);
        }

        Wake();
    }

    /// <summary>Offers a focused driver's extras document, replacing any of theirs not yet sent.</summary>
    public void OfferExtras(ExtrasFrameMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        _extras[message.DriverKey] = message;
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
    /// Discards anything not yet sent for one driver a viewer has stopped following.
    /// </summary>
    /// <remarks>
    /// Without this, the frame already waiting would still be delivered — one sample of a car the
    /// viewer has dropped, arriving after the drop and reading as a glitch. Named, so unfocusing one
    /// of two compared drivers costs the other nothing.
    /// </remarks>
    public void ClearFocus(string driverKey)
    {
        ArgumentNullException.ThrowIfNull(driverKey);

        _focusFrames.TryRemove(driverKey, out _);

        // The extras slot follows the focus for the same reason. A damage panel showing a dropped
        // driver's car is worse than one showing nothing, because it looks current.
        _extras.TryRemove(driverKey, out _);
    }

    /// <summary>Discards every focus frame and extras document not yet sent — a viewer leaving a room.</summary>
    public void ClearFocus()
    {
        _focusFrames.Clear();
        _extras.Clear();
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

        if (TakeNextFocus() is { } focus)
        {
            return focus;
        }

        // Extras need no rotation: at roughly 1 Hz per driver there is never a backlog for a scan to
        // be unfair about, and taking whichever is ready cannot starve the other.
        foreach (var driverKey in _extras.Keys)
        {
            if (_extras.TryRemove(driverKey, out var extras))
            {
                return extras;
            }
        }

        return null;
    }

    /// <summary>
    /// Takes one focus frame, starting the scan where the last one left off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rotation is what keeps two 60 Hz streams sharing one pump. Scanning from a fixed end
    /// would return the first driver every single time — their slot refills within a frame of being
    /// drained — and the second driver's traces would never advance at all.
    /// </para>
    /// <para>
    /// One pass, and no snapshot of the keys: this runs once per message sent, and a fresh array per
    /// send is the kind of garbage the rest of this path is built to avoid. The cursor is a position
    /// rather than a key so it stays meaningful when the focused set changes underneath it.
    /// </para>
    /// </remarks>
    private FocusFrameMessage? TakeNextFocus()
    {
        var count = _focusFrames.Count;
        if (count == 0)
        {
            return null;
        }

        var start = (int)((uint)_focusCursor % (uint)count);

        string? chosen = null;
        string? first = null;
        var index = 0;

        foreach (var slot in _focusFrames)
        {
            if (index >= start)
            {
                chosen = slot.Key;
                break;
            }

            // Remembered so a cursor past the end of a shrunken set wraps rather than finding
            // nothing.
            first ??= slot.Key;
            index++;
        }

        chosen ??= first;

        if (chosen is not null && _focusFrames.TryRemove(chosen, out var message))
        {
            _focusCursor = start + 1;
            return message;
        }

        // The chosen slot was emptied under us, which only a viewer dropping a focus can do. Falling
        // back to any waiting frame keeps the pump from returning empty-handed while one is ready.
        foreach (var driverKey in _focusFrames.Keys)
        {
            if (_focusFrames.TryRemove(driverKey, out var fallback))
            {
                return fallback;
            }
        }

        return null;
    }

    private void Wake() => _wake.Writer.TryWrite(0);
}
