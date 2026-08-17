import type { TowerRow } from '../live/contracts';

/**
 * Sector arithmetic, shared by the timing tower and the lap-history rows.
 *
 * Shared rather than duplicated because the discipline in here is the whole value of it: both
 * views must refuse to invent a split in exactly the same way, and two copies of a rule like that
 * eventually disagree.
 */

export const SECTOR_COUNT = 3;

/**
 * How close two times have to be to count as the same time, in milliseconds.
 *
 * These are floats that made a round trip through the simulator, the wire and JSON. An exact
 * equality test would leave a genuine session best rendered as an ordinary time now and then,
 * which looks like a bug in the colouring rather than in the comparison.
 */
export const BEST_TOLERANCE_MS = 1;

/** Session bests, computed once per snapshot rather than per cell. */
export interface SessionBests {
  lapMs: number | null;
  sectorMs: (number | null)[];
}

/**
 * Cumulative splits into per-sector durations.
 *
 * The wire carries them cumulative — S3 is the whole lap — because that is the form the connector
 * normalised the simulator's two possible conventions into. A race engineer reads sectors
 * individually, so the subtraction happens here. A gap in the sequence stops it: a missing S2
 * makes S3 undeterminable, and inventing a number for it would be worse than showing nothing.
 */
export function toPerSector(cumulative: (number | null)[]): (number | null)[] {
  const out: (number | null)[] = [];
  let previous = 0;

  for (let i = 0; i < SECTOR_COUNT; i++) {
    const value = cumulative[i];
    if (value == null || value < previous) {
      out.push(null);
      previous = Number.NaN;
      continue;
    }

    out.push(Number.isNaN(previous) ? null : value - previous);
    previous = value;
  }

  return out;
}

export function computeSessionBests(rows: TowerRow[]): SessionBests {
  let lapMs: number | null = null;
  const sectorMs: (number | null)[] = Array.from({ length: SECTOR_COUNT }, () => null);

  for (const row of rows) {
    if (row.bestLapMs != null && (lapMs === null || row.bestLapMs < lapMs)) {
      lapMs = row.bestLapMs;
    }

    // Converted before comparing. The wire carries cumulative splits, so a raw comparison would
    // pit one driver's cumulative S2 against another's per-sector S2 and colour the tower by a
    // number that means nothing.
    const perSector = toPerSector(row.bestSectorMs);

    for (let i = 0; i < SECTOR_COUNT; i++) {
      const value = perSector[i];
      const current = sectorMs[i] ?? null;
      if (value != null && (current === null || value < current)) {
        sectorMs[i] = value;
      }
    }
  }

  return { lapMs, sectorMs };
}

/**
 * Purple for a session best, green for a personal best.
 *
 * The convention every sim-racing timing screen uses. A race engineer already reads it without
 * thinking, and inventing a different scheme here would cost accuracy at exactly the wrong moment.
 */
export function bestClass(
  value: number | null | undefined,
  personalBest: number | null | undefined,
  sessionBest: number | null | undefined,
): string {
  if (value == null) {
    return '';
  }

  if (sessionBest != null && value <= sessionBest + BEST_TOLERANCE_MS) {
    return 'time--session-best';
  }

  return personalBest != null && value <= personalBest + BEST_TOLERANCE_MS
    ? 'time--personal-best'
    : '';
}
