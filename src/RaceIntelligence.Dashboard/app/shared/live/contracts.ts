/**
 * TypeScript mirrors of `RaceIntelligence.Live.Contracts.View`.
 *
 * Hand-written rather than generated. A handful of types is well under the threshold where a code
 * generator pays for its toolchain, and the drift these would otherwise be prone to is covered from
 * the C# side instead: `LiveViewContractShapeTests` asserts the serialized property names against
 * the names below, so a rename in C# fails the build rather than silently making a field
 * `undefined` here.
 *
 * Two conventions carried over from the wire, both worth knowing before reading a component:
 *
 * - **Durations are milliseconds**, never `TimeSpan` strings. Anything named `...Ms` is a number.
 * - **Nulls are omitted.** The hub serializes with `WhenWritingNull`, so an absent lap time is a
 *   missing property, not `null`. Every optional field is therefore `?: T | null` — either form
 *   can arrive depending on the field, and code must treat both as "not reported". It must not
 *   treat either as zero: a gap the simulator does not report is not a gap of 0.0s, and that
 *   distinction is what someone makes a pit call on.
 */

/** Where a merged value came from, and therefore how much of it there is. */
export type LiveDataTier = 'Observed' | 'Self';

/** One collector feeding a room. */
export interface LivePublisherSummary {
  clientId: string;
  clientName: string;
  driverName?: string | null;
  simDriverId?: string | null;
  connectedAtUtc: string;
  /** `SimCapabilities` flag names. Panels are chosen from these, never from the game key. */
  capabilities: string[];
}

/** One live session in the room list. */
export interface LiveRoomSummary {
  roomId: string;
  gameKey: string;
  trackName: string;
  layoutName: string;
  sessionType: number;
  driverCount: number;
  publishers: LivePublisherSummary[];
  lastUpdatedAtUtc: string;
}

/** One row of the merged timing tower. */
export interface TowerRow {
  driverKey: string;
  displayName: string;
  carNumber?: number | null;
  simCarId?: string | null;
  simCarClassId?: string | null;
  position?: number | null;
  positionInClass?: number | null;
  completedLaps?: number | null;
  trackPositionFraction?: number | null;
  sector?: number | null;
  speedMetersPerSecond?: number | null;
  currentLapMs?: number | null;
  previousLapMs?: number | null;
  bestLapMs?: number | null;
  /**
   * Whether the lap **in progress** is still valid — the one timed by `currentLapMs`.
   *
   * It says nothing about `previousLapMs`. Reading it against the completed lap strikes a clean lap
   * through the moment the driver goes off on the next one, and leaves the lap that was actually
   * binned looking legitimate.
   */
  currentLapValid?: boolean | null;
  /**
   * Whether the most recently **completed** lap was valid — the one timed by `previousLapMs`.
   *
   * Absent until the hub has watched this driver finish a lap, which includes the first lap after
   * connecting mid-session. Absent is "not known" and must never render as invalid.
   */
  previousLapValid?: boolean | null;
  /** Cumulative splits. Entries are null for sectors not yet reached. */
  currentSectorMs: (number | null)[];
  previousSectorMs: (number | null)[];
  bestSectorMs: (number | null)[];
  gapToCarAheadMs?: number | null;
  gapToCarBehindMs?: number | null;
  inPitLane?: boolean | null;
  /**
   * `PitLaneState`: -1 unavailable, 0 on track, 1 stop requested, 2 entering, 3 stopped in the box,
   * 4 exiting, 5 in the pit lane at an unknown stage.
   *
   * 5 is a weaker claim than 2, not a synonym: the simulator reports the graded ladder only for the
   * car being driven locally, and a publisher that could not work out which rung a rival is on says
   * so rather than picking one.
   */
  pitLaneState: number;
  /** `PitStopStatus`: -1 unavailable, 0 two tyres unserved, 1 four unserved, 2 served. */
  pitStopStatus: number;
  pitStopCount?: number | null;
  /** `DriverFinishStatus`: -1 unavailable, 0 running, 1 finished, 2 DNF, 3 DNQ, 4 DNS, 5 DSQ. */
  finishStatus: number;
  penaltyCount?: number | null;
  tier: LiveDataTier;
}

export interface RoomListMessage {
  type: 'roomList';
  rooms: LiveRoomSummary[];
}

export interface TowerSnapshotMessage {
  type: 'towerSnapshot';
  roomId: string;
  capturedAtUtc: string;
  drivers: TowerRow[];
}

/**
 * Whether a session's mandatory pit window is accepting stops.
 *
 * Strings rather than the integer codes the per-car pit fields use. This message is low-rate, so the
 * bytes cost nothing, and a name cannot silently drift out of step with the server the way a
 * hand-maintained table of numbers does.
 *
 * `'Closed'` covers both "not open yet" and "already gone by" — the simulator does not distinguish
 * them, so neither does this. `'Stopped'` and `'Completed'` describe the *publishing* car's stop
 * rather than the session, since that is the only car whose box the simulator reports.
 */
export type PitWindowStatus =
  'Unavailable' | 'Disabled' | 'Closed' | 'Open' | 'Stopped' | 'Completed';

/**
 * What a pit window's bounds are counted in.
 *
 * Never inferred from the session type. The same integer is lap 25 in a lap race and the 25-minute
 * mark in a timed one, and guessing wrong puts a mandatory stop most of an hour from where it is.
 */
export type PitWindowUnit = 'Unknown' | 'Laps' | 'Minutes';

/** The session's mandatory pit window. */
export interface PitWindowState {
  status: PitWindowStatus;
  /**
   * Where the window opens, in `unit`, or absent when the simulator does not report it.
   *
   * Never a sentinel: the connector turns RaceRoom's `-1` into null, so this cannot say lap −1.
   */
  start?: number | null;
  /** Where the window closes, in `unit`. See `start`. */
  end?: number | null;
  unit: PitWindowUnit;
}

/**
 * What is true of the session itself rather than of any car in it.
 *
 * Sent once on watching a room and again whenever it changes — which is roughly twice a race. A
 * viewer joining mid-session is told the current state immediately rather than waiting for a change
 * that, for a window which opened ten minutes ago, would never come.
 */
export interface SessionStateMessage {
  type: 'sessionState';
  roomId: string;
  /**
   * One lap of this layout, in meters, or absent when no publisher reported it.
   *
   * This is what makes a lap's average speed computable: lap length over lap time. It is sent once
   * per session rather than as a speed on every `LapRecord` because it is constant — a derived
   * number repeated on every lap of every driver would bloat the largest message on the wire to save
   * one division.
   */
  layoutLengthMeters?: number | null;
  /** Absent when the session has no mandatory window, or the simulator reports none. */
  pitWindow?: PitWindowState | null;
  /** Absent when no publisher reports a race length. See `RaceLengthState`. */
  raceLength?: RaceLengthState | null;
}

/**
 * What ends the race.
 *
 * `'Time'` rather than `'Minutes'` — unlike a pit window's bounds, a duration crosses the wire in
 * seconds, and naming the unit after a figure it is not expressed in is how a division ends up out
 * by sixty.
 */
export type RaceLengthUnit = 'Unknown' | 'Laps' | 'Time';

/**
 * How long the race is.
 *
 * **Both figures may be present, and only `unit` says which one governs.** A simulator will happily
 * report a lap count in a session that ends on the clock. Reading whichever is non-null would divide
 * fuel by the wrong number and promise a margin in a race the car cannot finish, which is precisely
 * the mistake `PitWindowUnit` exists to prevent one field further down.
 */
export interface RaceLengthState {
  /** Total laps, or absent when the race is not run to a lap count. */
  laps?: number | null;
  /** Total length in seconds, or absent when not run to a clock. */
  durationSeconds?: number | null;
  unit: RaceLengthUnit;
}

/**
 * The band a simulator says a temperature belongs in.
 *
 * Its own type because two different readings want it — tyre tread and brake discs — and an
 * operating window is one idea. A reader who has learned it in one place should not have to learn it
 * again in the other, and a chart that draws the band can take either. It arrives on
 * {@link SlowFrameMessage} as an {@link OperatingWindowRow} per corner; this is the shape a chart
 * reads it in once a corner has been picked out.
 *
 * **Every bound is optional and absent means the simulator named no band.** Draw nothing in that
 * case. A window invented from a nominal value is worse than no window: it tells an engineer their
 * tyres are cold with the same confidence the simulator would have used to tell them the truth.
 */
export interface OperatingWindow {
  /** Where the simulator says this compound or pad wants to be. */
  optimal?: number | null;
  /** Below this, the simulator considers it cold. */
  cold?: number | null;
  /** Above this, the simulator considers it overheating. */
  hot?: number | null;
}

/**
 * One tyre's temperatures across the tread. Celsius.
 *
 * Three readings rather than one because the spread across the tread is the question a temperature
 * is usually being asked: **inner against outer is the camber and pressure story**, and a front left
 * twenty degrees hotter on its inner shoulder is a setup that is wrong in a nameable way. The middle
 * reading alone — which is all this wire used to carry — says only "warm".
 *
 * **The window used to be here and is not any more.** It never changed between frames, so sending
 * it per tyre per frame was five constants riding a 1 Hz message; it comes on
 * {@link SlowFrameMessage} now, keyed by compound, which is also what makes it stay correct across a
 * stop onto a different tyre.
 *
 * A null shoulder is a hole in the chart, never a zero.
 */
export interface TreadTemperatures {
  inner?: number | null;
  middle?: number | null;
  outer?: number | null;
}

/** The rich channels, for a driver whose own machine is publishing. Per-wheel arrays are FL, FR, RL, RR. */
export interface FocusFrameMessage {
  type: 'focusFrame';
  roomId: string;
  driverKey: string;
  capturedAtUtc: string;
  simulationTime: number;
  lapNumber: number;
  sector: number;
  trackPositionFraction?: number | null;
  speedMetersPerSecond: number;
  throttle?: number | null;
  brake?: number | null;
  absSetting?: number | null;
  absActive?: boolean | null;
  tractionControlSetting?: number | null;
  tractionControlActive?: boolean | null;
  brakeBias?: number | null;
  /**
   * Absent on a simulator that does not report a clutch, and on any collector older than the
   * schema version that added it. Not zero — an unreported clutch is not a released one.
   */
  clutch?: number | null;
  steering: number;
  gear?: number | null;
  engineRpm: number;
  fuelLeftLiters: number;
  /**
   * Kilonewtons at each corner, FL/FR/RL/RR.
   *
   * On the fast frame, beside the pedal that caused it — the two share a sample index, which is what
   * makes "what the driver asked for against what arrived at the corner" a comparison rather than
   * two traces sampled at unrelated moments. A null is unreported, never zero: a corner drawn at
   * zero reads as a brake that did nothing.
   */
  brakePressureKiloNewtons: (number | null)[];
}

/**
 * The focused driver's tyre channels, at roughly 1 Hz.
 *
 * These used to ride `FocusFrameMessage` at the collector's full poll rate and were the majority of
 * it — twelve values a corner, sixty times a second, for readings the store then thinned straight
 * back to about this rate. The decimation was right and was simply happening three processes too
 * late; the wire does it now.
 *
 * Shares its cadence with {@link SlowFrameMessage}, and stays a separate channel: a tyre is read
 * across a stint where damage is read the moment it happens, and the two want different tiles.
 */
export interface StintFrameMessage {
  type: 'stintFrame';
  roomId: string;
  driverKey: string;
  capturedAtUtc: string;
  tyrePressureKpa: (number | null)[];
  tyreWear: (number | null)[];
  tyreTemperatureCelsius: TreadTemperatures[];
}

/** One completed lap, as the hub's accumulator recorded it. */
export interface LapRecord {
  lapNumber: number;
  lapTimeMs?: number | null;
  /** Cumulative splits, exactly as on a tower row — S3 is the whole lap. */
  sectorMs: (number | null)[];
  /**
   * Whether the simulator still considered the lap valid when it completed.
   *
   * The hub captures this a tick before the lap counter increments, because the flag it comes from
   * describes the lap *in progress*.
   *
   * Absent when the simulator never reported it, which is **not** the same as invalid. Only an
   * explicit `false` means the lap was refused; unknown validity must not strike a lap through or
   * bar it from setting a personal best.
   */
  valid?: boolean | null;
}

/**
 * Every lap one driver has completed in this session.
 *
 * **Always a full snapshot for one driver, never a delta.** A snapshot can sit in a conflating
 * slot and be dropped in favour of a newer one without ever developing a hole, which is why the
 * hub sends history this way rather than as incremental lap events.
 */
export interface LapHistoryMessage {
  type: 'lapHistory';
  roomId: string;
  driverKey: string;
  laps: LapRecord[];
  /** Set once the per-driver cap has dropped the oldest laps, so the view can say so. */
  truncated: boolean;
}

/**
 * The low-rate channel: every slow-moving channel for the focused driver, typed.
 *
 * Roughly 1 Hz by design. It used to be an opaque JSON string the store parsed once a frame, with a
 * hand-written mirror of the document's shape that nothing checked and a warning attached that the
 * simulator's `-1` sentinels were untranslated — so a panel that trusted a number reported a brake
 * at minus one degree. The channels are named and nullable now, and the connector turned every
 * sentinel into a `null` before the sample left the collector.
 *
 * **The sample is sent whole.** At 1 Hz, with nulls omitted, that costs a few hundred bytes and
 * removes the judgement about which channels count as slow — a judgement that failed silently when
 * it was made wrong.
 */
export interface SlowFrameMessage {
  type: 'slowFrame';
  roomId: string;
  driverKey: string;
  capturedAtUtc: string;
  sample: RaceRoomSample;
  /** The tyre and brake bands in force, one per corner. Constant for a compound, hence not on the sample. */
  operatingWindows: OperatingWindowRow[];
}

export interface LiveErrorMessage {
  type: 'error';
  code: LiveErrorCode;
  message: string;
}

export type LiveViewMessage =
  | RoomListMessage
  | SessionStateMessage
  | TowerSnapshotMessage
  | FocusFrameMessage
  | LapHistoryMessage
  | StintFrameMessage
  | SlowFrameMessage
  | LiveErrorMessage;

/** Mirrors `LiveErrorCodes`. Branch on these, never on the message text. */
export type LiveErrorCode =
  | 'unknownRoom'
  | 'roomClosed'
  | 'unknownDriver'
  | 'noTelemetryForDriver'
  | 'notWatchingRoom'
  | 'malformedCommand';

/** Subscribes to a room's timing tower, or leaves with `roomId: null`. */
export interface WatchRoomCommand {
  type: 'watchRoom';
  roomId: string | null;
}

/**
 * Adds a driver to the set whose full-rate channels this viewer receives, or drops **all** of them
 * with `driverKey: null`.
 *
 * Additive and uncapped, following the same pattern `subscribeLapHistory` does — a race engineer
 * comparing several cars at once needs all of them on screen, not just two.
 */
export interface FocusDriverCommand {
  type: 'focusDriver';
  driverKey: string | null;
}

/** Drops one driver's full-rate stream, leaving every other focus untouched. */
export interface UnfocusDriverCommand {
  type: 'unfocusDriver';
  driverKey: string;
}

/**
 * Asks for one driver's lap history, or drops **all** of them with `driverKey: null`.
 *
 * Several drivers can be subscribed at once — the hub conflates per driver — because a race
 * engineer comparing stints has more than one row open. That is also why `null` means "drop them
 * all" rather than "drop the one": with a set rather than a single slot, dropping one has to name
 * it, which is what `unsubscribeLapHistory` is for.
 */
export interface SubscribeLapHistoryCommand {
  type: 'subscribeLapHistory';
  driverKey: string | null;
}

/** Drops one driver's lap history, leaving every other subscription untouched. */
export interface UnsubscribeLapHistoryCommand {
  type: 'unsubscribeLapHistory';
  driverKey: string;
}

export type LiveViewCommand =
  | WatchRoomCommand
  | FocusDriverCommand
  | UnfocusDriverCommand
  | SubscribeLapHistoryCommand
  | UnsubscribeLapHistoryCommand;

/** Wheel order on every per-wheel array crossing the wire. */
export const WHEELS = ['FL', 'FR', 'RL', 'RR'] as const;

/**
 * One corner's tyre and brake temperature band, keyed by the compound it belongs to.
 *
 * **These are constants**, which is why they are not on a sample: measured over one recorded
 * session the six bounds had exactly one distinct value each against 119,146 for the reading they
 * bound. They are keyed by compound because the one thing that moves them is fitting a different
 * tyre, so a stop that switches compound gets its own row rather than rewriting the earlier stint's.
 *
 * `corner` is `0..3` — FL, FR, RL, RR, the same order as every per-wheel array on this wire.
 */
export interface OperatingWindowRow {
  corner: number;
  compound?: number;
  tyreOptimalCelsius?: number;
  tyreColdCelsius?: number;
  tyreHotCelsius?: number;
  brakeOptimalCelsius?: number;
  brakeColdCelsius?: number;
  brakeHotCelsius?: number;
}

/**
 * One RaceRoom telemetry sample, as the slow channel sends it.
 *
 * **Mirrored by hand from `channels/raceroom-telemetry.channels`**, which is the single declaration
 * every C# restatement of these channels is generated from. Only the channels this dashboard reads
 * are typed here — the manifest has a hundred and seventy-five and a wall does not draw them all —
 * and `ReadContractShapeTests` on the C# side asserts that every name below is one the manifest
 * declares, so a rename there fails a test rather than blanking a tile.
 *
 * **Absent means the simulator did not report it.** Every field is optional because the wire omits
 * nulls, and that is the whole improvement over the JSON document this replaced: there is no `-1`
 * left to mistake for a reading.
 */
export interface RaceRoomSample {
  // Damage, 0..1.
  damageEngine?: number;
  damageTransmission?: number;
  damageAerodynamics?: number;
  damageSuspension?: number;

  // Penalties. `maxIncidentPoints` is the server's disqualification limit, absent when there is none.
  incidentPoints?: number;
  maxIncidentPoints?: number;
  cutTrackWarnings?: number;

  // Tyre compounds, per axle. RaceRoom's own subtype codes, untranslated.
  tyreTypeFront?: number;
  tyreTypeRear?: number;
  tyreSubtypeFront?: number;
  tyreSubtypeRear?: number;

  // Per-corner channels. FL, FR, RL, RR.
  brakeTempFl?: number;
  brakeTempFr?: number;
  brakeTempRl?: number;
  brakeTempRr?: number;
  /**
   * Grip loss measured directly rather than inferred from lap time.
   *
   * The reason the connector's per-tyre block existed at all: a degradation model built without it
   * can only see the symptom.
   */
  tyreGripFl?: number;
  tyreGripFr?: number;
  tyreGripRl?: number;
  tyreGripRr?: number;
  tyreLoadNewtonsFl?: number;
  tyreLoadNewtonsFr?: number;
  tyreLoadNewtonsRl?: number;
  tyreLoadNewtonsRr?: number;
  tyreDirtFl?: number;
  tyreDirtFr?: number;
  tyreDirtRl?: number;
  tyreDirtRr?: number;
  /** A tri-state, not a fraction: `0` false, `1` true, absent when unreported. */
  tyreFlatspotFl?: number;
  tyreFlatspotFr?: number;
  tyreFlatspotRl?: number;
  tyreFlatspotRr?: number;
  tyreSurfaceMaterialFl?: number;
  tyreSurfaceMaterialFr?: number;
  tyreSurfaceMaterialRl?: number;
  tyreSurfaceMaterialRr?: number;
  tyreRotationRadiansPerSecondFl?: number;
  tyreRotationRadiansPerSecondFr?: number;
  tyreRotationRadiansPerSecondRl?: number;
  tyreRotationRadiansPerSecondRr?: number;

  // Engine and drivetrain health. Trends rather than instants — an oil temperature that has climbed
  // for ten laps is information, and the same number seen once is not.
  engineTempCelsius?: number;
  engineOilTempCelsius?: number;
  engineOilPressureKpa?: number;
  fuelPressureKpa?: number;
  turboPressureBar?: number;

  // Energy, for the cars that have it.
  batteryStateOfChargePercent?: number;
  virtualEnergyLeftMj?: number;
  virtualEnergyCapacityMj?: number;
  virtualEnergyPerLapMj?: number;

  // Flags. Each is a count or a 0/1, as the simulator reports it.
  flagYellow?: number;
  flagBlue?: number;
  flagBlack?: number;
  flagGreen?: number;
  flagCheckered?: number;
  flagWhite?: number;
  flagBlackAndWhite?: number;

  // DRS. `drsActivationsUnlimited` is what tells "endless" apart from "unreported": RaceRoom encodes
  // endless as int32::max, and the connector turns both into an absent count plus this flag.
  drsEquipped?: number;
  drsAvailable?: number;
  drsEngaged?: number;
  drsActivationsLeft?: number;
  drsActivationsUnlimited?: boolean;
  drsActivationsTotal?: number;

  // Push-to-pass.
  pushToPassAvailable?: number;
  pushToPassEngaged?: number;
  pushToPassAmountLeft?: number;
  pushToPassEngagedTimeLeftSeconds?: number;
  pushToPassWaitTimeLeftSeconds?: number;

  // Pit state. The simulator's own integers, distinct from `PitWindowState` on the session message,
  // which is the hub's translated view.
  pitWindowStatus?: number;
  pitWindowStart?: number;
  pitWindowEnd?: number;
  pitState?: number;
  pitAction?: number;
  pitStopsPerformed?: number;
  pitTotalDurationSeconds?: number;
  pitElapsedTimeSeconds?: number;
}
