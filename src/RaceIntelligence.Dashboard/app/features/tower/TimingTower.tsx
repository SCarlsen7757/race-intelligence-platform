import { Fragment, useMemo, type ReactNode } from 'react';
import {
  formatFinishStatus,
  formatGap,
  formatLapTime,
  formatPitStatus,
  formatSector,
  NOT_REPORTED,
} from '../../shared/format/format';
import {
  bestClass,
  computeSessionBests,
  SECTOR_COUNT,
  toPerSector,
  type SessionBests,
} from '../../shared/format/sectors';
import type { TowerRow } from '../../shared/live/contracts';

interface TimingTowerProps {
  rows: TowerRow[];
  focusedDriverKey: string | null;
  onFocus: (driverKey: string) => void;
  /** Driver keys whose detail row is open. Several at once, on purpose. */
  expandedDriverKeys: ReadonlySet<string>;
  onToggleExpand: (driverKey: string) => void;
  /**
   * What to show inside an expanded row.
   *
   * A render prop rather than a direct import so this component stays pure: it needs no live
   * connection to be rendered, which is what makes it testable against a plain array of rows.
   */
  renderDetail?: (driverKey: string, sessionBests: SessionBests) => ReactNode;
}

/** Every column above, so an expanded row's detail cell can span the whole table. */
const COLUMN_COUNT = 11;

export function TimingTower({
  rows,
  focusedDriverKey,
  onFocus,
  expandedDriverKeys,
  onToggleExpand,
  renderDetail,
}: TimingTowerProps) {
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
          <th className="tower__telemetry" />
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
          const isExpanded = expandedDriverKeys.has(row.driverKey);
          const finish = formatFinishStatus(row.finishStatus);
          const pit = formatPitStatus(row.pitStopStatus, row.inPitLane);
          const detailId = `laps-${row.driverKey}`;

          return (
            <Fragment key={row.driverKey}>
              <tr
                className={[
                  'tower__row',
                  isRich ? 'tower__row--rich' : '',
                  isFocused ? 'tower__row--focused' : '',
                  isExpanded ? 'tower__row--expanded' : '',
                  row.inPitLane === true ? 'tower__row--pit' : '',
                ]
                  .filter(Boolean)
                  .join(' ')}
              >
                <td className="tower__pos">{row.position ?? NOT_REPORTED}</td>

                <td className="tower__driver">
                  {/*
                    Every driver expands, not only the ones running a collector: lap history comes
                    from standings, which the hub has for the whole field. A native button, so it
                    is keyboard operable without reinventing focus handling.
                  */}
                  <button
                    type="button"
                    className="driver-button"
                    aria-expanded={isExpanded}
                    aria-controls={detailId}
                    onClick={() => onToggleExpand(row.driverKey)}
                  >
                    <span className="driver-button__chevron" aria-hidden="true">
                      {isExpanded ? '▾' : '▸'}
                    </span>
                    {row.displayName}
                  </button>
                  {row.carNumber != null && <span className="car-number">#{row.carNumber}</span>}
                </td>

                <td className="tower__telemetry">
                  {isRich && (
                    // The only affordance saying which rows have full telemetry behind them. A
                    // driver not running a collector is not a broken row — there is simply nothing
                    // more to show for them than the timing already on this line.
                    <button
                      type="button"
                      className="focus-button"
                      aria-label={`Open telemetry for ${row.displayName}`}
                      title="Full telemetry available — open the focus panel"
                      onClick={() => onFocus(row.driverKey)}
                    >
                      <span className="focus-button__mark" aria-hidden="true" />
                    </button>
                  )}
                </td>

                <td>{row.completedLaps ?? NOT_REPORTED}</td>
                <td className="time">{formatGap(row.gapToCarAheadMs)}</td>

                <td className={`time ${row.currentLapValid === false ? 'time--invalid' : ''}`}>
                  {formatLapTime(row.previousLapMs)}
                </td>

                <td className={`time ${bestClass(row.bestLapMs, null, bests.lapMs)}`}>
                  {formatLapTime(row.bestLapMs)}
                </td>

                {Array.from({ length: SECTOR_COUNT }, (_, i) => (
                  <td
                    key={i}
                    className={`time ${bestClass(
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

              {isExpanded && (
                <tr className="tower__detail" id={detailId}>
                  <td colSpan={COLUMN_COUNT}>{renderDetail?.(row.driverKey, bests)}</td>
                </tr>
              )}
            </Fragment>
          );
        })}
      </tbody>
    </table>
  );
}
