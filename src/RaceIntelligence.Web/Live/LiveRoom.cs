using RaceIntelligence.Core.Sessions;
using RaceIntelligence.Live.Contracts;
using RaceIntelligence.Live.Contracts.Mapping;
using RaceIntelligence.Live.Contracts.Publish;
using RaceIntelligence.Live.Contracts.View;

namespace RaceIntelligence.Web.Live;

/// <summary>
/// One live session, and everyone publishing into it.
/// </summary>
/// <remarks>
/// <para>
/// All mutation happens under one lock. The room is written by each publisher's receive loop and
/// read by viewer commands, and the work done inside is a projection over at most a full grid —
/// microseconds. A finer-grained scheme would buy nothing and would make "the tower is internally
/// consistent" much harder to be sure of, which is the property that matters: positions and gaps
/// only mean anything as a set.
/// </para>
/// <para>
/// Nothing is sent from inside the lock. Projections are built under it and handed to the caller
/// to broadcast, so a viewer's queue is never touched while a publisher's frame is being applied.
/// </para>
/// </remarks>
public sealed class LiveRoom
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, LivePublisherState> _publishers = [];
    private readonly LapHistoryAccumulator _lapHistory = new();

    private DateTimeOffset _lastUpdatedAtUtc;

    /// <summary>Creates a room for a session key.</summary>
    /// <param name="roomId">The opaque id viewers subscribe with.</param>
    /// <param name="key">The session identity this room was opened for.</param>
    /// <param name="createdAtUtc">Server time at creation, which also seeds the expiry clock.</param>
    public LiveRoom(string roomId, RoomKey key, DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(roomId);

        RoomId = roomId;
        Key = key;
        _lastUpdatedAtUtc = createdAtUtc;
    }

    /// <summary>The opaque id viewers subscribe with, stable for this room's lifetime.</summary>
    public string RoomId { get; }

    /// <summary>The session identity this room represents.</summary>
    public RoomKey Key { get; }

    /// <summary>
    /// When the hub last received anything for this room, by <b>server</b> clock.
    /// </summary>
    /// <remarks>
    /// Server time, never the publisher's. Expiry is the one decision that must not be delegated to
    /// a machine the hub does not control: a gaming PC with a clock an hour fast would otherwise
    /// keep a dead room alive for an hour, and one an hour slow would have its live room swept out
    /// from under it immediately.
    /// </remarks>
    public DateTimeOffset LastUpdatedAtUtc
    {
        get
        {
            lock (_gate)
            {
                return _lastUpdatedAtUtc;
            }
        }
    }

    /// <summary>Whether any publisher is currently connected.</summary>
    public bool HasPublishers
    {
        get
        {
            lock (_gate)
            {
                return _publishers.Count > 0;
            }
        }
    }

    /// <summary>
    /// Adds or updates a publisher's session announcement.
    /// </summary>
    /// <returns>What viewers should now be sent. Empty if no standings have arrived yet.</returns>
    public LiveRoomUpdate Announce(LivePublisherIdentity identity, LiveSessionFrame frame, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(frame);

        lock (_gate)
        {
            if (!_publishers.TryGetValue(identity.ClientId, out var state))
            {
                state = new LivePublisherState(identity);
                _publishers[identity.ClientId] = state;
            }

            state.Session = frame;
            _lastUpdatedAtUtc = nowUtc;

            return ObserveAndProjectLocked(nowUtc);
        }
    }

    /// <summary>
    /// Applies a standings snapshot from a publisher.
    /// </summary>
    /// <returns>What viewers should now be sent. Empty if the publisher is unknown here.</returns>
    public LiveRoomUpdate ApplyStandings(Guid clientId, LiveStandingsFrame frame, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(frame);

        lock (_gate)
        {
            if (!_publishers.TryGetValue(clientId, out var state))
            {
                return LiveRoomUpdate.None;
            }

            state.Standings = LiveStandingsContractMapper.ToCore(frame);
            state.StandingsReceivedAtUtc = nowUtc;
            _lastUpdatedAtUtc = nowUtc;

            return ObserveAndProjectLocked(nowUtc);
        }
    }

    /// <summary>
    /// Turns a publisher's local-car frame into the focus message for the row it belongs to.
    /// </summary>
    /// <returns>
    /// The message to broadcast, or <see langword="null"/> when the publisher is unknown here or
    /// its simulator reports no identity for the car it is driving — in which case there is no
    /// tower row to attach the telemetry to, and guessing one would show a race engineer another
    /// driver's pedal traces.
    /// </returns>
    public FocusFrameMessage? ApplySelf(Guid clientId, LiveSelfFrame frame, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(frame);

        lock (_gate)
        {
            if (!_publishers.TryGetValue(clientId, out var state))
            {
                return null;
            }

            _lastUpdatedAtUtc = nowUtc;

            // The frame's own id first, falling back to the one announced for the session: a self
            // frame is captured from the local car every poll, whereas the announcement is the
            // considered answer to "who am I" made once when the session started.
            string? driverKey =
                LiveTowerProjector.DriverKeyForSimDriverId(frame.SimDriverId)
                ?? LiveTowerProjector.DriverKeyForSimDriverId(state.Session?.LocalSimDriverId);

            return driverKey is null ? null : ToFocusFrame(RoomId, driverKey, frame);
        }
    }

    /// <summary>
    /// Turns a publisher's extras document into the message for the row it belongs to.
    /// </summary>
    /// <returns>
    /// The message to broadcast, or <see langword="null"/> under exactly the same conditions as
    /// <see cref="ApplySelf"/> — an unknown publisher, or a simulator that reports no identity for
    /// the car it is driving, since there is no tower row to attach the document to and guessing one
    /// would show a race engineer another driver's damage.
    /// </returns>
    public ExtrasFrameMessage? ApplyExtras(Guid clientId, LiveExtrasFrame frame, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(frame);

        lock (_gate)
        {
            if (!_publishers.TryGetValue(clientId, out var state))
            {
                return null;
            }

            // Kept per publisher, so a viewer that focuses a driver mid-session is answered from
            // what the hub already holds rather than waiting out an extras interval — a second of a
            // blank damage panel that would read as "no damage".
            state.Extras = frame;
            _lastUpdatedAtUtc = nowUtc;

            string? driverKey =
                LiveTowerProjector.DriverKeyForSimDriverId(frame.SimDriverId)
                ?? LiveTowerProjector.DriverKeyForSimDriverId(state.Session?.LocalSimDriverId);

            return driverKey is null
                ? null
                : new ExtrasFrameMessage(RoomId, driverKey, frame.CapturedAtUtc, frame.ExtrasJson);
        }
    }

    /// <summary>
    /// The most recent extras document for a driver, for a viewer that has just focused them.
    /// </summary>
    public ExtrasFrameMessage? LatestExtrasFor(string driverKey)
    {
        ArgumentNullException.ThrowIfNull(driverKey);

        lock (_gate)
        {
            foreach (var state in _publishers.Values)
            {
                if (state.Extras is not { } frame)
                {
                    continue;
                }

                string? key =
                    LiveTowerProjector.DriverKeyForSimDriverId(frame.SimDriverId)
                    ?? LiveTowerProjector.DriverKeyForSimDriverId(state.Session?.LocalSimDriverId);

                if (string.Equals(key, driverKey, StringComparison.Ordinal))
                {
                    return new ExtrasFrameMessage(RoomId, driverKey, frame.CapturedAtUtc, frame.ExtrasJson);
                }
            }

            return null;
        }
    }

    /// <summary>Removes a publisher, returning the tower without it.</summary>
    /// <remarks>
    /// The room itself survives losing its last publisher, and is swept later by
    /// <see cref="LiveRoomJanitor"/> instead. That grace period is what lets a collector whose
    /// socket dropped mid-race rejoin the room it was already in, keeping the room id — and so
    /// every viewer's subscription — intact across a reconnect.
    /// </remarks>
    public LiveRoomUpdate RemovePublisher(Guid clientId, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            return _publishers.Remove(clientId) ? ObserveAndProjectLocked(nowUtc) : LiveRoomUpdate.None;
        }
    }

    /// <summary>Whether a driver key exists in this room and has a publisher supplying full-rate telemetry.</summary>
    public DriverFocusAvailability GetFocusAvailability(string driverKey)
    {
        ArgumentNullException.ThrowIfNull(driverKey);

        lock (_gate)
        {
            if (SelfDriverKeysLocked().Contains(driverKey))
            {
                return DriverFocusAvailability.Available;
            }

            bool known = SelectSnapshotLocked() is { } standings
                && standings.Drivers.Any(driver => LiveTowerProjector.DriverKeyFor(driver) == driverKey);

            return known ? DriverFocusAvailability.ObservedOnly : DriverFocusAvailability.UnknownDriver;
        }
    }

    /// <summary>Builds this room's entry in the room list.</summary>
    public LiveRoomSummary Summarize()
    {
        lock (_gate)
        {
            var publishers = _publishers.Values
                .Select(state => new LivePublisherSummary(
                    state.Identity.ClientId,
                    state.Identity.ClientName,
                    state.Session?.PlayerName,
                    state.Session?.LocalSimDriverId,
                    state.Identity.ConnectedAtUtc,
                    LiveCapabilityNames.From(state.Identity.Capabilities)))
                .OrderBy(publisher => publisher.ConnectedAtUtc)
                .ToArray();

            return new LiveRoomSummary(
                RoomId,
                Key.GameKey,
                Key.TrackName,
                Key.LayoutName,
                Key.SessionType,
                SelectSnapshotLocked()?.Drivers.Count ?? 0,
                publishers,
                _lastUpdatedAtUtc);
        }
    }

    /// <summary>Builds the current tower, for a viewer that has just subscribed.</summary>
    /// <remarks>
    /// Read-only, unlike the publisher-driven paths: a viewer's command must not be what advances
    /// lap history. If it were, the lap a viewer's subscription happened to observe would be
    /// recorded without ever being broadcast, and every other viewer would wait for the following
    /// lap to learn about it.
    /// </remarks>
    public TowerSnapshotMessage? Snapshot(DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            return ProjectLocked(nowUtc);
        }
    }

    /// <summary>
    /// The completed laps this room has recorded for one driver, or <see langword="null"/> when it
    /// has never heard of them.
    /// </summary>
    /// <remarks>
    /// Answers a subscription immediately rather than leaving the viewer to wait for the next lap,
    /// which on a long circuit is minutes of an empty panel. A driver the room knows but who has
    /// completed nothing yields an empty history — that is a real answer, and distinct from the null
    /// that means "no such driver here".
    /// </remarks>
    public LapHistoryMessage? LapHistoryFor(string driverKey)
    {
        ArgumentNullException.ThrowIfNull(driverKey);

        lock (_gate)
        {
            // Either source is enough. The accumulator remembers a driver who has since dropped out
            // of the snapshot, and the snapshot names a driver who has not completed a lap yet.
            bool known = _lapHistory.Knows(driverKey)
                || (SelectSnapshotLocked() is { } standings
                    && standings.Drivers.Any(driver => LiveTowerProjector.DriverKeyFor(driver) == driverKey));

            if (!known)
            {
                return null;
            }

            var (laps, truncated) = _lapHistory.SnapshotFor(driverKey);
            return new LapHistoryMessage(RoomId, driverKey, laps, truncated);
        }
    }

    /// <summary>
    /// Feeds the lap accumulator from the selected snapshot, then projects the tower.
    /// </summary>
    /// <remarks>
    /// The selected snapshot rather than the frame that just arrived, deliberately: that is what
    /// makes the accumulator correct when <see cref="SelectSnapshotLocked"/> switches publishers
    /// mid-race, since both publishers describe the same drivers and recording is idempotent by lap
    /// number. Everything is built here, under the lock, and returned for the caller to broadcast
    /// outside it.
    /// </remarks>
    private LiveRoomUpdate ObserveAndProjectLocked(DateTimeOffset nowUtc)
    {
        var standings = SelectSnapshotLocked();
        if (standings is null)
        {
            return LiveRoomUpdate.None;
        }

        var changedDrivers = _lapHistory.Observe(standings);

        LapHistoryMessage[] histories;
        if (changedDrivers.Count == 0)
        {
            histories = [];
        }
        else
        {
            histories = new LapHistoryMessage[changedDrivers.Count];
            for (int i = 0; i < changedDrivers.Count; i++)
            {
                var (laps, truncated) = _lapHistory.SnapshotFor(changedDrivers[i]);
                histories[i] = new LapHistoryMessage(RoomId, changedDrivers[i], laps, truncated);
            }
        }

        return new LiveRoomUpdate(
            new TowerSnapshotMessage(RoomId, nowUtc, LiveTowerProjector.Project(standings, SelfDriverKeysLocked())),
            histories);
    }

    private TowerSnapshotMessage? ProjectLocked(DateTimeOffset nowUtc)
    {
        var standings = SelectSnapshotLocked();
        if (standings is null)
        {
            return null;
        }

        return new TowerSnapshotMessage(
            RoomId,
            nowUtc,
            LiveTowerProjector.Project(standings, SelfDriverKeysLocked()));
    }

    /// <summary>
    /// Picks the snapshot the tower is projected from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The most complete view wins, breaking ties on the freshest. With one publisher — the case
    /// step 4 is built for — this is simply "that publisher's latest". With two it is a deliberate
    /// placeholder for the per-field authority merge in step 6: choosing the client that can see
    /// more cars means nobody disappears from the tower, which is the failure that would actually
    /// be noticed.
    /// </para>
    /// <para>
    /// What it does <i>not</i> do is combine timing from both, so a car the chosen publisher sees
    /// stale data for stays stale even when the other client has something fresher. That is the gap
    /// step 6 closes, and it is why the merge is the next item in the build order rather than a
    /// later refinement.
    /// </para>
    /// </remarks>
    private SessionStandings? SelectSnapshotLocked()
    {
        SessionStandings? best = null;
        var bestReceivedAt = DateTimeOffset.MinValue;

        foreach (var state in _publishers.Values)
        {
            if (state.Standings is not { } candidate)
            {
                continue;
            }

            if (best is null
                || candidate.Drivers.Count > best.Drivers.Count
                || (candidate.Drivers.Count == best.Drivers.Count && state.StandingsReceivedAtUtc > bestReceivedAt))
            {
                best = candidate;
                bestReceivedAt = state.StandingsReceivedAtUtc;
            }
        }

        return best;
    }

    /// <summary>
    /// The driver keys whose own machine is publishing here — the rows that get a
    /// <see cref="LiveDataTier.Self"/> marking and a focus stream.
    /// </summary>
    private HashSet<string> SelfDriverKeysLocked()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in _publishers.Values)
        {
            if (LiveTowerProjector.DriverKeyForSimDriverId(state.Session?.LocalSimDriverId) is { } key)
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    private static FocusFrameMessage ToFocusFrame(string roomId, string driverKey, LiveSelfFrame frame) => new(
        roomId,
        driverKey,
        frame.CapturedAtUtc,
        frame.SimulationTime,
        frame.LapNumber,
        frame.Sector,
        frame.TrackPositionFraction,
        frame.Speed,
        frame.Throttle,
        frame.Brake,
        frame.Clutch,
        frame.Steering,
        frame.Gear,
        frame.EngineRpm,
        frame.FuelLeft,
        ToWheelArray(frame.TyrePressure),
        ToWheelArray(frame.TyreWear),
        ToWheelArray(frame.TyreTemperature));

    /// <summary>
    /// Flattens per-wheel values into the platform's FL, FR, RL, RR order.
    /// </summary>
    /// <remarks>
    /// An array rather than four named properties because the browser renders these as a set — a
    /// tyre diagram indexes them, it does not read them one at a time — and because the order is
    /// already the platform's convention everywhere else.
    /// </remarks>
    private static IReadOnlyList<float?> ToWheelArray(LiveWheelValues values) =>
        [values.FrontLeft, values.FrontRight, values.RearLeft, values.RearRight];
}

/// <summary>
/// Everything one applied publisher frame produces for viewers.
/// </summary>
/// <remarks>
/// A single return value rather than a room that pushes to viewers itself, because the room's whole
/// discipline is that nothing is sent from inside its lock. Both halves are built under the lock and
/// handed back for the registry to fan out afterwards, so a viewer's queue is never touched while a
/// publisher's frame is being applied.
/// </remarks>
/// <param name="Tower">The tower as it now stands, or <see langword="null"/> when no standings have arrived.</param>
/// <param name="LapHistories">
/// A full history for each driver whose lap count moved. Usually empty: standings arrive ten times a
/// second and a lap finishes about once a minute.
/// </param>
public readonly record struct LiveRoomUpdate(
    TowerSnapshotMessage? Tower,
    IReadOnlyList<LapHistoryMessage> LapHistories)
{
    /// <summary>Nothing to send — an unknown publisher, or a room with no standings yet.</summary>
    public static LiveRoomUpdate None => new(null, []);
}

/// <summary>Whether a driver can be focused, and if not, why.</summary>
public enum DriverFocusAvailability
{
    /// <summary>No such driver in this room.</summary>
    UnknownDriver,

    /// <summary>The driver is in the room, but nobody is publishing their machine's telemetry.</summary>
    ObservedOnly,

    /// <summary>Full-rate telemetry is being published for this driver.</summary>
    Available,
}

/// <summary>Who a publisher is, fixed for the life of its connection.</summary>
/// <param name="ClientId">Stable per installation, so a reconnect is recognised as the same publisher.</param>
/// <param name="ClientName">The human-readable label shown in the dashboard's client list.</param>
/// <param name="ClientVersion">The collector's version, for diagnosing a mixed-version fleet.</param>
/// <param name="ConnectedAtUtc">Server time the connection was established.</param>
/// <param name="Capabilities">
/// The connector's <see cref="RaceIntelligence.Core.Capabilities.SimCapabilities"/> bitmask, as
/// declared in the hello. Carried through to the dashboard so panels are chosen by what the
/// simulator can actually report rather than by which simulator it is.
/// </param>
public sealed record LivePublisherIdentity(
    Guid ClientId,
    string ClientName,
    string ClientVersion,
    DateTimeOffset ConnectedAtUtc,
    ulong Capabilities = 0);

/// <summary>What a room holds for one publisher. Guarded by the owning room's lock.</summary>
internal sealed class LivePublisherState(LivePublisherIdentity identity)
{
    public LivePublisherIdentity Identity { get; } = identity;

    /// <summary>The session this publisher announced, or null before its first announcement.</summary>
    public LiveSessionFrame? Session { get; set; }

    /// <summary>This publisher's most recent view of the field.</summary>
    public SessionStandings? Standings { get; set; }

    /// <summary>
    /// This publisher's most recent simulator-specific document, or null before the first arrives.
    /// </summary>
    /// <remarks>
    /// Retained rather than forwarded and forgotten, so a viewer focusing a driver is answered from
    /// what the hub holds instead of waiting out an extras interval. At roughly 1 Hz that wait is a
    /// second of an empty damage panel, which reads as "no damage" rather than "not known yet".
    /// </remarks>
    public LiveExtrasFrame? Extras { get; set; }

    /// <summary>
    /// Server time <see cref="Standings"/> arrived.
    /// </summary>
    /// <remarks>
    /// Deliberately not the frame's own <c>CapturedAtUtc</c>. Two gaming PCs have two unsynchronised
    /// wall clocks, so comparing one publisher's capture time against another's is meaningless —
    /// arrival is the only ordering the hub can trust across publishers.
    /// </remarks>
    public DateTimeOffset StandingsReceivedAtUtc { get; set; }
}
