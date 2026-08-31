/**
 * The read API's wire shapes, mirrored by hand.
 *
 * The same arrangement as `shared/live/contracts.ts`, and the same conventions: durations end in
 * `Ms`, and a field the server omits is optional here rather than nullable-and-present.
 *
 * Hand-written rather than generated because the surface is four records and a generator would be
 * more machinery than the thing it generates. The C# side is
 * `src/RaceIntelligence.Read.Api/Contracts/ReadContracts.cs`.
 */

/**
 * One stored session, as the picker needs it.
 *
 * `sessionType`, `fuelUsageRate` and `tyreWearRate` are the **simulator's own raw codes**,
 * untranslated — exactly as they arrive over the live socket, and for the same reason. For RaceRoom
 * the rates encode `-1` not available, `0` off, `1`–`4` for 1x–4x. Note `-1` sorts below `0`: "the
 * rate was on" is `> 0`, never "non-zero".
 */
export interface StoredSession {
  readonly sessionId: string;
  readonly startedAtUtc: string;
  readonly endedAtUtc?: string;
  /** The driver's current display name. */
  readonly driverName?: string;
  /** The name reported for this session specifically — what to show if the driver has since renamed. */
  readonly playerName?: string;
  readonly trackName?: string;
  readonly layoutName?: string;
  readonly carName?: string;
  readonly sessionType: number;
  readonly fuelUsageRate?: number;
  readonly tyreWearRate?: number;
  readonly lapCount: number;
  /** Zero means there is nothing to chart, however many laps were recorded. */
  readonly sampleCount: number;
}

/** A page of sessions, newest first. */
export interface StoredSessionPage {
  readonly sessions: readonly StoredSession[];
  /** Pass as `before` to fetch the next page; absent when this page is the last. */
  readonly nextBefore?: string;
}

/** One lap's summary statistics. Speeds are metres per second, as stored. */
export interface StoredLap {
  readonly lapNumber: number;
  readonly lapTimeMs?: number;
  readonly fuelUsed?: number;
  readonly avgSpeed?: number;
  readonly maxSpeed?: number;
  readonly isValid: boolean;
}

/**
 * One telemetry sample.
 *
 * Canonical channels only — a simulator's promoted columns are not on this wire. See the C#
 * contract for why that is structural rather than an omission.
 */
export interface StoredSample {
  readonly sequenceNumber: number;
  readonly timestampUtc: string;
  readonly simulationTime: number;
  readonly lapNumber: number;
  readonly sector: number;
  /** Metres per second. */
  readonly speed: number;
  readonly throttle?: number;
  readonly brake?: number;
  readonly clutch?: number;
  readonly steering: number;
  readonly gear?: number;
  readonly engineRpm: number;
  readonly fuelLeft: number;
  readonly position?: number;
  readonly trackPositionFraction?: number;
}

/** The samples recorded for one lap, in capture order. */
export interface StoredLapSamples {
  readonly lapNumber: number;
  readonly samples: readonly StoredSample[];
}

/**
 * The samples recorded for the laps a request named.
 *
 * Keyed by lap even when one lap was asked for: an overlay of two to four laps is the normal way
 * stored telemetry is read, so a caller charting a single lap indexes into a list of one rather
 * than meeting a second response shape.
 */
export interface StoredTelemetry {
  readonly sessionId: string;
  readonly laps: readonly StoredLapSamples[];
}
