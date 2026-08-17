import { useMemo } from 'react';
import {
  averageLapSpeed,
  formatLapTime,
  formatSector,
  formatSpeed,
} from '../../shared/format/format';
import {
  bestClass,
  SECTOR_COUNT,
  toPerSector,
  type SessionBests,
} from '../../shared/format/sectors';
import type { LapRecord } from '../../shared/live/contracts';

interface LapHistoryProps {
  laps: LapRecord[] | null;
  truncated: boolean;
  /** The tower's session bests, so a lap here is coloured against the same field it raced. */
  sessionBests: SessionBests;
  /**
   * One lap of this layout in metres, for the average speed column.
   *
   * Null when no publisher has reported it, which leaves every average blank rather than reaching
   * for a nominal length from somewhere else.
   */
  layoutLengthMeters: number | null;
}

/** One driver's own fastest lap and sectors, over the laps shown. */
interface PersonalBests {
  lapMs: number | null;
  sectorMs: (number | null)[];
}

/**
 * A driver's personal bests, from valid laps only.
 *
 * An invalid lap is one the simulator refused to count, so letting it set a personal best would
 * paint a green sector for a time that officially never happened.
 */
function computePersonalBests(laps: LapRecord[]): PersonalBests {
  let lapMs: number | null = null;
  const sectorMs: (number | null)[] = Array.from({ length: SECTOR_COUNT }, () => null);

  for (const lap of laps) {
    // Only an explicit false bars a lap. Unknown validity is not invalidity — a simulator that
    // never reports the flag would otherwise have every one of its laps silently excluded, leaving
    // a driver with no personal best at all.
    if (lap.valid === false) {
      continue;
    }

    if (lap.lapTimeMs != null && (lapMs === null || lap.lapTimeMs < lapMs)) {
      lapMs = lap.lapTimeMs;
    }

    const perSector = toPerSector(lap.sectorMs);

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
 * Every completed lap of one driver's session.
 *
 * The shape of a stint is the thing this whole feature exists to show — where the tyres went off,
 * where traffic cost a second, which sector a set-up change actually helped. A single
 * `previousLapMs` scalar can answer none of that.
 *
 * Pure: it is handed laps rather than fetching them, so it renders identically in a test and in a
 * race. `LapHistoryPanel` is the half that talks to the hub.
 */
export function LapHistory({ laps, truncated, sessionBests, layoutLengthMeters }: LapHistoryProps) {
  const personal = useMemo(() => computePersonalBests(laps ?? []), [laps]);

  // Only laps the simulator explicitly refused. A lap with no validity reported is not counted here
  // for the same reason it is not struck through: unknown is not invalid, and a simulator that never
  // reports the flag must not have a driver's whole stint announced as binned.
  const invalidCount = useMemo(
    () => (laps ?? []).filter((lap) => lap.valid === false).length,
    [laps],
  );

  if (laps === null) {
    return <p className="laps__note">Loading laps…</p>;
  }

  if (laps.length === 0) {
    return <p className="laps__note">No completed laps yet.</p>;
  }

  return (
    <div className="laps">
      {truncated && (
        // Said out loud rather than shown as a shorter list, because a race engineer counting
        // stints off this table would otherwise silently miscount the opening one.
        <p className="laps__note">Earliest laps dropped — showing the most recent only.</p>
      )}

      {invalidCount > 0 && (
        // Counted rather than left to be read off the struck rows. "How many did they lose?" is a
        // question asked of a stint constantly, and answering it by eye means counting strikethroughs
        // down a table that may be forty rows long.
        <p className="laps__note">
          {invalidCount} of {laps.length} lap{laps.length === 1 ? '' : 's'} invalidated by the
          simulator.
        </p>
      )}

      <table className="laps__table">
        <thead>
          <tr>
            <th>Lap</th>
            <th>Time</th>
            {/* km/h, matching the focus panel's readout — see `formatSpeed`. */}
            <th>Avg</th>
            <th>S1</th>
            <th>S2</th>
            <th>S3</th>
            {/* Holds the invalid marker. Headed for screen readers rather than shown, because a
                column that is empty on every valid lap does not need a visible title. */}
            <th className="visually-hidden">Validity</th>
          </tr>
        </thead>
        <tbody>
          {laps.map((lap) => {
            const perSector = toPerSector(lap.sectorMs);
            const isInvalid = lap.valid === false;

            return (
              <tr key={lap.lapNumber} className={isInvalid ? 'laps__row--invalid' : ''}>
                <td className="laps__number">{lap.lapNumber}</td>

                <td
                  className={`time ${
                    isInvalid
                      ? 'time--invalid'
                      : bestClass(lap.lapTimeMs, personal.lapMs, sessionBests.lapMs)
                  }`}
                >
                  {formatLapTime(lap.lapTimeMs)}
                </td>

                {/* Struck alongside the time when the lap was refused, so a fast average cannot be
                    read off a lap that officially never happened. */}
                <td className={`time ${isInvalid ? 'time--invalid' : ''}`}>
                  {formatSpeed(averageLapSpeed(layoutLengthMeters, lap.lapTimeMs))}
                </td>

                {Array.from({ length: SECTOR_COUNT }, (_, i) => (
                  <td
                    key={i}
                    className={`time ${
                      isInvalid
                        ? 'time--invalid'
                        : bestClass(
                            perSector[i] ?? null,
                            personal.sectorMs[i] ?? null,
                            sessionBests.sectorMs[i] ?? null,
                          )
                    }`}
                  >
                    {formatSector(perSector[i] ?? null)}
                  </td>
                ))}

                <td>
                  {isInvalid && (
                    // A pill, not just the colour and the strikethrough it already had. Colour alone
                    // is invisible to a colour-blind engineer and on a washed-out screen in a lit
                    // garage, and a strikethrough on its own reads as "deleted" rather than as "the
                    // simulator refused this one". The title says which.
                    <span className="pill pill--warn" title="The simulator did not count this lap">
                      INV
                    </span>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
