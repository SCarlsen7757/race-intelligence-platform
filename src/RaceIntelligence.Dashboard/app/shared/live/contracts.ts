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
  currentLapValid?: boolean | null;
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
  /**
   * Absent on a simulator that does not report a clutch, and on any collector older than the
   * schema version that added it. Not zero — an unreported clutch is not a released one.
   */
  clutch?: number | null;
  steering: number;
  gear?: number | null;
  engineRpm: number;
  fuelLeftLiters: number;
  tyrePressureKpa: (number | null)[];
  tyreWear: (number | null)[];
  tyreTemperatureCelsius: (number | null)[];
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
 * The low-rate channel, carrying whatever the connector wrote into `Extras` for the focused driver.
 *
 * Roughly 1 Hz by design, and on its own channel precisely so parsing it never happens at focus
 * rate — `extras` is a JSON string and parsing it sixty times a second would be pure waste.
 *
 * **The values inside are raw.** Nothing upstream translates a simulator's sentinels, so a
 * consumer must know them: RaceRoom writes `-1` for "not available", which is emphatically not the
 * same as zero.
 */
export interface ExtrasFrameMessage {
  type: 'extrasFrame';
  roomId: string;
  driverKey: string;
  capturedAtUtc: string;
  /** The connector's raw document, still a string. Named to match the hub's `Extras` property. */
  extras: string;
}

export interface LiveErrorMessage {
  type: 'error';
  code: LiveErrorCode;
  message: string;
}

export type LiveViewMessage =
  | RoomListMessage
  | TowerSnapshotMessage
  | FocusFrameMessage
  | LapHistoryMessage
  | ExtrasFrameMessage
  | LiveErrorMessage;

/** Mirrors `LiveErrorCodes`. Branch on these, never on the message text. */
export type LiveErrorCode =
  | 'unknownRoom'
  | 'roomClosed'
  | 'unknownDriver'
  | 'noTelemetryForDriver'
  | 'tooManyFocusDrivers'
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
 * Additive and capped, following the same pattern `subscribeLapHistory` does — two cars on screen at
 * once is what a comparison is. The cap lives on the hub (`LiveViewer.MaxFocusDrivers`) because
 * focus frames are the 60 Hz channel and the viewing endpoint is open; asking for one too many is
 * answered with `tooManyFocusDrivers` rather than by evicting a driver already being watched.
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

/** RaceRoom's car damage, as it appears under `damage` in the extras document. */
export interface RaceRoomDamage {
  engine?: number;
  transmission?: number;
  aerodynamics?: number;
  suspension?: number;
}

/** The shape this dashboard reads out of RaceRoom's extras document. Every field is optional. */
export interface RaceRoomExtras {
  damage?: RaceRoomDamage;
}
