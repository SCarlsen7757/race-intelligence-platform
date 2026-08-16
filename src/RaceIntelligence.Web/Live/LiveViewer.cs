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
    /// The focus is cleared rather than carried over because a driver key is only meaningful within
    /// a room. Keeping it would either resolve to nobody or, worse, to a different driver who
    /// happened to share an id — and the viewer would be shown someone else's telemetry under the
    /// name it had selected.
    /// </remarks>
    public void WatchRoom(string? roomId)
    {
        Volatile.Write(ref _roomId, roomId);
        Focus(null);
    }

    /// <summary>Switches which driver this viewer follows at full rate.</summary>
    public void Focus(string? driverKey)
    {
        Volatile.Write(ref _focusDriverKey, driverKey);

        // Drop anything already waiting for the previous driver. Delivered after the switch it
        // would appear as a stray frame of someone else's telemetry in the new driver's traces.
        Queue.ClearFocus();
    }

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
}
