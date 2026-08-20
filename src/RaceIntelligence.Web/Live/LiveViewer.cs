using System.Collections.Concurrent;
using RaceIntelligence.Live.Contracts.View;

namespace RaceIntelligence.Web.Live;

/// <summary>
/// One connected browser: what it has asked to see, and the queue feeding it.
/// </summary>
/// <remarks>
/// A viewer holds room and driver <i>keys</i>, never references to the room objects themselves.
/// Rooms come and go — a session ends, a room expires — and a viewer holding a live reference would
/// keep the whole thing alive after the last publisher had gone. Resolving by key means a
/// subscription to a room that has since vanished simply stops producing frames, which is exactly
/// what should happen.
/// </remarks>
public sealed class LiveViewer
{
    /// <summary>
    /// How many drivers one viewer may follow at full rate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two, because a comparison needs exactly two and the number is a cost, not a preference. Focus
    /// frames are the 60 Hz channel; the two-rate transport exists precisely so they are not
    /// broadcast widely, and each additional subscription multiplies this viewer's share of the
    /// hub's serialize-and-send work by a whole stream.
    /// </para>
    /// <para>
    /// Stated as a constant rather than left to fall out of however many the dashboard happens to
    /// ask for: the endpoint is open, so the bound has to hold against a client that is not the
    /// dashboard.
    /// </para>
    /// </remarks>
    public const int MaxFocusDrivers = 2;

    /// <summary>
    /// Drivers whose completed laps this viewer has asked for, used as a set.
    /// </summary>
    /// <remarks>
    /// Concurrent because the two sides run on different loops: the command loop adds and removes
    /// as rows are expanded, while every publisher's receive loop reads it to decide whether a lap
    /// history belongs to this viewer.
    /// </remarks>
    private readonly ConcurrentDictionary<string, byte> _lapHistoryDriverKeys =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Drivers whose full-rate channels this viewer is following, used as a set.
    /// </summary>
    /// <remarks>
    /// A set rather than a slot so two cars can be read side by side — see
    /// <see cref="MaxFocusDrivers"/> for why it is a small set. Concurrent for the same reason the
    /// lap-history set is: the command loop writes it while every publisher's receive loop reads it.
    /// </remarks>
    private readonly ConcurrentDictionary<string, byte> _focusDriverKeys =
        new(StringComparer.Ordinal);

    private string? _roomId;

    /// <summary>The queue this viewer's send pump drains.</summary>
    public ViewerQueue Queue { get; } = new();

    /// <summary>The room whose timing tower this viewer is watching, if any.</summary>
    public string? RoomId => Volatile.Read(ref _roomId);

    /// <summary>How many drivers this viewer is currently following at full rate.</summary>
    public int FocusCount => _focusDriverKeys.Count;

    /// <summary>Whether this viewer is following a driver's full-rate channels.</summary>
    public bool IsFocused(string driverKey)
    {
        ArgumentNullException.ThrowIfNull(driverKey);

        return _focusDriverKeys.ContainsKey(driverKey);
    }

    /// <summary>
    /// Switches which room this viewer watches, clearing any focus that belonged to the old one.
    /// </summary>
    /// <remarks>
    /// The focus and every lap-history subscription are cleared rather than carried over, because a
    /// driver key is only meaningful within a room. Keeping them would either resolve to nobody or,
    /// worse, to a different driver who happened to share an id — and the viewer would be shown
    /// someone else's telemetry, or someone else's stint, under the name it had selected.
    /// </remarks>
    public void WatchRoom(string? roomId)
    {
        Volatile.Write(ref _roomId, roomId);
        UnfocusAll();
        UnsubscribeAllLapHistory();

        // The old room's lap length and pit window are as wrong for the new room as its telemetry
        // would be, and unlike a tower row a banner carries nothing on screen that would look out of
        // place against the session the viewer has actually switched to.
        Queue.ClearSessionState();
    }

    /// <summary>
    /// Adds a driver to the set this viewer follows at full rate.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the viewer already follows <see cref="MaxFocusDrivers"/> others,
    /// so the caller can answer rather than silently doing nothing.
    /// </returns>
    public bool Focus(string driverKey)
    {
        ArgumentNullException.ThrowIfNull(driverKey);

        // Checked before the add, and re-stating an existing focus is always allowed: a dashboard
        // replaying its subscriptions after a reconnect must not be refused for asking for what it
        // already has.
        if (!_focusDriverKeys.ContainsKey(driverKey) && _focusDriverKeys.Count >= MaxFocusDrivers)
        {
            return false;
        }

        _focusDriverKeys[driverKey] = 0;
        return true;
    }

    /// <summary>Removes one driver from that set, dropping any frame already waiting for them.</summary>
    public void Unfocus(string driverKey)
    {
        ArgumentNullException.ThrowIfNull(driverKey);

        _focusDriverKeys.TryRemove(driverKey, out _);

        // Delivered after the drop, a frame already in the queue would appear as a stray sample of a
        // car the viewer has stopped watching. Named, so the other focus keeps every frame it has.
        Queue.ClearFocus(driverKey);
    }

    /// <summary>Drops every focus subscription.</summary>
    public void UnfocusAll()
    {
        _focusDriverKeys.Clear();
        Queue.ClearFocus();
    }

    /// <summary>Adds a driver to the set whose completed laps this viewer receives.</summary>
    public void SubscribeLapHistory(string driverKey)
    {
        ArgumentNullException.ThrowIfNull(driverKey);

        _lapHistoryDriverKeys[driverKey] = 0;
    }

    /// <summary>Removes one driver from that set, dropping anything already waiting for them.</summary>
    public void UnsubscribeLapHistory(string driverKey)
    {
        ArgumentNullException.ThrowIfNull(driverKey);

        _lapHistoryDriverKeys.TryRemove(driverKey, out _);
        Queue.ClearLapHistory(driverKey);
    }

    /// <summary>Drops every lap-history subscription.</summary>
    public void UnsubscribeAllLapHistory()
    {
        _lapHistoryDriverKeys.Clear();
        Queue.ClearLapHistory();
    }

    /// <summary>Whether this viewer has asked for a driver's completed laps.</summary>
    public bool IsSubscribedToLapHistory(string driverKey) => _lapHistoryDriverKeys.ContainsKey(driverKey);

    /// <summary>Offers a tower snapshot, if this viewer is watching the room it describes.</summary>
    public void OfferTower(TowerSnapshotMessage snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (string.Equals(RoomId, snapshot.RoomId, StringComparison.Ordinal))
        {
            Queue.OfferTower(snapshot);
        }
    }

    /// <summary>Offers the session's state, if this viewer is watching the room it describes.</summary>
    public void OfferSessionState(SessionStateMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.Equals(RoomId, message.RoomId, StringComparison.Ordinal))
        {
            Queue.OfferSessionState(message);
        }
    }

    /// <summary>Offers a focus frame, if this viewer is following the driver it describes.</summary>
    public void OfferFocus(FocusFrameMessage frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        // Both must match. Checking only the driver key would send a viewer frames from a driver
        // with the same key in a room it is not watching, which is possible whenever two sessions
        // contain the same person.
        if (string.Equals(RoomId, frame.RoomId, StringComparison.Ordinal)
            && IsFocused(frame.DriverKey))
        {
            Queue.OfferFocus(frame);
        }
    }

    /// <summary>Offers tyre readings, if this viewer is following the driver they describe.</summary>
    /// <remarks>
    /// Gated on the focus rather than on a subscription of its own, exactly as extras are: tyre
    /// readings describe the car a race engineer has open, and a second subscription for a channel
    /// that always accompanies the focus would be two things to keep in step for no gain.
    /// </remarks>
    public void OfferStint(StintFrameMessage frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (string.Equals(RoomId, frame.RoomId, StringComparison.Ordinal)
            && IsFocused(frame.DriverKey))
        {
            Queue.OfferStint(frame);
        }
    }

    /// <summary>Offers an extras document, if this viewer is following the driver it describes.</summary>
    /// <remarks>
    /// Gated on the focus rather than on a subscription of its own. Extras describe the car a race
    /// engineer has open — damage belongs beside the pedal traces, not beside a timing tower — and a
    /// second subscription for a channel that always accompanies the focus would be two things to
    /// keep in step for no gain.
    /// </remarks>
    public void OfferExtras(ExtrasFrameMessage frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (string.Equals(RoomId, frame.RoomId, StringComparison.Ordinal)
            && IsFocused(frame.DriverKey))
        {
            Queue.OfferExtras(frame);
        }
    }

    /// <summary>Offers a lap history, if this viewer has subscribed to that driver in that room.</summary>
    public void OfferLapHistory(LapHistoryMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Room checked as well as driver, for the same reason focus frames are: two sessions can
        // contain the same person, and a key match alone would deliver another room's stint.
        if (string.Equals(RoomId, message.RoomId, StringComparison.Ordinal)
            && IsSubscribedToLapHistory(message.DriverKey))
        {
            Queue.OfferLapHistory(message);
        }
    }
}
