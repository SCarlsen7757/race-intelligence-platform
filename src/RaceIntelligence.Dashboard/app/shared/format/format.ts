/**
 * Formatting for the timing tower.
 *
 * **One rule runs through all of it: absent is not zero.** The hub omits a value it was not given,
 * and every function here renders that as an em dash rather than a number. A gap of "—" tells a
 * race engineer the simulator did not report it; a gap of "0.0s" tells them the cars are level.
 * Those are different claims and only one of them is true.
 */

/** What every formatter here renders for a value the simulator did not report. */
export const NOT_REPORTED = '—';

const isMissing = (value: number | null | undefined): value is null | undefined =>
  value === null || value === undefined || Number.isNaN(value);

/** A lap or sector time as `m:ss.mmm`, or `ss.mmm` under a minute. */
export function formatLapTime(ms: number | null | undefined): string {
  if (isMissing(ms) || ms < 0) {
    return NOT_REPORTED;
  }

  const totalSeconds = ms / 1000;
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds - minutes * 60;

  return minutes > 0 ? `${minutes}:${seconds.toFixed(3).padStart(6, '0')}` : seconds.toFixed(3);
}

/** A gap as `+1.234`, signed so the direction is unambiguous. */
export function formatGap(ms: number | null | undefined): string {
  if (isMissing(ms)) {
    return NOT_REPORTED;
  }

  const seconds = ms / 1000;
  return `${seconds >= 0 ? '+' : ''}${seconds.toFixed(3)}`;
}

/** A sector split, shown in seconds — nobody reads a sector as minutes. */
export function formatSector(ms: number | null | undefined): string {
  return isMissing(ms) || ms < 0 ? NOT_REPORTED : (ms / 1000).toFixed(3);
}

/** Metres per second as km/h, which is what a dashboard shows. */
export function formatSpeed(metersPerSecond: number | null | undefined): string {
  return isMissing(metersPerSecond) ? NOT_REPORTED : `${Math.round(metersPerSecond * 3.6)}`;
}

/** A 0..1 fraction as a whole percentage. */
export function formatPercent(fraction: number | null | undefined): string {
  return isMissing(fraction) ? NOT_REPORTED : `${Math.round(fraction * 100)}%`;
}

export function formatNumber(value: number | null | undefined, digits = 0): string {
  return isMissing(value) ? NOT_REPORTED : value.toFixed(digits);
}

/** A gear, with the simulator's own encoding: -1 reverse, 0 neutral. */
export function formatGear(gear: number | null | undefined): string {
  if (isMissing(gear)) {
    return NOT_REPORTED;
  }

  if (gear < 0) {
    return 'R';
  }

  return gear === 0 ? 'N' : String(gear);
}

/** Mirrors `PitStopStatus`. -1 means the simulator reports nothing, which is not "not pitting". */
export function formatPitStatus(status: number, inPitLane: boolean | null | undefined): string {
  if (inPitLane === true) {
    return 'PIT';
  }

  switch (status) {
    case 0:
      return 'PIT 2T';
    case 1:
      return 'PIT 4T';
    case 2:
      return 'SERVED';
    default:
      return '';
  }
}

/** Mirrors `DriverFinishStatus`. Empty for a car still running. */
export function formatFinishStatus(status: number): string {
  switch (status) {
    case 1:
      return 'FIN';
    case 2:
      return 'DNF';
    case 3:
      return 'DNQ';
    case 4:
      return 'DNS';
    case 5:
      return 'DSQ';
    default:
      return '';
  }
}

/** How long ago a timestamp was, for the room list's "last seen". */
export function formatAge(isoUtc: string, now: number = Date.now()): string {
  const elapsedMs = now - Date.parse(isoUtc);
  if (!Number.isFinite(elapsedMs) || elapsedMs < 0) {
    return 'now';
  }

  const seconds = Math.floor(elapsedMs / 1000);
  if (seconds < 5) {
    return 'now';
  }

  if (seconds < 60) {
    return `${seconds}s ago`;
  }

  const minutes = Math.floor(seconds / 60);
  return minutes < 60 ? `${minutes}m ago` : `${Math.floor(minutes / 60)}h ago`;
}

/**
 * Session type names.
 *
 * **Takes a game key because the value is the simulator's own raw code, not the canonical
 * `SessionType` numbering.** The connector deliberately passes `session_type` through
 * uninterpreted — translating it belongs to a layer that knows which simulator produced it, and
 * this is that layer.
 *
 * The failure mode if this is read as canonical is quiet and convincing: RaceRoom's 0 is practice
 * where canonical 0 is unknown, so every label shifts by one and a race reports as qualifying.
 * Nothing on screen looks broken. A live session at Daytona is what caught it.
 */
export function formatSessionType(gameKey: string, sessionType: number): string {
  // -1 is RaceRoom's "not available", and is the only value shared across every simulator here.
  if (sessionType < 0) {
    return 'Session';
  }

  if (gameKey === 'raceroom') {
    switch (sessionType) {
      case 0:
        return 'Practice';
      case 1:
        return 'Qualifying';
      case 2:
        return 'Race';
      case 3:
        return 'Warmup';
      default:
        return 'Session';
    }
  }

  // An unrecognised simulator gets no guess. Naming a session wrongly is worse than not naming it.
  return 'Session';
}
