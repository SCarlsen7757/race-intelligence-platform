using System.Text.Json.Serialization;

namespace RaceIntelligence.Live.Contracts.View;

/// <summary>
/// Base type for every message the hub sends to a browser over a viewing WebSocket.
/// </summary>
/// <remarks>
/// <para>
/// JSON, not MessagePack, and a separate hierarchy from <c>Publish.LivePublisherMessage</c> rather
/// than a reuse of it. Two reasons, both of which would be lost by sharing one set of types:
/// </para>
/// <para>
/// The browser is not the collector. It needs no binary decoder, and durations are far more
/// natural as JSON numbers than as .NET <see cref="TimeSpan"/> strings — so times cross this
/// boundary as milliseconds, whereas the publishing contracts keep exact <see cref="TimeSpan"/>
/// values.
/// </para>
/// <para>
/// More importantly, the two directions describe different things. A publishing frame is one
/// machine's observation; a view message is the hub's merged conclusion drawn from several. The
/// view types therefore carry provenance — see <see cref="LiveDataTier"/> — which has no meaning
/// on the way in, because a publisher only ever reports its own view.
/// </para>
/// <para>
/// The discriminator is written as a <c>type</c> property so the browser can switch on it without
/// a schema, and each subtype fixes its own value.
/// </para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RoomListMessage), "roomList")]
[JsonDerivedType(typeof(SessionStateMessage), "sessionState")]
[JsonDerivedType(typeof(TowerSnapshotMessage), "towerSnapshot")]
[JsonDerivedType(typeof(FocusFrameMessage), "focusFrame")]
[JsonDerivedType(typeof(LapHistoryMessage), "lapHistory")]
[JsonDerivedType(typeof(ExtrasFrameMessage), "extrasFrame")]
[JsonDerivedType(typeof(LiveErrorMessage), "error")]
public abstract record LiveViewMessage;

/// <summary>
/// Where a merged value came from, and therefore how much of it there is.
/// </summary>
/// <remarks>
/// The ladder the hub resolves conflicts on: a higher tier always wins, and ties break on the
/// fresher value. It is also what the dashboard uses to decide whether a driver's row can be
/// opened into a full telemetry panel or only shows timing.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<LiveDataTier>))]
public enum LiveDataTier
{
    /// <summary>
    /// Reported by another machine watching this car. Position, gaps, lap and sector times, pit
    /// state — everything a simulator publishes about cars it is not driving.
    /// </summary>
    Observed = 0,

    /// <summary>
    /// Reported by this driver's own machine. Adds the channels no observer can see: pedal inputs,
    /// tyre pressure, tyre wear, fuel.
    /// </summary>
    Self = 1,
}

/// <summary>Every session currently being published, and by whom — the dashboard's landing view.</summary>
/// <param name="Rooms">Active rooms, most recently updated first.</param>
public sealed record RoomListMessage(IReadOnlyList<LiveRoomSummary> Rooms) : LiveViewMessage;

/// <summary>One live session in the room list.</summary>
/// <param name="RoomId">Opaque, stable for the room's lifetime. What a viewer subscribes with.</param>
/// <param name="GameKey">Which simulator.</param>
/// <param name="TrackName">Track name as the simulator reports it.</param>
/// <param name="LayoutName">Layout name as the simulator reports it.</param>
/// <param name="SessionType">
/// The simulator's own raw session type value, uninterpreted — <b>not</b> the canonical
/// <see cref="RaceIntelligence.Core.Sessions.SessionType"/> numbering. Interpret it against
/// <see cref="LiveRoomSummary.GameKey"/>; RaceRoom's 0 is practice, where the canonical 0 is
/// unknown.
/// </param>
/// <param name="DriverCount">Cars in the session, however many clients it took to see them.</param>
/// <param name="Publishers">The clients feeding this room.</param>
/// <param name="LastUpdatedAtUtc">When the hub last received anything for this room.</param>
public sealed record LiveRoomSummary(
    string RoomId,
    string GameKey,
    string TrackName,
    string LayoutName,
    int SessionType,
    int DriverCount,
    IReadOnlyList<LivePublisherSummary> Publishers,
    DateTimeOffset LastUpdatedAtUtc);

/// <summary>One collector feeding a room.</summary>
/// <param name="ClientId">Stable per installation.</param>
/// <param name="ClientName">Human-readable label the client chose for itself.</param>
/// <param name="DriverName">The driver this client is collecting for, when known.</param>
/// <param name="SimDriverId">That driver's simulator identity — which tower row this client enriches.</param>
/// <param name="ConnectedAtUtc">When the publishing connection was established.</param>
/// <param name="Capabilities">
/// What this client's connector can report, as <see cref="RaceIntelligence.Core.Capabilities.SimCapabilities"/>
/// flag names. The dashboard renders panels from this rather than branching on the game key, so a
/// simulator that cannot report tyre wear simply has no tyre wear panel — no frontend change
/// required to add one that can.
/// </param>
public sealed record LivePublisherSummary(
    Guid ClientId,
    string ClientName,
    string? DriverName,
    string? SimDriverId,
    DateTimeOffset ConnectedAtUtc,
    IReadOnlyList<string> Capabilities);

/// <summary>
/// What is true of the session itself rather than of any car in it.
/// </summary>
/// <remarks>
/// <para>
/// A message of its own because the alternatives are both worse. Putting these on
/// <see cref="LiveRoomSummary"/> would tie a mid-race change to the room list, which is broadcast
/// when publishers come and go rather than when a pit window opens; putting them on
/// <see cref="TowerSnapshotMessage"/> would repeat a lap length ten times a second for the life of
/// the session to carry a value that changes twice.
/// </para>
/// <para>
/// <b>Sent when it changes, and once on subscribing.</b> A viewer joining mid-race gets the current
/// state immediately rather than waiting for the next change, which for a pit window that has
/// already opened would be never.
/// </para>
/// </remarks>
/// <param name="RoomId">The room this describes.</param>
/// <param name="LayoutLengthMeters">
/// One lap of this layout, in meters, or <see langword="null"/> when no publisher reported it.
/// <para>
/// This is what makes a lap's average speed computable in the browser — lap length over lap time.
/// It is sent once here rather than as a speed on every <see cref="LapRecord"/> precisely because it
/// is constant for the session: a derived number repeated on every lap of every driver would
/// multiply the largest message the hub sends to save one division.
/// </para>
/// </param>
/// <param name="PitWindow">
/// The session's mandatory pit window, or <see langword="null"/> when no publisher reports one.
/// </param>
/// <param name="RaceLength">
/// How long the race is, or <see langword="null"/> when no publisher reports it.
/// <para>
/// This is what turns a fuel readout into a decision. Litres remaining and litres per lap are both
/// arithmetic a browser can do unaided; whether the fuel reaches the flag needs to know where the
/// flag is, and nothing else on this wire says.
/// </para>
/// </param>
public sealed record SessionStateMessage(
    string RoomId,
    float? LayoutLengthMeters,
    PitWindowState? PitWindow,
    RaceLengthState? RaceLength = null) : LiveViewMessage;

/// <summary>How long the race is, as a browser sees it.</summary>
/// <remarks>
/// <para>
/// <see cref="Unit"/> crosses as a <b>string</b> for the reason <see cref="PitWindowState"/> gives:
/// this message is low-rate, so the bytes buy nothing back, and a browser matching on
/// <c>"Laps"</c> cannot drift out of step with the server the way a hand-copied table of integers
/// can.
/// </para>
/// <para>
/// <b>Both figures are present and only <see cref="Unit"/> says which governs.</b> A consumer that
/// picked whichever was non-null would divide fuel by a lap count in a session that ends on the
/// clock, and report a comfortable margin in a race the car cannot finish.
/// </para>
/// </remarks>
/// <param name="Laps">Total laps, or null when the race is not run to a lap count.</param>
/// <param name="DurationSeconds">Total length in seconds, or null when not run to a clock.</param>
/// <param name="Unit">Which of the two ends the race.</param>
public sealed record RaceLengthState(
    int? Laps,
    double? DurationSeconds,
    RaceLengthUnitView Unit);

/// <summary>What a race's length is measured in. Mirrors <see cref="RaceIntelligence.Core.Sessions.RaceLengthUnit"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RaceLengthUnitView>))]
public enum RaceLengthUnitView
{
    /// <summary>The simulator did not say, so neither figure can be trusted to govern.</summary>
    Unknown,

    /// <summary>The race ends after a fixed number of laps.</summary>
    Laps,

    /// <summary>The race ends after a fixed duration.</summary>
    Time,
}

/// <summary>The session's mandatory pit window, as a browser sees it.</summary>
/// <remarks>
/// <see cref="Status"/> and <see cref="Unit"/> cross this boundary as <b>strings</b> where the
/// publishing wire carries ints, following <see cref="LiveDataTier"/> rather than
/// <c>TowerRow.PitLaneState</c>. Two reasons: this message is low-rate, so the extra bytes buy
/// nothing back, and a browser matching on <c>"open"</c> cannot drift out of step with the server the
/// way a hand-maintained table of integer codes silently does.
/// </remarks>
/// <param name="Status">Whether the window is accepting stops.</param>
/// <param name="Start">
/// Where the window opens, in <paramref name="Unit"/>, or <see langword="null"/> when the simulator
/// does not report it. Never a sentinel — the connector turns RaceRoom's <c>-1</c> into null, so a
/// banner cannot announce a window opening on lap −1.
/// </param>
/// <param name="End">Where the window closes, in <paramref name="Unit"/>. See <paramref name="Start"/>.</param>
/// <param name="Unit">What the two bounds are counted in.</param>
public sealed record PitWindowState(
    PitWindowStatusView Status,
    int? Start,
    int? End,
    PitWindowUnitView Unit);

/// <summary>Whether a session's pit window is accepting stops. Mirrors <see cref="RaceIntelligence.Core.Sessions.PitWindowStatus"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PitWindowStatusView>))]
public enum PitWindowStatusView
{
    /// <summary>The simulator does not report a pit window for this session.</summary>
    Unavailable,

    /// <summary>This session has no mandatory pit window.</summary>
    Disabled,

    /// <summary>A window exists but is not currently open — either not yet, or no longer.</summary>
    Closed,

    /// <summary>The window is open and a stop taken now counts.</summary>
    Open,

    /// <summary>The local car is stopped in its box during the window.</summary>
    Stopped,

    /// <summary>The local car has served its mandatory stop.</summary>
    Completed,
}

/// <summary>What a pit window's bounds are measured in. Mirrors <see cref="RaceIntelligence.Core.Sessions.PitWindowUnit"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PitWindowUnitView>))]
public enum PitWindowUnitView
{
    /// <summary>The simulator did not say, so neither bound can be labelled.</summary>
    Unknown,

    /// <summary>Lap numbers.</summary>
    Laps,

    /// <summary>Minutes of session time elapsed.</summary>
    Minutes,
}

/// <summary>The merged timing tower for one room.</summary>
/// <remarks>
/// A whole snapshot rather than a delta. Positions and gaps are only internally consistent as a
/// set, and at the tower's rate a full field is a few kilobytes — small enough that the bug surface
/// of a delta protocol is not worth buying. If bandwidth ever forces the issue, a delta message can
/// be added alongside this without changing it.
/// </remarks>
/// <param name="RoomId">The room this describes.</param>
/// <param name="CapturedAtUtc">When the hub assembled it.</param>
/// <param name="Drivers">Every known car, sorted by position.</param>
public sealed record TowerSnapshotMessage(
    string RoomId,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<TowerRow> Drivers) : LiveViewMessage;

/// <summary>One row of the merged timing tower.</summary>
/// <param name="DriverKey">
/// The hub's identity for this car within the room, stable across publishers. What a viewer names
/// when subscribing to a focus stream, and what the browser should key its rows on.
/// </param>
/// <param name="DisplayName">The driver's name.</param>
/// <param name="CarNumber">Car number, when reported.</param>
/// <param name="SimCarId">Simulator-specific car model id.</param>
/// <param name="SimCarClassId">Simulator-specific car class id.</param>
/// <param name="Position">Overall position, 1-based.</param>
/// <param name="PositionInClass">Position within class, 1-based.</param>
/// <param name="CompletedLaps">Laps fully completed, or null when unreported.</param>
/// <param name="TrackPositionFraction">Progress around the current lap, 0..1.</param>
/// <param name="Sector">Current sector, 1-based.</param>
/// <param name="SpeedMetersPerSecond">Current speed.</param>
/// <param name="CurrentLapMs">Elapsed time on the lap in progress, in milliseconds.</param>
/// <param name="PreviousLapMs">Most recently completed lap, in milliseconds.</param>
/// <param name="BestLapMs">Best lap this session, in milliseconds.</param>
/// <param name="CurrentLapValid">
/// Whether the lap <b>in progress</b> is still valid — the lap the car is on right now, whose time
/// is <paramref name="CurrentLapMs"/>. It says nothing about <paramref name="PreviousLapMs"/>; that
/// is what <paramref name="PreviousLapValid"/> is for.
/// </param>
/// <param name="PreviousLapValid">
/// Whether the most recently <b>completed</b> lap was valid — the one timed by
/// <paramref name="PreviousLapMs"/>.
/// <para>
/// Supplied by the hub's lap accumulator rather than read off the standings snapshot, because no
/// snapshot contains it: the simulator's validity flag describes the lap in progress, so by the time
/// a snapshot reports the lap counter has advanced, the flag has already moved on to the new lap.
/// The accumulator is the thing that holds the value observed a tick earlier.
/// </para>
/// <para>
/// <see langword="null"/> until the hub has watched this driver finish a lap, which includes the
/// first lap after a viewer or publisher connects mid-session. Unknown validity is
/// <b>not</b> invalidity and must not be rendered as it.
/// </para>
/// </param>
/// <param name="CurrentSectorMs">Cumulative splits for the lap in progress; null entries are sectors not yet reached.</param>
/// <param name="PreviousSectorMs">Cumulative splits for the most recently completed lap.</param>
/// <param name="BestSectorMs">Cumulative splits for the best lap.</param>
/// <param name="GapToCarAheadMs">Gap to the car ahead on track, in milliseconds.</param>
/// <param name="GapToCarBehindMs">Gap to the car behind on track, in milliseconds.</param>
/// <param name="InPitLane">Whether the car is in the pit lane.</param>
/// <param name="PitLaneState">
/// Where the car is in the act of pitting, as <see cref="RaceIntelligence.Core.Sessions.PitLaneState"/>.
/// Graded for a car whose own machine is publishing; for everyone else it is what the publisher was
/// able to infer, down to a bare "in the pit lane".
/// </param>
/// <param name="PitStopStatus">Pit stop progress, as <see cref="RaceIntelligence.Core.Sessions.PitStopStatus"/>.</param>
/// <param name="PitStopCount">Pit stops completed.</param>
/// <param name="FinishStatus">How the car's session ended, as <see cref="RaceIntelligence.Core.Sessions.DriverFinishStatus"/>.</param>
/// <param name="PenaltyCount">Penalties outstanding.</param>
/// <param name="Tier">
/// Whether this row is enriched by the driver's own client. <see cref="LiveDataTier.Self"/> means a
/// focus stream is available for it; <see cref="LiveDataTier.Observed"/> means timing only.
/// </param>
public sealed record TowerRow(
    string DriverKey,
    string DisplayName,
    int? CarNumber,
    string? SimCarId,
    string? SimCarClassId,
    int? Position,
    int? PositionInClass,
    int? CompletedLaps,
    float? TrackPositionFraction,
    int? Sector,
    float? SpeedMetersPerSecond,
    double? CurrentLapMs,
    double? PreviousLapMs,
    double? BestLapMs,
    bool? CurrentLapValid,
    bool? PreviousLapValid,
    IReadOnlyList<double?> CurrentSectorMs,
    IReadOnlyList<double?> PreviousSectorMs,
    IReadOnlyList<double?> BestSectorMs,
    double? GapToCarAheadMs,
    double? GapToCarBehindMs,
    bool? InPitLane,
    int PitLaneState,
    int PitStopStatus,
    int? PitStopCount,
    int FinishStatus,
    int? PenaltyCount,
    LiveDataTier Tier);

/// <summary>
/// The rich channels for one driver, at the publishing collector's full poll rate.
/// </summary>
/// <remarks>
/// Only ever produced for a driver whose own machine is publishing — the tier that has pedal and
/// tyre data at all. Sent only to viewers who have explicitly subscribed to that driver, since at
/// this rate broadcasting it for every car in a full field to every viewer is exactly the cost the
/// two-rate design exists to avoid.
/// </remarks>
/// <param name="RoomId">The room this belongs to.</param>
/// <param name="DriverKey">Which driver, matching <see cref="TowerRow.DriverKey"/>.</param>
/// <param name="CapturedAtUtc">Capture time on the publishing machine, for a latency readout.</param>
/// <param name="SimulationTime">The simulator's clock at capture.</param>
/// <param name="LapNumber">Current lap.</param>
/// <param name="Sector">Current sector.</param>
/// <param name="TrackPositionFraction">Progress around the lap, 0..1.</param>
/// <param name="SpeedMetersPerSecond">Speed.</param>
/// <param name="Throttle">0..1.</param>
/// <param name="Brake">0..1.</param>
/// <param name="Clutch">
/// 0 (engaged) to 1 (fully disengaged), and absent from the JSON entirely when the simulator does
/// not report it — a car with an automatic clutch. The dashboard must distinguish "no clutch
/// channel" from "clutch fully up", which is why this is nullable rather than defaulted to 0.
/// </param>
/// <param name="Steering">-1 (full left) to 1 (full right).</param>
/// <param name="Gear">-1 reverse, 0 neutral, positive forward gear.</param>
/// <param name="EngineRpm">Revolutions per minute.</param>
/// <param name="FuelLeftLiters">Fuel remaining.</param>
/// <param name="TyrePressureKpa">Kilopascals, FL/FR/RL/RR.</param>
/// <param name="TyreWear">0 (new) to 1 (fully worn), FL/FR/RL/RR.</param>
/// <param name="TyreTemperatureCelsius">Tread temperatures and the simulator's window, FL/FR/RL/RR.</param>
public sealed record FocusFrameMessage(
    string RoomId,
    string DriverKey,
    DateTimeOffset CapturedAtUtc,
    double SimulationTime,
    int LapNumber,
    int Sector,
    float? TrackPositionFraction,
    float SpeedMetersPerSecond,
    float? Throttle,
    float? Brake,
    float? Clutch,
    float Steering,
    int? Gear,
    float EngineRpm,
    float FuelLeftLiters,
    IReadOnlyList<float?> TyrePressureKpa,
    IReadOnlyList<float?> TyreWear,
    IReadOnlyList<TreadTemperatures> TyreTemperatureCelsius,
    int? AbsSetting = null,
    bool? AbsActive = null,
    int? TractionControlSetting = null,
    bool? TractionControlActive = null,
    float? BrakeBias = null) : LiveViewMessage;

/// <summary>
/// One tyre's tread temperatures and the operating window the simulator says they belong in, °C.
/// </summary>
/// <remarks>
/// <para>
/// The viewer-facing mirror of <see cref="Publish.LiveTreadTemperatures"/>; see that type for why a
/// tyre's temperature is three readings and a band rather than one number.
/// </para>
/// <para>
/// <b>A null is not a reading and must never be drawn as one.</b> An unreported shoulder leaves a
/// hole in the chart; an unreported window means the simulator named no band, and the dashboard
/// draws none rather than inventing one from a nominal value. The same rule the rest of this
/// contract states for throttle, pressure and wear.
/// </para>
/// <para>
/// <paramref name="Optimal"/>, <paramref name="Cold"/> and <paramref name="Hot"/> are the three
/// members a brake temperature carries too, under the same names in the extras document. That is
/// deliberate: an operating window is one idea, and a reader who has learned it here should not have
/// to learn it again for brakes.
/// </para>
/// </remarks>
/// <param name="Inner">Inner tread temperature. Null when not reported.</param>
/// <param name="Middle">Middle tread temperature. Null when not reported.</param>
/// <param name="Outer">Outer tread temperature. Null when not reported.</param>
/// <param name="Optimal">Where the simulator says this compound wants to be. Null when not reported.</param>
/// <param name="Cold">Below this, the simulator considers the tyre cold. Null when not reported.</param>
/// <param name="Hot">Above this, the simulator considers the tyre overheating. Null when not reported.</param>
public sealed record TreadTemperatures(
    float? Inner,
    float? Middle,
    float? Outer,
    float? Optimal,
    float? Cold,
    float? Hot);

/// <summary>
/// The simulator-specific channels for the focused driver, at roughly 1 Hz.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes <c>SimCapabilities.Damage</c> mean something: the collector has always
/// advertised it in its hello while no damage value ever crossed the wire, because the connector
/// wrote damage into a sample's extras and the live mapper dropped it. It now travels on a channel
/// sized for it.
/// </para>
/// <para>
/// <b>The payload is opaque to the hub</b> — carried through as the string the connector wrote,
/// never parsed here. That is the "flexible metadata" half of the platform's data model working as
/// intended: a simulator exposing a field nobody anticipated costs a connector and a dashboard
/// panel, not a change to this contract.
/// </para>
/// <para>
/// <b>Sentinels are not translated.</b> RaceRoom reports <c>-1</c> for a value it does not have, and
/// a panel that renders that as zero damage says the car is fine when the truth is that nobody
/// knows.
/// </para>
/// </remarks>
/// <param name="RoomId">The room this belongs to.</param>
/// <param name="DriverKey">Which driver, matching <see cref="TowerRow.DriverKey"/>.</param>
/// <param name="CapturedAtUtc">Capture time on the publishing machine.</param>
/// <param name="Extras">
/// The simulator's own document, as raw JSON text. For RaceRoom this carries
/// <c>damage.engine</c>, <c>damage.transmission</c>, <c>damage.aerodynamics</c> and
/// <c>damage.suspension</c>, each 0..1 or <c>-1</c> for unavailable.
/// </param>
public sealed record ExtrasFrameMessage(
    string RoomId,
    string DriverKey,
    DateTimeOffset CapturedAtUtc,
    string Extras) : LiveViewMessage;

/// <summary>
/// Every completed lap the hub has watched one driver finish.
/// </summary>
/// <remarks>
/// <para>
/// <b>Always a full snapshot for one driver, never a delta.</b> That shape is what lets this share
/// the same conflating treatment as the tower and the focus stream: a viewer too slow to keep up
/// has older snapshots collapsed into the newest, and because each one restates the whole history,
/// a collapsed message can never leave a hole where a lap used to be. An incremental
/// "lap N completed" event would be strictly smaller and would develop exactly that hole the first
/// time a viewer fell behind — the failure a race engineer would least notice and least forgive.
/// </para>
/// <para>
/// Sent for any driver a viewer has subscribed to, whatever their <see cref="LiveDataTier"/>. Lap
/// history is read out of the standings snapshot, which describes every car in the session, so it
/// does not depend on the driver running a collector the way a focus stream does.
/// </para>
/// </remarks>
/// <param name="RoomId">The room this belongs to.</param>
/// <param name="DriverKey">Which driver, matching <see cref="TowerRow.DriverKey"/>.</param>
/// <param name="Laps">Completed laps in ascending lap order. Empty until the driver finishes one.</param>
/// <param name="Truncated">
/// <see langword="true"/> when the oldest laps have been dropped to stay within the hub's per-driver
/// cap, so the list starts partway through the session. A dashboard must not present a truncated
/// history as a whole stint.
/// </param>
public sealed record LapHistoryMessage(
    string RoomId,
    string DriverKey,
    IReadOnlyList<LapRecord> Laps,
    bool Truncated) : LiveViewMessage;

/// <summary>One completed lap.</summary>
/// <param name="LapNumber">1-based, and contiguous within a history.</param>
/// <param name="LapTimeMs">
/// The lap's time in milliseconds, or <see langword="null"/> when the hub watched the lap count go
/// up without seeing a snapshot that described the lap — a publisher that was away for longer than
/// a lap. Null is "this lap happened and its time is unknown", which is why the lap is listed at all
/// rather than silently skipped.
/// </param>
/// <param name="SectorMs">
/// Cumulative sector splits in milliseconds. Entries are <see langword="null"/> for sectors the
/// simulator did not report, and empty when the lap itself was not described.
/// </param>
/// <param name="Valid">
/// Whether the lap counted, or <see langword="null"/> when unknown. This is the flag observed one
/// tick <i>before</i> the lap count advanced: the simulator's validity flag describes the lap in
/// progress, so the value carried on the snapshot that reports the completion already belongs to
/// the next lap.
/// </param>
public sealed record LapRecord(
    int LapNumber,
    double? LapTimeMs,
    IReadOnlyList<double?> SectorMs,
    bool? Valid);

/// <summary>Reports a problem with a viewer's request — an unknown room, or a malformed subscription.</summary>
/// <param name="Code">A short stable code the browser can branch on, e.g. <c>unknownRoom</c>.</param>
/// <param name="Message">A human-readable explanation.</param>
public sealed record LiveErrorMessage(string Code, string Message) : LiveViewMessage;
