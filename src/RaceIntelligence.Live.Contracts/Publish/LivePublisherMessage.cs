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
/// <b>Union keys are not permanent yet.</b> Before the first release tag there is no deployed
/// client for a renumbering to break — hub and collector are built from one commit — so a message
/// type may be added, removed or renumbered as the shape demands, and
/// <see cref="LiveSchemaVersion"/> stays at 1 through all of it.
/// </para>
/// <para>
/// From <c>v1.0.0</c> that inverts and keys become permanent: reusing one for a different type
/// would make a deployed client's frames decode as the wrong message rather than failing cleanly,
/// so new types would take the next free key and retired ones would leave theirs vacant forever.
/// </para>
/// </remarks>
[Union(0, typeof(LiveHello))]
[Union(1, typeof(LiveSessionFrame))]
[Union(2, typeof(LiveStandingsFrame))]
[Union(3, typeof(LiveSelfFrame))]
[Union(4, typeof(LiveGoodbye))]
[Union(5, typeof(LiveExtrasFrame))]
[Union(6, typeof(LiveStintFrame))]
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
/// <b>The simulator's own raw session type value, uninterpreted.</b> It arrives here typed as
/// <see cref="RaceIntelligence.Core.Sessions.SessionType"/>, but the connector deliberately does
/// not translate RaceRoom's numbering into the canonical enum's — see the remarks on
/// <c>R3ETelemetryMapper.ToSessionInfo</c>. The enum is a carrier for the raw int, not a
/// conversion of it, so RaceRoom's 0 is practice while the canonical 0 is unknown.
/// <para>
/// Anything rendering this therefore has to interpret it against <paramref name="GameKey"/>.
/// Reading it as canonical shifts every label by one, which looks entirely plausible on screen —
/// a race reported as qualifying — and is why this is spelled out rather than left to the type.
/// </para>
/// </param>
/// <param name="SessionIteration">Which session of this type it is (first qualifying, second, ...).</param>
/// <param name="StartedAtUtc">When the client observed the session start.</param>
/// <param name="PlayerName">The local driver's display name.</param>
/// <param name="LocalSimDriverId">
/// The local driver's simulator identity, when known. This is the key by which this client's rich
/// telemetry is attached to the right row of a merged timing tower.
/// </param>
/// <param name="LocalSlotId">
/// The local car's per-session slot, when known — the fallback for attaching this client's rich
/// telemetry to a tower row when <paramref name="LocalSimDriverId"/> is absent.
/// <para>
/// It is absent for a whole class of session rather than rarely: RaceRoom issues account ids only
/// to authenticated online sessions and reports 0 or -1 offline, so an offline or single-player
/// race has no local identity at all. Without this the focus and extras channels resolve to no
/// tower row and are dropped, and the driver's own car shows no pedals, no tyre data and no
/// damage while the timing tower — which falls back to <c>slot:</c> keys of its own — carries on
/// looking healthy.
/// </para>
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
    [property: Key(11)] int RosterSize,
    [property: Key(12)] int? LocalSlotId) : LivePublisherMessage;

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
/// <param name="PitWindow">
/// The session's mandatory pit window, or <see langword="null"/> from a connector that does not
/// report one. Rides the standings frame rather than the session announcement because its status
/// changes as the race runs, and this is the message that repeats.
/// </param>
[MessagePackObject]
public sealed record LiveStandingsFrame(
    [property: Key(0)] Guid SessionId,
    [property: Key(1)] DateTimeOffset CapturedAtUtc,
    [property: Key(2)] double? SimulationTime,
    [property: Key(3)] string? LocalSimDriverId,
    [property: Key(4)] IReadOnlyList<LiveDriverDto> Drivers,
    [property: Key(5)] LivePitWindowDto? PitWindow = null,
    [property: Key(6)] LiveRaceLengthDto? RaceLength = null) : LivePublisherMessage;

/// <summary>The session's mandatory pit window, on the wire.</summary>
/// <remarks>
/// <see cref="Status"/> and <see cref="Unit"/> travel as plain <see cref="int"/> for the same reason
/// <see cref="LiveDriverDto.PitLaneState"/> does: the value comes from another machine's build, which
/// may know a code this one does not, and an unrecognised code has to degrade to "unavailable"
/// rather than land in an enum as an out-of-range value every downstream <c>switch</c> falls through.
/// </remarks>
/// <param name="Status">As <see cref="RaceIntelligence.Core.Sessions.PitWindowStatus"/>.</param>
/// <param name="Start">Where the window opens, in <paramref name="Unit"/>; null when unreported.</param>
/// <param name="End">Where the window closes, in <paramref name="Unit"/>; null when unreported.</param>
/// <param name="Unit">As <see cref="RaceIntelligence.Core.Sessions.PitWindowUnit"/>.</param>
[MessagePackObject]
public sealed record LivePitWindowDto(
    [property: Key(0)] int Status,
    [property: Key(1)] int? Start,
    [property: Key(2)] int? End,
    [property: Key(3)] int Unit);

/// <summary>How long the race is, on the wire.</summary>
/// <remarks>
/// <see cref="Unit"/> travels as a plain <see cref="int"/> for the reason
/// <see cref="LivePitWindowDto"/> gives: the value comes from another machine's build, and an
/// unrecognised code must degrade to "unknown" rather than land in an enum out of range.
/// <para>
/// Both figures ride together and neither is inferred from the other. A simulator may report a lap
/// count in a timed session or a duration in a lap race, and only <see cref="Unit"/> says which one
/// actually ends the race.
/// </para>
/// </remarks>
/// <param name="Laps">Total laps; null when the session is not run to a lap count.</param>
/// <param name="DurationSeconds">Total session length in seconds; null when not run to a clock.</param>
/// <param name="Unit">As <see cref="RaceIntelligence.Core.Sessions.RaceLengthUnit"/>.</param>
[MessagePackObject]
public sealed record LiveRaceLengthDto(
    [property: Key(0)] int? Laps,
    [property: Key(1)] double? DurationSeconds,
    [property: Key(2)] int Unit);

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
/// <param name="BrakePressure">
/// Kilonewtons at each corner, FL/FR/RL/RR. Members are <see langword="null"/> when unreported.
/// <para>
/// Here rather than on <see cref="LiveStintFrame"/> because it moves at pedal rate: it rises and
/// falls inside one braking event, and it shares a sample index with
/// <paramref name="Brake"/> — which is what makes "what the driver asked for against what arrived at
/// the corner" a comparison rather than two traces sampled at unrelated moments.
/// </para>
/// </param>
/// <param name="Clutch">
/// 0 (engaged) to 1 (fully disengaged), or <see langword="null"/> when unreported — the normal case
/// for a car with an automatic clutch, where a dashboard must draw nothing rather than a bar at
/// rest. Sits at the end rather than beside <paramref name="Brake"/> only because that is where it
/// was added; nothing depends on the position while keys are still free to move.
/// </param>
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
    [property: Key(15)] LiveWheelValues BrakePressure,
    [property: Key(16)] float? Clutch,
    [property: Key(17)] int? AbsSetting = null,
    [property: Key(18)] bool? AbsActive = null,
    [property: Key(19)] int? TractionControlSetting = null,
    [property: Key(20)] bool? TractionControlActive = null,
    [property: Key(21)] float? BrakeBias = null) : LivePublisherMessage;

/// <summary>
/// The local car's canonical channels that move over a stint rather than over a corner.
/// </summary>
/// <remarks>
/// <para>
/// A third rate class, and the reason it exists is arithmetic. Tyre pressure, wear and temperature
/// are twelve values a corner once the tread and its window are carried whole, and they were riding
/// <see cref="LiveSelfFrame"/> sixty times a second — the majority of a frame, for readings that
/// change over a stint. Every consumer then decimated them back to about 1 Hz on arrival, so
/// fifty-nine of every sixty samples were serialised, sent, decoded and dropped.
/// </para>
/// <para>
/// <b>Not folded into <see cref="LiveExtrasFrame"/>, despite sharing its cadence.</b> That message
/// is the connector's own document: untyped, simulator-specific, and explicitly carrying sentinels
/// untranslated. These are canonical channels with translated nulls, and putting them there would
/// make a tyre pressure RaceRoom-only and hand every consumer a raw <c>-1</c> to rediscover.
/// </para>
/// <para>
/// The decimation happens at the publisher, not by leaving members null on the fast frame. Null
/// already means "the simulator did not report this", and reusing it for "unchanged since the last
/// frame" would make an unreported tyre indistinguishable from a decimated one.
/// </para>
/// </remarks>
/// <param name="SessionId">The publishing client's session id.</param>
/// <param name="SimDriverId">
/// Which driver this describes, in the same identity space as <see cref="LiveSelfFrame.SimDriverId"/>.
/// </param>
/// <param name="CapturedAtUtc">Wall-clock capture time on the publishing machine.</param>
/// <param name="TyrePressure">Kilopascals, FL/FR/RL/RR. Members are <see langword="null"/> when unreported.</param>
/// <param name="TyreWear">0 (new) to 1 (fully worn), FL/FR/RL/RR.</param>
/// <param name="TyreTemperature">Tread temperatures and the simulator's operating window, FL/FR/RL/RR.</param>
[MessagePackObject]
public sealed record LiveStintFrame(
    [property: Key(0)] Guid SessionId,
    [property: Key(1)] string? SimDriverId,
    [property: Key(2)] DateTimeOffset CapturedAtUtc,
    [property: Key(3)] LiveWheelValues TyrePressure,
    [property: Key(4)] LiveWheelValues TyreWear,
    [property: Key(5)] LiveTyreTemperatures TyreTemperature) : LivePublisherMessage;

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

/// <summary>
/// One tyre's tread temperatures and the operating window the simulator says they belong in.
/// </summary>
/// <remarks>
/// <para>
/// A whole record per tyre rather than a <see cref="LiveWheelValues"/> of one reading, because the
/// two questions a tyre temperature answers are both about differences and neither survives being
/// reduced to a single number. <b>Inner against outer is the camber and pressure story</b> — a front
/// left twenty degrees hotter on its inner shoulder is a setup that is wrong in a nameable way,
/// where the middle reading alone says only "warm". And a reading without its window is a number an
/// engineer has to already know the answer for: 84 °C is cold for one compound and overheating for
/// another, and the simulator is willing to say which.
/// </para>
/// <para>
/// The window travels on every frame rather than once per session because it is a property of the
/// compound currently fitted, and a pit stop can change it mid-race. It costs five floats on a
/// message that already carries fifteen.
/// </para>
/// <para>
/// Every member is nullable for the reason
/// <see cref="Core.Telemetry.TyreTemperature"/> gives: a simulator may not report a reading, and its
/// "not available" sentinel must have been translated to <see langword="null"/> by the connector
/// before it reaches here. A window of nulls means the simulator declined to name a band, and a
/// consumer must draw no band rather than one built from a nominal value.
/// </para>
/// </remarks>
[MessagePackObject]
public sealed record LiveTreadTemperatures(
    [property: Key(0)] float? Inner,
    [property: Key(1)] float? Middle,
    [property: Key(2)] float? Outer,
    [property: Key(3)] float? Optimal,
    [property: Key(4)] float? Cold,
    [property: Key(5)] float? Hot);

/// <summary>Per-tyre tread temperatures in the platform's FL, FR, RL, RR order.</summary>
/// <remarks>
/// The same grouping <see cref="LiveWheelValues"/> uses, for the same reason — four named members
/// rather than four flattened parameters on the frame — but carrying a record per corner instead of
/// a float.
/// </remarks>
[MessagePackObject]
public sealed record LiveTyreTemperatures(
    [property: Key(0)] LiveTreadTemperatures FrontLeft,
    [property: Key(1)] LiveTreadTemperatures FrontRight,
    [property: Key(2)] LiveTreadTemperatures RearLeft,
    [property: Key(3)] LiveTreadTemperatures RearRight);

/// <summary>
/// The local car's simulator-specific channels, at their own slow rate.
/// </summary>
/// <remarks>
/// <para>
/// A channel of its own rather than a member of <see cref="LiveSelfFrame"/>. The payload is a JSON
/// document, and the values in it — car damage, push-to-pass state, tyre subtypes — move on the
/// scale of a race rather than of a frame. Riding it on the 60 Hz stream would mean parsing a
/// document sixty times a second to watch a number that changes after contact, spending the
/// high-rate channel's budget on the low-rate one's data.
/// </para>
/// <para>
/// <b>Sentinels are not translated.</b> The connector writes this raw, so a simulator's "not
/// available" encoding arrives intact — RaceRoom uses <c>-1</c>, which is emphatically not zero. A
/// dashboard that renders <c>damage.engine == -1</c> as an undamaged engine reports the opposite of
/// the truth, and this is the contract that says so.
/// </para>
/// </remarks>
/// <param name="SessionId">The publishing client's session id.</param>
/// <param name="SimDriverId">
/// Which driver this describes, in the same identity space as <see cref="LiveDriverDto.SimDriverId"/>.
/// <see langword="null"/> when the simulator reports no usable identity, in which case the hub falls
/// back to the session announcement the same way it does for a self frame.
/// </param>
/// <param name="CapturedAtUtc">Wall-clock capture time on the publishing machine.</param>
/// <param name="ExtrasJson">The connector's raw JSON document. Never null; may be empty.</param>
[MessagePackObject]
public sealed record LiveExtrasFrame(
    [property: Key(0)] Guid SessionId,
    [property: Key(1)] string? SimDriverId,
    [property: Key(2)] DateTimeOffset CapturedAtUtc,
    [property: Key(3)] string ExtrasJson) : LivePublisherMessage;

/// <summary>Sent when a client stops publishing a session cleanly, so the hub need not wait for a timeout.</summary>
/// <param name="SessionId">The session being closed.</param>
/// <param name="Reason">A short human-readable explanation, shown in the dashboard's client list.</param>
[MessagePackObject]
public sealed record LiveGoodbye(
    [property: Key(0)] Guid SessionId,
    [property: Key(1)] string? Reason) : LivePublisherMessage;
