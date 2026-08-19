import { useMemo, useState } from 'react';
import type { TowerRow } from '../../shared/live/contracts';
import { RACE_TRACE_CAPACITY } from '../../shared/live/store';
import { useTower } from '../../shared/live/useLive';
import type { SimPanelProps } from '../../sims/registry';
import { LiveChart, type LiveChartSpec } from '../focus/LiveChart';
import { WHEEL_COLOURS } from '../focus/traceColours';

/**
 * How many cars the timeline draws before it stops being readable.
 *
 * Six. A thirty-car field is thirty lines crossing each other on a tile a few hundred pixels tall,
 * which is a picture of a race rather than a reading of one. The default is far narrower still —
 * see {@link defaultSelection}.
 */
const MAX_LINES = 6;

/**
 * Whether a pit code is a car actually in the pit lane.
 *
 * **`-1` is "unavailable", and unavailable is not "not in the pits".** The distinction matters here
 * because this widget marks stops: reading the sentinel as a negative would quietly report that
 * nobody in the field has stopped all race, which is a claim a strategist might act on.
 */
function inPits(row: TowerRow): boolean | null {
  if (row.inPitLane !== null && row.inPitLane !== undefined) {
    return row.inPitLane;
  }

  return row.pitLaneState < 0 ? null : row.pitLaneState > 0;
}

/**
 * Who the timeline shows before the user chooses.
 *
 * The selected car and the leader, which is the comparison a race engineer is making by default:
 * "are we taking time out of the front, or losing it". Everything else is opt-in, because every
 * extra line costs legibility for every other line.
 */
function defaultSelection(rows: readonly TowerRow[], driverKey: string): string[] {
  const leader = rows.find((row) => row.position === 1);
  const keys = new Set<string>();

  if (leader !== undefined) {
    keys.add(leader.driverKey);
  }

  if (rows.some((row) => row.driverKey === driverKey)) {
    keys.add(driverKey);
  }

  return [...keys];
}

/**
 * The race as it happened: every shown car's gap to the leader, against elapsed time.
 *
 * **Read across it and the race explains itself.** A line converging steadily is a genuine pace
 * advantage. A line that steps sideways is a pit stop. Two lines crossing just after one of those
 * steps is an undercut that worked, and the picture says so without anybody having to reconstruct
 * it from a timing screen afterwards.
 *
 * **This is the one widget on the wall that works for the whole field.** Almost everything else
 * needs a collector running on the car, so it can only ever describe one driver; standings cover
 * every entry in the session, which is why the track map works for a whole grid where telemetry
 * works for one. That is the reason to give this tile room.
 *
 * The lines come from the store's decimated race rings — one sample a second, derived once per
 * snapshot — rather than being recomputed here. Gap to leader is not on the wire at all: the wire
 * carries the gap to the car ahead, and the leader's own gap is a running sum down the running
 * order. Two widgets summing that independently would be two chances to sum it differently.
 *
 * ### Why this is a driver widget and not a room widget
 *
 * It is about the room, and it should have been the catalogue's first `scope: 'room'` entry. It
 * takes a driver key anyway, for one reason: it needs to know **which line is yours**. A gap chart
 * that cannot pick the selected car out of six is a chart you have to read a legend to use, and the
 * selection is not reachable from `RoomPanelProps`, which is deliberately only the store.
 *
 * The alternative was drawing the top few cars and never highlighting one, which is a worse widget
 * for the sake of a cleaner scope. Registering it as room-scoped and threading the selection in
 * would mean widening `RoomPanelProps` for every future room widget — see the note on #70.
 */
export function RaceTimeline({ store, driverKey }: SimPanelProps) {
  const tower = useTower();
  const rows = useMemo(() => tower?.drivers ?? [], [tower]);

  // Null until the user picks, so the default can keep following the leader as the lead changes
  // hands rather than freezing on whoever led when the tile mounted.
  const [chosen, setChosen] = useState<string[] | null>(null);
  const shown = useMemo(
    () => (chosen ?? defaultSelection(rows, driverKey)).slice(0, MAX_LINES),
    [chosen, rows, driverKey],
  );

  const byKey = useMemo(() => new Map(rows.map((row) => [row.driverKey, row])), [rows]);

  const spec: LiveChartSpec = {
    capacity: RACE_TRACE_CAPACITY,
    scales: { y: {} },
    series: shown.map((key, index) => ({
      id: key,
      label: byKey.get(key)?.displayName ?? key,
      // The selected car always takes the first colour, so it is the same colour on every tile that
      // draws several cars.
      stroke: WHEEL_COLOURS[index % WHEEL_COLOURS.length]!,
      width: key === driverKey ? 2 : 1.25,
      buffer: () => store.raceTracesFor(key).gapToLeaderSeconds,
    })),
  };

  if (rows.length === 0) {
    return <p className="wall__unavailable">No standings yet.</p>;
  }

  return (
    <div className="race-timeline">
      <LiveChart
        store={store}
        driverKey={driverKey}
        spec={spec}
        className="race-timeline__plot"
        height={160}
      />

      <div className="race-timeline__cars">
        {rows.slice(0, 24).map((row) => {
          const on = shown.includes(row.driverKey);
          const pits = inPits(row);

          return (
            <button
              key={row.driverKey}
              type="button"
              className={`race-timeline__car${on ? ' race-timeline__car--on' : ''}`}
              aria-pressed={on}
              onClick={() =>
                setChosen(
                  on
                    ? shown.filter((key) => key !== row.driverKey)
                    : [...shown, row.driverKey].slice(-MAX_LINES),
                )
              }
            >
              <span className="race-timeline__pos">{row.position ?? '—'}</span>
              <span className="race-timeline__name">{row.displayName}</span>
              {/* Stops marked here rather than on the plot: a marker on a line the user has turned
                  off would be a stop with nothing to attach to. A null pit code says nothing at
                  all, which is not the same as saying the car is out on track. */}
              {pits === true && <span className="race-timeline__pit">BOX</span>}
              {row.pitStopCount !== null &&
                row.pitStopCount !== undefined &&
                row.pitStopCount > 0 && (
                  <span className="race-timeline__stops">{row.pitStopCount}</span>
                )}
            </button>
          );
        })}
      </div>
    </div>
  );
}
