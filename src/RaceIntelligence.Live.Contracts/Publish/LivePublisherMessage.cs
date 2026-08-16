using MessagePack;

namespace RaceIntelligence.Live.Contracts.Publish;

/// <summary>
/// Base type for every message a collector sends to the live hub over its publishing WebSocket.
/// </summary>
/// <remarks>
/// <para>
/// A MessagePack union rather than an envelope with a hand-rolled type tag: the union key is
/// written by the serializer and dispatched by it on the way back, so a new message type cannot be
/// added without also being decodable, and there is no switch to forget to update.
/// </para>
/// <para>
/// <b>Union keys are permanent.</b> Reusing one for a different type would make an old client's
/// frames decode as the wrong message rather than failing cleanly. New message types take the next
/// free key; retired ones leave their key vacant forever.
/// </para>
/// </remarks>
[Union(0, typeof(LiveHello))]
[Union(1, typeof(LiveSessionFrame))]
[Union(2, typeof(LiveStandingsFrame))]
[Union(3, typeof(LiveSelfFrame))]
[Union(4, typeof(LiveGoodbye))]
public abstract record LivePublisherMessage;

/// <summary>
/// The first message on a publishing connection: who is connecting and what they can produce.
/// </summary>
/// <remarks>
/// The hub rejects a connection whose <see cref="SchemaVersion"/> it does not support, and
/// otherwise holds this for the lifetime of the socket — nothing here changes while connected, so
/// no later message repeats it.
/// </remarks>
/// <param name="SchemaVersion">The wire schema this client was written against. See <see cref="LiveSchemaVersion"/>.</param>
/// <param name="ClientId">
/// Stable per installation, not per connection. It is what lets a reconnecting client be
/// recognised as the same publisher rather than appearing as a second one alongside its own stale
/// entry.
/// </param>
/// <param name="ClientName">A human-readable label for the machine or driver, shown in the dashboard's client list.</param>
/// <param name="ClientVersion">The collector's assembly version, for diagnosing a mixed-version fleet.</param>
/// <param name="GameKey">Which simulator this client is collecting from (e.g. <c>raceroom</c>).</param>
/// <param name="Capabilities">
/// The connector's <see cref="RaceIntelligence.Core.Capabilities.SimCapabilities"/> bitmask. The
/// dashboard renders panels from this rather than branching on <paramref name="GameKey"/>.
/// </param>
[MessagePackObject]
public sealed record LiveHello(
    [property: Key(0)] int SchemaVersion,
    [property: Key(1)] Guid ClientId,
    [property: Key(2)] string ClientName,
    [property: Key(3)] string ClientVersion,
    [property: Key(4)] string GameKey,
    [property: Key(5)] ulong Capabilities) : LivePublisherMessage;

/// <summary>
/// Announces the session this client is now publishing, and the room it believes it belongs to.
/// </summary>
/// <remarks>
/// Sent when a session starts and whenever the roster changes enough to move the fingerprint. The
/// hub uses <paramref name="TrackName"/>, <paramref name="LayoutName"/>,
/// <paramref name="SessionType"/> and <paramref name="SessionIteration"/> — the same tuple the
/// RaceRoom connector already uses to identify a session — plus <paramref name="GameKey"/> as a
/// room key, then confirms it against <paramref name="RosterFingerprint"/> before merging two
/// clients. The key alone is not enough: two people qualifying at the same track at the same time
/// in different servers produce identical keys and completely different rosters.
/// </remarks>
/// <param name="SessionId">The publishing client's own id for this session. Not shared between clients.</param>
/// <param name="GameKey">Which simulator produced it.</param>
/// <param name="TrackName">Track name as the simulator reports it.</param>
/// <param name="LayoutName">Layout name as the simulator reports it.</param>
/// <param name="LayoutLengthMeters">Lap length, when reported.</param>
/// <param name="SessionType">
/// The canonical <see cref="RaceIntelligence.Core.Sessions.SessionType"/> as an integer — the
/// connector's interpretation, not the simulator's raw code. Practice means practice whichever
/// simulator produced it, which is what lets the dashboard label a session without knowing the
/// game.
/// </param>
/// <param name="SessionIteration">Which session of this type it is (first qualifying, second, ...).</param>
/// <param name="StartedAtUtc">When the client observed the session start.</param>
/// <param name="PlayerName">The local driver's display name.</param>
/// <param name="LocalSimDriverId">
/// The local driver's simulator identity, when known. This is the key by which this client's rich
/// telemetry is attached to the right row of a merged timing tower.
/// </param>
/// <param name="RosterFingerprint">
/// A stable hash of the sorted driver identities this client can see. Two clients in the same
/// server produce the same fingerprint; a key collision between different servers does not.
/// </param>
/// <param name="RosterSize">How many cars went into <paramref name="RosterFingerprint"/>, so the hub can weigh a partial overlap.</param>
[MessagePackObject]
public sealed record LiveSessionFrame(
    [property: Key(0)] Guid SessionId,
    [property: Key(1)] string GameKey,
    [property: Key(2)] string TrackName,
    [property: Key(3)] string LayoutName,
    [property: Key(4)] float? LayoutLengthMeters,
    [property: Key(5)] int SessionType,
    [property: Key(6)] int SessionIteration,
    [property: Key(7)] DateTimeOffset StartedAtUtc,
    [property: Key(8)] string? PlayerName,
    [property: Key(9)] string? LocalSimDriverId,
    [property: Key(10)] string RosterFingerprint,
    [property: Key(11)] int RosterSize) : LivePublisherMessage;

/// <summary>
/// A snapshot of every car this client can see — the observed, scoring-granularity view.
/// </summary>
/// <param name="SessionId">The publishing client's session id.</param>
/// <param name="CapturedAtUtc">
/// When the client captured it. Useful for reporting a client's own latency, but never for
/// ordering one client's frames against another's — two gaming PCs have two unsynchronised
/// clocks, so the hub orders across publishers by arrival instead.
/// </param>
/// <param name="SimulationTime">The simulator's clock at capture, which does order one client's frames reliably.</param>
/// <param name="LocalSimDriverId">Which row of <paramref name="Drivers"/> this client has rich telemetry for.</param>
/// <param name="Drivers">Every car in the session, in the simulator's own order.</param>
[MessagePackObject]
public sealed record LiveStandingsFrame(
    [property: Key(0)] Guid SessionId,
    [property: Key(1)] DateTimeOffset CapturedAtUtc,
    [property: Key(2)] double? SimulationTime,
    [property: Key(3)] string? LocalSimDriverId,
    [property: Key(4)] IReadOnlyList<LiveDriverDto> Drivers) : LivePublisherMessage;

/// <summary>
/// The rich channels only the machine running the simulator can see, for the car it is driving.
/// </summary>
/// <remarks>
/// This is the half of the live picture no other client can supply: a simulator publishes pedal
/// inputs, tyre pressure, tyre wear and fuel for the local car only. Sent at the collector's poll
/// rate rather than the standings rate, because these are exactly the channels that are worth
/// seeing at full fidelity.
/// </remarks>
/// <param name="SessionId">The publishing client's session id.</param>
/// <param name="SimDriverId">
/// Which driver this describes, in the same identity space as
/// <see cref="LiveDriverDto.SimDriverId"/>. <see langword="null"/> when the simulator reports no
/// usable identity, in which case the hub falls back to the publisher's own client id.
/// </param>
/// <param name="SequenceNumber">The collector's per-session sample sequence number.</param>
/// <param name="CapturedAtUtc">Wall-clock capture time on the publishing machine.</param>
/// <param name="SimulationTime">The simulator's clock at capture.</param>
/// <param name="LapNumber">Current lap.</param>
/// <param name="Sector">Current sector.</param>
/// <param name="TrackPositionFraction">Progress around the lap, 0..1.</param>
/// <param name="Speed">Meters per second.</param>
/// <param name="Throttle">0..1, or <see langword="null"/> when unreported.</param>
/// <param name="Brake">0..1, or <see langword="null"/> when unreported.</param>
/// <param name="Steering">-1 (full left) to 1 (full right).</param>
/// <param name="Gear">-1 reverse, 0 neutral, positive forward gear.</param>
/// <param name="EngineRpm">Revolutions per minute.</param>
/// <param name="FuelLeft">Liters.</param>
/// <param name="TyrePressure">Kilopascals, FL/FR/RL/RR. Members are <see langword="null"/> when unreported.</param>
/// <param name="TyreWear">0 (new) to 1 (fully worn), FL/FR/RL/RR.</param>
/// <param name="TyreTemperature">Core tyre temperature in celsius, FL/FR/RL/RR.</param>
[MessagePackObject]
public sealed record LiveSelfFrame(
    [property: Key(0)] Guid SessionId,
    [property: Key(1)] string? SimDriverId,
    [property: Key(2)] long SequenceNumber,
    [property: Key(3)] DateTimeOffset CapturedAtUtc,
    [property: Key(4)] double SimulationTime,
    [property: Key(5)] int LapNumber,
    [property: Key(6)] int Sector,
    [property: Key(7)] float? TrackPositionFraction,
    [property: Key(8)] float Speed,
    [property: Key(9)] float? Throttle,
    [property: Key(10)] float? Brake,
    [property: Key(11)] float Steering,
    [property: Key(12)] int? Gear,
    [property: Key(13)] float EngineRpm,
    [property: Key(14)] float FuelLeft,
    [property: Key(15)] LiveWheelValues TyrePressure,
    [property: Key(16)] LiveWheelValues TyreWear,
    [property: Key(17)] LiveWheelValues TyreTemperature) : LivePublisherMessage;

/// <summary>Per-wheel values in the platform's FL, FR, RL, RR order.</summary>
/// <remarks>
/// A dedicated type rather than four flattened members per group, because
/// <see cref="LiveSelfFrame"/> carries three such groups and flattening them all would put twelve
/// positional parameters on one record. The ingest contracts flatten instead; the trade-off tips
/// the other way here only because of how many groups there are.
/// </remarks>
[MessagePackObject]
public sealed record LiveWheelValues(
    [property: Key(0)] float? FrontLeft,
    [property: Key(1)] float? FrontRight,
    [property: Key(2)] float? RearLeft,
    [property: Key(3)] float? RearRight);

/// <summary>Sent when a client stops publishing a session cleanly, so the hub need not wait for a timeout.</summary>
/// <param name="SessionId">The session being closed.</param>
/// <param name="Reason">A short human-readable explanation, shown in the dashboard's client list.</param>
[MessagePackObject]
public sealed record LiveGoodbye(
    [property: Key(0)] Guid SessionId,
    [property: Key(1)] string? Reason) : LivePublisherMessage;
