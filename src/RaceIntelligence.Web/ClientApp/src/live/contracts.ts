/**
 * TypeScript mirrors of `RaceIntelligence.Live.Contracts.View`.
 *
 * Hand-written rather than generated. Six types is well under the threshold where a code generator
 * pays for its toolchain, and the drift these would otherwise be prone to is covered from the C#
 * side instead: `LiveViewContractShapeTests` asserts the serialized property names against the
 * names below, so a rename in C# fails the build rather than silently making a field `undefined`
 * here.
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
  steering: number;
  gear?: number | null;
  engineRpm: number;
  fuelLeftLiters: number;
  tyrePressureKpa: (number | null)[];
  tyreWear: (number | null)[];
  tyreTemperatureCelsius: (number | null)[];
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

/** Follows one driver's full-rate channels, or stops with `driverKey: null`. */
export interface FocusDriverCommand {
  type: 'focusDriver';
  driverKey: string | null;
}

export type LiveViewCommand = WatchRoomCommand | FocusDriverCommand;

/** Wheel order on every per-wheel array crossing the wire. */
export const WHEELS = ['FL', 'FR', 'RL', 'RR'] as const;
