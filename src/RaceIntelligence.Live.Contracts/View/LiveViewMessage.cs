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
/// <param name="CurrentLapValid">Whether the lap in progress is still valid.</param>
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
/// <param name="TyreTemperatureCelsius">Core tread temperature, FL/FR/RL/RR.</param>
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
    IReadOnlyList<float?> TyreTemperatureCelsius) : LiveViewMessage;

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
