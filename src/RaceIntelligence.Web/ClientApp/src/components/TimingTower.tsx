import { useMemo } from 'react';
import type { TowerRow } from '../live/contracts';
import {
  formatFinishStatus,
  formatGap,
  formatLapTime,
  formatPitStatus,
  formatSector,
  NOT_REPORTED,
} from '../live/format';

interface TimingTowerProps {
  rows: TowerRow[];
  focusedDriverKey: string | null;
  onFocus: (driverKey: string) => void;
}

/** Session bests, computed once per snapshot rather than per cell. */
interface SessionBests {
  lapMs: number | null;
  sectorMs: (number | null)[];
}

const SECTOR_COUNT = 3;

function computeSessionBests(rows: TowerRow[]): SessionBests {
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
 * Cumulative splits into per-sector durations.
 *
 * The wire carries them cumulative — S3 is the whole lap — because that is the form the connector
 * normalised the simulator's two possible conventions into. A race engineer reads sectors
 * individually, so the subtraction happens here. A gap in the sequence stops it: a missing S2
 * makes S3 undeterminable, and inventing a number for it would be worse than showing nothing.
 */
function toPerSector(cumulative: (number | null)[]): (number | null)[] {
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

function sectorClass(
  value: number | null,
  personalBest: number | null,
  sessionBest: number | null,
): string {
  if (value == null) {
    return '';
  }

  // Compared with a millisecond of tolerance. These are floats that made a round trip through the
  // simulator, the wire and JSON, and an exact equality test would leave a genuine session best
  // rendered as an ordinary time now and then.
  if (sessionBest != null && value <= sessionBest + 1) {
    return 'time--session-best';
  }

  return personalBest != null && value <= personalBest + 1 ? 'time--personal-best' : '';
}

export function TimingTower({ rows, focusedDriverKey, onFocus }: TimingTowerProps) {
  const bests = useMemo(() => computeSessionBests(rows), [rows]);

  if (rows.length === 0) {
    return (
      <div className="empty empty--inline">
        <p>Waiting for the first timing update…</p>
      </div>
    );
  }

  return (
    <table className="tower">
      <thead>
        <tr>
          <th className="tower__pos">#</th>
          <th className="tower__driver">Driver</th>
          <th>Laps</th>
          <th>Gap</th>
          <th>Last</th>
          <th>Best</th>
          <th>S1</th>
          <th>S2</th>
          <th>S3</th>
          <th className="tower__state" />
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => {
          const previous = toPerSector(row.previousSectorMs);
          const personalBest = toPerSector(row.bestSectorMs);
          const sessionBestSectors = bests.sectorMs;
          const isRich = row.tier === 'Self';
          const isFocused = row.driverKey === focusedDriverKey;
          const finish = formatFinishStatus(row.finishStatus);
          const pit = formatPitStatus(row.pitStopStatus, row.inPitLane);

          return (
            <tr
              key={row.driverKey}
              className={[
                'tower__row',
                isRich ? 'tower__row--rich' : '',
                isFocused ? 'tower__row--focused' : '',
                row.inPitLane === true ? 'tower__row--pit' : '',
              ]
                .filter(Boolean)
                .join(' ')}
            >
              <td className="tower__pos">{row.position ?? NOT_REPORTED}</td>

              <td className="tower__driver">
                {isRich ? (
                  <button
                    type="button"
                    className="driver-button"
                    onClick={() => onFocus(row.driverKey)}
                    // The only affordance saying which rows have telemetry behind them. A driver
                    // not running a collector is not a broken row — there is simply nothing more
                    // to show for them.
                    title="Full telemetry available — open the focus panel"
                  >
                    <span className="driver-button__mark" aria-hidden="true" />
                    {row.displayName}
                  </button>
                ) : (
                  <span className="driver-name">{row.displayName}</span>
                )}
                {row.carNumber != null && <span className="car-number">#{row.carNumber}</span>}
              </td>

              <td>{row.completedLaps ?? NOT_REPORTED}</td>
              <td className="time">{formatGap(row.gapToCarAheadMs)}</td>

              <td className={`time ${row.currentLapValid === false ? 'time--invalid' : ''}`}>
                {formatLapTime(row.previousLapMs)}
              </td>

              <td
                className={`time ${
                  bests.lapMs != null && row.bestLapMs != null && row.bestLapMs <= bests.lapMs + 1
                    ? 'time--session-best'
                    : ''
                }`}
              >
                {formatLapTime(row.bestLapMs)}
              </td>

              {Array.from({ length: SECTOR_COUNT }, (_, i) => (
                <td
                  key={i}
                  className={`time ${sectorClass(
                    previous[i] ?? null,
                    personalBest[i] ?? null,
                    sessionBestSectors[i] ?? null,
                  )}`}
                >
                  {formatSector(previous[i] ?? null)}
                </td>
              ))}

              <td className="tower__state">
                {finish !== '' && <span className="pill pill--warn">{finish}</span>}
                {pit !== '' && <span className="pill pill--pit">{pit}</span>}
                {row.penaltyCount != null && row.penaltyCount > 0 && (
                  <span className="pill pill--penalty">{row.penaltyCount}P</span>
                )}
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
