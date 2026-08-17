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
    /// Drivers whose completed laps this viewer has asked for, used as a set.
    /// </summary>
    /// <remarks>
    /// Concurrent because the two sides run on different loops: the command loop adds and removes
    /// as rows are expanded, while every publisher's receive loop reads it to decide whether a lap
    /// history belongs to this viewer.
    /// </remarks>
    private readonly ConcurrentDictionary<string, byte> _lapHistoryDriverKeys =
        new(StringComparer.Ordinal);

    private string? _roomId;
    private string? _focusDriverKey;

    /// <summary>The queue this viewer's send pump drains.</summary>
    public ViewerQueue Queue { get; } = new();

    /// <summary>The room whose timing tower this viewer is watching, if any.</summary>
    public string? RoomId => Volatile.Read(ref _roomId);

    /// <summary>The driver whose full-rate channels this viewer is following, if any.</summary>
    public string? FocusDriverKey => Volatile.Read(ref _focusDriverKey);

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
        Focus(null);
        UnsubscribeAllLapHistory();
    }

    /// <summary>Switches which driver this viewer follows at full rate.</summary>
    public void Focus(string? driverKey)
    {
        Volatile.Write(ref _focusDriverKey, driverKey);

        // Drop anything already waiting for the previous driver. Delivered after the switch it
        // would appear as a stray frame of someone else's telemetry in the new driver's traces.
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

    /// <summary>Offers a focus frame, if this viewer is following the driver it describes.</summary>
    public void OfferFocus(FocusFrameMessage frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        // Both must match. Checking only the driver key would send a viewer frames from a driver
        // with the same key in a room it is not watching, which is possible whenever two sessions
        // contain the same person.
        if (string.Equals(RoomId, frame.RoomId, StringComparison.Ordinal)
            && string.Equals(FocusDriverKey, frame.DriverKey, StringComparison.Ordinal))
        {
            Queue.OfferFocus(frame);
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
