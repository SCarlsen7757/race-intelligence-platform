using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaceIntelligence.Live.Contracts.Publish;
using RaceIntelligence.Live.Contracts.View;

namespace RaceIntelligence.Web.Live;

/// <summary>
/// Every live session the hub knows about, and the routing of publisher frames into viewers.
/// </summary>
/// <remarks>
/// <para>
/// This is the hub. Publishers call in from their receive loops; the registry decides which room a
/// frame belongs to, applies it, and hands the result to <see cref="LiveViewerRegistry"/> for
/// fan-out. Rooms know nothing about viewers and viewers know nothing about rooms — both know only
/// this class, which is what keeps a room's lifetime independent of who happens to be watching it.
/// </para>
/// <para>
/// Everything here is in memory and nothing is persisted, by explicit decision: a live frame is
/// superseded within milliseconds, the archive path already stores what is worth keeping forever,
/// and a hub restart during a race costs a reconnect rather than any data.
/// </para>
/// </remarks>
public sealed class LiveRoomRegistry(
    LiveViewerRegistry viewers,
    IOptions<LiveHubOptions> options,
    TimeProvider timeProvider,
    ILogger<LiveRoomRegistry> logger)
{
    private readonly ConcurrentDictionary<RoomKey, LiveRoom> _rooms = new();

    /// <summary>Which room each connected publisher is currently in, so a frame can be routed without its session announcement.</summary>
    private readonly ConcurrentDictionary<Guid, LiveRoom> _publisherRooms = new();

    private readonly LiveHubOptions _options = options.Value;

    /// <summary>How many rooms are live.</summary>
    public int Count => _rooms.Count;

    /// <summary>Finds a room by the id a viewer subscribed with.</summary>
    /// <remarks>
    /// A scan rather than a second dictionary keyed by room id. The room count is the number of
    /// simultaneous races on one home server — single digits — and lookups happen when a viewer
    /// sends a command, not per frame. A second index would be another thing to keep consistent
    /// with the first for no measurable gain.
    /// </remarks>
    public LiveRoom? FindByRoomId(string roomId)
    {
        ArgumentNullException.ThrowIfNull(roomId);

        foreach (var room in _rooms.Values)
        {
            if (string.Equals(room.RoomId, roomId, StringComparison.Ordinal))
            {
                return room;
            }
        }

        return null;
    }

    /// <summary>Builds the room list, most recently updated first.</summary>
    public RoomListMessage BuildRoomList()
    {
        var summaries = _rooms.Values
            .Select(room => room.Summarize())
            .OrderByDescending(summary => summary.LastUpdatedAtUtc)
            .ToArray();

        return new RoomListMessage(summaries);
    }

    /// <summary>
    /// Places a publisher in the room its session announcement names, moving it out of any previous
    /// one.
    /// </summary>
    public void Announce(LivePublisherIdentity identity, LiveSessionFrame frame)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(frame);

        var now = timeProvider.GetUtcNow();
        var key = RoomKey.From(frame);

        var room = _rooms.GetOrAdd(key, static (roomKey, createdAt) =>
            new LiveRoom(NewRoomId(), roomKey, createdAt), now);

        // A client that changes session mid-connection — qualifying into race — has to leave the
        // room it was in, or it would go on counting as a publisher of a session it no longer sees.
        if (_publisherRooms.TryGetValue(identity.ClientId, out var previous)
            && !ReferenceEquals(previous, room))
        {
            Broadcast(previous.RemovePublisher(identity.ClientId, now));
        }

        _publisherRooms[identity.ClientId] = room;

        Broadcast(room.Announce(identity, frame, now));
        viewers.BroadcastRoomList(BuildRoomList());

        logger.LogInformation(
            "Publisher {ClientName} ({ClientId}) is publishing {Track} {Layout} as room {RoomId}.",
            identity.ClientName, identity.ClientId, key.TrackName, key.LayoutName, room.RoomId);
    }

    /// <summary>Applies a standings snapshot from a publisher and fans the new tower out.</summary>
    /// <remarks>
    /// A frame from a publisher that has not announced a session is dropped rather than buffered.
    /// The hub has nowhere to put it — a room is a session, and the announcement is what names one
    /// — and the collector re-announces on every connection, so the gap is bounded by a single
    /// frame rather than a whole race.
    /// </remarks>
    public void ApplyStandings(Guid clientId, LiveStandingsFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (!_publisherRooms.TryGetValue(clientId, out var room))
        {
            return;
        }

        Broadcast(room.ApplyStandings(clientId, frame, timeProvider.GetUtcNow()));
    }

    /// <summary>Applies a publisher's local-car frame and fans it out to viewers focused on that driver.</summary>
    public void ApplySelf(Guid clientId, LiveSelfFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (!_publisherRooms.TryGetValue(clientId, out var room))
        {
            return;
        }

        if (room.ApplySelf(clientId, frame, timeProvider.GetUtcNow()) is { } focus)
        {
            viewers.BroadcastFocus(focus);
        }
    }

    /// <summary>
    /// Removes a publisher from whichever room it is in — a clean goodbye, or a dropped connection.
    /// </summary>
    public void RemovePublisher(Guid clientId)
    {
        if (!_publisherRooms.TryRemove(clientId, out var room))
        {
            return;
        }

        Broadcast(room.RemovePublisher(clientId, timeProvider.GetUtcNow()));
        viewers.BroadcastRoomList(BuildRoomList());
    }

    /// <summary>
    /// Forgets rooms that have received nothing for longer than the configured expiry.
    /// </summary>
    /// <returns>How many rooms were removed.</returns>
    /// <remarks>
    /// Expiry is measured from the last frame rather than from the last publisher leaving, which is
    /// what gives a reconnecting collector a window to rejoin the room it was already in. Viewers
    /// watching keep their subscription across that reconnect and never see the session flicker
    /// out of the list.
    /// </remarks>
    public int RemoveExpiredRooms()
    {
        var now = timeProvider.GetUtcNow();
        int removed = 0;

        foreach (var (key, room) in _rooms)
        {
            if (now - room.LastUpdatedAtUtc < _options.RoomExpiry || room.HasPublishers)
            {
                continue;
            }

            if (!_rooms.TryRemove(key, out _))
            {
                continue;
            }

            removed++;
            viewers.EvictRoom(room.RoomId);

            logger.LogInformation(
                "Room {RoomId} ({Track} {Layout}) expired after {Expiry} with no frames.",
                room.RoomId, key.TrackName, key.LayoutName, _options.RoomExpiry);
        }

        if (removed > 0)
        {
            viewers.BroadcastRoomList(BuildRoomList());
        }

        return removed;
    }

    /// <summary>Fans one applied frame's results out: the tower, then any lap that just completed.</summary>
    /// <remarks>
    /// The tower first, so a row appears before the history that expands it. Nothing here can block:
    /// every offer lands in a per-viewer conflating queue, and this runs on a publisher's receive
    /// loop.
    /// </remarks>
    private void Broadcast(LiveRoomUpdate update)
    {
        if (update.Tower is { } tower)
        {
            viewers.BroadcastTower(tower);
        }

        foreach (var history in update.LapHistories)
        {
            viewers.BroadcastLapHistory(history);
        }
    }

    /// <summary>
    /// A fresh opaque room id.
    /// </summary>
    /// <remarks>
    /// Opaque rather than derived from the room key, so a room id says nothing about the session
    /// and cannot be guessed by someone who knows what track a race is at. It is also what lets
    /// step 6 split a room that turns out to be two servers sharing a key without any viewer's
    /// subscription silently following the wrong half.
    /// </remarks>
    private static string NewRoomId() => Guid.NewGuid().ToString("n");
}
