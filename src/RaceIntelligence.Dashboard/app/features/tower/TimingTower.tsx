import { Fragment, useMemo, type ReactNode } from 'react';
import {
  formatFinishStatus,
  formatGap,
  formatLapTime,
  formatPitLaneState,
  formatPitStopStatus,
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
  /** Drivers whose telemetry is open. Up to two at once, so this is a list rather than a key. */
  focusedDriverKeys: readonly string[];
  /** Adds the driver to the comparison, or removes them if they are already open. */
  onFocus: (driverKey: string) => void;
  /**
   * Focused drivers whose first frame has not arrived yet.
   *
   * Passed in rather than read from the store, so this component stays pure — see `renderDetail`.
   * A click that subscribes and shows nothing looks like a click that missed, and on anything
   * slower than a LAN the window is long enough to click twice.
   */
  pendingDriverKeys?: ReadonlySet<string>;
  /** Driver keys whose detail row is open. Several at once, on purpose. */
  expandedDriverKeys: ReadonlySet<string>;
  onToggleExpand: (driverKey: string) => void;
  /**
   * Whether this is a race, which is the only session where pit state is worth reading.
   *
   * In practice and qualifying a car in the pit lane is a car in the garage, and the simulator's
   * pit-stop fields are stale rather than absent — RaceRoom reports "two tyres unserved" for a full
   * field that will never stop. So outside a race the tower says only that a car is in the lane,
   * and leaves the ladder and the crew's progress out of it.
   */
  isRace?: boolean;
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
  focusedDriverKeys,
  onFocus,
  pendingDriverKeys,
  expandedDriverKeys,
  onToggleExpand,
  isRace = false,
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
          <th className="tower__telemetry">Telemetry</th>
          <th>Laps</th>
          <th>Gap</th>
          <th>Last</th>
          <th>Best</th>
          <th>S1</th>
          <th>S2</th>
          <th>S3</th>
          <th className="tower__state">Status</th>
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => {
          const previous = toPerSector(row.previousSectorMs);
          const personalBest = toPerSector(row.bestSectorMs);
          const sessionBestSectors = bests.sectorMs;
          const isRich = row.tier === 'Self';
          const isFocused = focusedDriverKeys.includes(row.driverKey);
          const isPending = isFocused && pendingDriverKeys?.has(row.driverKey) === true;
          const isExpanded = expandedDriverKeys.has(row.driverKey);
          const finish = formatFinishStatus(row.finishStatus);
          const detailId = `laps-${row.driverKey}`;
          const bestLapClass = bestClass(row.bestLapMs, null, bests.lapMs);

          // Outside a race, the pit lane is the garage and the only honest thing to say is that the
          // car is in it. Inside one, the ladder is what a strategist reads — and the crew's
          // progress only means anything for a car that is actually in the lane, so it is gated on
          // that rather than on the simulator having left a stale code in the field.
          const pit = isRace
            ? formatPitLaneState(row.pitLaneState)
            : row.inPitLane === true
              ? 'PIT'
              : '';
          const crew =
            isRace && row.inPitLane === true ? formatPitStopStatus(row.pitStopStatus) : '';

          return (
            <Fragment key={row.driverKey}>
              <tr
                className={[
                  'tower__row',
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
                    // The only affordance saying which rows have full telemetry behind them, and
                    // therefore one that has to read as a control rather than as decoration — an
                    // unlabelled dot is invisible to anyone not already looking for it. A driver
                    // not running a collector is not a broken row: there is simply nothing more to
                    // show for them than the timing already on this line.
                    <button
                      type="button"
                      className={[
                        'focus-button',
                        isFocused ? 'focus-button--open' : '',
                        isPending ? 'focus-button--pending' : '',
                      ]
                        .filter(Boolean)
                        .join(' ')}
                      aria-label={`Open telemetry for ${row.displayName}`}
                      // Still pressed while pending: the subscription is on, which is what the
                      // control toggles. `aria-busy` is the part that says the data has not
                      // caught up, and it is the distinction a screen reader needs to avoid
                      // announcing an empty panel as though it were the answer.
                      aria-pressed={isFocused}
                      aria-busy={isPending}
                      title="Pedals, tyres and damage — opens below the tower"
                      onClick={() => onFocus(row.driverKey)}
                    >
                      {isPending ? 'Opening…' : isFocused ? 'Shown' : 'Show'}
                    </button>
                  )}
                </td>

                <td className="tower__laps">{row.completedLaps ?? NOT_REPORTED}</td>
                <td className="time">{formatGap(row.gapToCarAheadMs)}</td>

                {/*
                  `previousLapValid`, not `currentLapValid`. The two describe different laps: the
                  simulator's live flag is about the lap being driven right now, so reading it here
                  struck through a clean last lap the instant a driver put a wheel off on the
                  following one — while the lap that was actually binned went by unmarked.
                */}
                <td className={`time ${row.previousLapValid === false ? 'time--invalid' : ''}`}>
                  {formatLapTime(row.previousLapMs)}
                  {row.previousLapValid === false && (
                    // Colour and a strikethrough are not enough on their own. This is the only
                    // signal a colour-blind engineer, or anyone on a screen washed out by garage
                    // lighting, gets that the lap was refused.
                    <span className="pill pill--warn" title="The simulator did not count this lap">
                      INV
                    </span>
                  )}
                </td>

                <td
                  key={`best-lap-${
                    bestLapClass === 'time--session-best' ? row.bestLapMs : 'ordinary'
                  }`}
                  className={`time ${bestLapClass}`}
                >
                  {formatLapTime(row.bestLapMs)}
                </td>

                {Array.from({ length: SECTOR_COUNT }, (_, i) => {
                  const sectorClass = bestClass(
                    previous[i] ?? null,
                    personalBest[i] ?? null,
                    sessionBestSectors[i] ?? null,
                  );

                  return (
                    <td
                      key={`sector-${i}-${
                        sectorClass === 'time--session-best' ? previous[i] : 'ordinary'
                      }`}
                      className={`time ${sectorClass}`}
                    >
                      {formatSector(previous[i] ?? null)}
                    </td>
                  );
                })}

                {/*
                  The pills live in a span rather than being laid out by the cell itself. A `td`
                  given `display: flex` stops being a table cell, so the browser wraps it in an
                  anonymous one — which is why this column used to sit a few pixels adrift of its
                  own header and of the row border. Laying out a child instead leaves the cell a
                  cell.
                */}
                <td className="tower__state">
                  <span className="tower__pills">
                    {finish !== '' && <span className="pill pill--warn">{finish}</span>}
                    {pit !== '' && <span className="pill pill--pit">{pit}</span>}
                    {crew !== '' && <span className="pill pill--muted">{crew}</span>}
                    {/*
                      No count: `penaltyCount` counts penalty types, not penalties, so a driver
                      with three drive-throughs would read as one. The pill says there is
                      something outstanding, which is all the data supports.
                    */}
                    {row.penaltyCount != null && row.penaltyCount > 0 && (
                      <span
                        className="pill pill--penalty"
                        title="The simulator reports a penalty pending for this driver"
                      >
                        PEN
                      </span>
                    )}
                  </span>
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
