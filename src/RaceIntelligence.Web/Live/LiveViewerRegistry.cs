using System.Collections.Concurrent;
using RaceIntelligence.Live.Contracts.View;

namespace RaceIntelligence.Web.Live;

/// <summary>
/// Every connected browser, and the fan-out to them.
/// </summary>
/// <remarks>
/// <para>
/// Fan-out is a straight iteration over all viewers, filtered by what each has subscribed to,
/// rather than an index from room to viewer. At the scale this serves — a handful of race
/// engineers watching a home server — an index would be more bookkeeping to keep correct across
/// room switches and disconnects than the scan it saves. If the dashboard ever has hundreds of
/// concurrent viewers this is the first thing to change, and nothing outside this class would need
/// to know.
/// </para>
/// <para>
/// Every offer is non-blocking, because the caller is a publisher's receive loop. The whole reason
/// each viewer owns a conflating queue is so that this loop can hand a frame to a stalled viewer
/// and carry on within nanoseconds.
/// </para>
/// </remarks>
public sealed class LiveViewerRegistry
{
    private readonly ConcurrentDictionary<LiveViewer, byte> _viewers = new();

    /// <summary>How many browsers are currently connected.</summary>
    public int Count => _viewers.Count;

    /// <summary>Registers a newly connected viewer.</summary>
    public void Add(LiveViewer viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        _viewers[viewer] = 0;
    }

    /// <summary>Removes a disconnected viewer.</summary>
    public void Remove(LiveViewer viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        _viewers.TryRemove(viewer, out _);
    }

    /// <summary>Offers a tower snapshot to every viewer watching that room.</summary>
    public void BroadcastTower(TowerSnapshotMessage snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        foreach (var viewer in _viewers.Keys)
        {
            viewer.OfferTower(snapshot);
        }
    }

    /// <summary>Offers a focus frame to every viewer following that driver in that room.</summary>
    public void BroadcastFocus(FocusFrameMessage frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        foreach (var viewer in _viewers.Keys)
        {
            viewer.OfferFocus(frame);
        }
    }

    /// <summary>Offers a driver's completed laps to every viewer subscribed to them in that room.</summary>
    public void BroadcastLapHistory(LapHistoryMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        foreach (var viewer in _viewers.Keys)
        {
            viewer.OfferLapHistory(message);
        }
    }

    /// <summary>
    /// Offers the room list to every viewer, whatever they are watching.
    /// </summary>
    /// <remarks>
    /// Unfiltered on purpose: the room list is the dashboard's landing view and its "some other
    /// session just started" notification at once, so a viewer already deep in one room still wants
    /// it.
    /// </remarks>
    public void BroadcastRoomList(RoomListMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        foreach (var viewer in _viewers.Keys)
        {
            viewer.Queue.OfferRoomList(message);
        }
    }

    /// <summary>
    /// Tells every viewer watching a room that has gone away to stop watching it.
    /// </summary>
    /// <remarks>
    /// Without this a viewer would sit on a room id that resolves to nothing, showing the last
    /// tower it received as though the session were still running. Clearing the subscription
    /// returns it to the room list, where the room's absence is visible.
    /// </remarks>
    public void EvictRoom(string roomId)
    {
        ArgumentNullException.ThrowIfNull(roomId);

        foreach (var viewer in _viewers.Keys)
        {
            if (string.Equals(viewer.RoomId, roomId, StringComparison.Ordinal))
            {
                viewer.WatchRoom(null);
                viewer.Queue.OfferError(new LiveErrorMessage(
                    LiveErrorCodes.RoomClosed,
                    "The session you were watching has finished."));
            }
        }
    }
}
