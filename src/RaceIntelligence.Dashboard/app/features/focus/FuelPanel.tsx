import { useMemo } from 'react';
import { formatNumber } from '../../shared/format/format';
import type { LapSummary } from '../../shared/live/store';
import { LiveReadout } from '../../shared/ui/LiveReadout';
import { useLapSummaries } from '../../shared/live/useLive';
import type { SimPanelProps } from '../../sims/registry';

/**
 * How many laps the burn rate averages over.
 *
 * Five, for the reason the lap-time trend uses five: the question is what the car is drinking *now*,
 * and a longer window carries a first-stint fuel save into a last-stint push. Fuel is a steadier
 * channel than lap time, so this could be shorter — but one lap behind a safety car halves the
 * burn, and a rate that swung on a single such lap would have somebody boxing early.
 */
const BURN_WINDOW = 5;

/**
 * A lap whose fuel use can be believed.
 *
 * Two exclusions, and the second is the one that matters. A lap with no reading is obvious. A lap
 * where the tank went **up** is a refuelling stop, and its "use" is a large negative number that
 * would drag a five-lap mean below zero and report a car generating fuel. Filtering on a positive
 * burn is a cheaper and more certain test than trying to spot the stop itself, which the wire
 * describes only for the local car and only sometimes.
 *
 * A lap of exactly zero is excluded with them: fuel use can be switched off entirely in RaceRoom,
 * and a tank that never moves is not a car doing nought litres a lap, it is a session where the
 * question does not apply.
 */
function burnOf(lap: LapSummary): number | null {
  const used = lap.fuelUsedLiters;
  return used !== null && used !== undefined && used > 0 ? used : null;
}

/** What the tank is doing, as far as completed laps can say. */
export interface FuelModel {
  /** Mean litres per lap over the last {@link BURN_WINDOW} believable laps. */
  burnPerLap: number | null;
  /** How many laps that rate was averaged over, so the readout can say how sure it is. */
  lapsMeasured: number;
}

/**
 * The burn rate, from the laps that can speak to it.
 *
 * Exported because it is the whole model: everything else in this file is presentation, and when a
 * real fuel model arrives in `RaceIntelligence.Strategy` this is the function it replaces.
 */
export function modelFrom(laps: readonly LapSummary[]): FuelModel {
  const burns: number[] = [];

  for (const lap of laps) {
    const burn = burnOf(lap);
    if (burn !== null) {
      burns.push(burn);
    }
  }

  const window = burns.slice(-BURN_WINDOW);
  if (window.length === 0) {
    return { burnPerLap: null, lapsMeasured: 0 };
  }

  return {
    burnPerLap: window.reduce((total, burn) => total + burn, 0) / window.length,
    lapsMeasured: window.length,
  };
}

/**
 * What is in the tank, what it is going out at, and how far that gets the car.
 *
 * The dashboard has always shown litres remaining, and litres remaining answers none of the
 * questions actually asked on a pit wall. **"Forty litres" is not an answer; "eleven more laps" is**
 * — and the difference between them is one division nobody should be doing in their head at racing
 * speed.
 *
 * Three readings, in the order they are asked:
 *
 * - **In the tank**, live off the focus frame. The only one of the three that is an observation
 *   rather than a calculation, and it is drawn from a `LiveReadout` painting outside React.
 * - **Per lap**, a rolling mean over the last few completed laps. Not the last lap alone: one lap
 *   behind a safety car burns half of what a green one does, and a rate taken from it would be
 *   wrong in the direction that loses races.
 * - **Laps left**, which is the first two divided. Shown to one decimal because the fraction is the
 *   whole point — "3.1 laps" and "2.9 laps" are different decisions, and rounding both to three
 *   would hide the one that matters.
 *
 * ### The projection that is not here
 *
 * The handover asks for a projected margin at the finish, and it cannot be built today. That
 * calculation needs the race length, and **the race length does not cross the live wire**:
 * `numberOfLaps` and `sessionTimeDurationSeconds` are written by `WriteSessionExtras`, which feeds
 * the ingest path's session record, while the live extras frame carries `WriteSampleExtras` only.
 * `SessionStateMessage` carries the layout length and the pit window and no duration of any kind.
 *
 * So the margin is left out rather than guessed at. "Laps left in the tank" is the half of the
 * question this side of the wire can answer honestly, and it is the half a driver is radioed
 * anyway. Putting a finish margin here would mean inventing a race length, and a fuel number
 * invented from a guess is exactly the sort of confident wrong answer that gets somebody parked on
 * the last lap.
 *
 * ### It is a model, and it says so
 *
 * The rate is an assumption — that the next laps burn what the last few did — and the panel states
 * the assumption rather than presenting arithmetic as measurement. A driver lifting and coasting
 * from the next corner invalidates it immediately, which is fine, because it is labelled as what it
 * is. The arithmetic is deliberately small: when a real fuel model lands in
 * `RaceIntelligence.Strategy`, replacing this is deleting {@link modelFrom}, not unpicking a widget.
 */
export function FuelPanel({ store, driverKey }: SimPanelProps) {
  const laps = useLapSummaries(driverKey);
  const derived = useMemo(() => modelFrom(laps), [laps]);

  return (
    <div className="fuel">
      <div className="metric metric--large metric--wide">
        <LiveReadout
          store={store}
          driverKey={driverKey}
          className="metric__value"
          render={(frame) => formatNumber(frame.fuelLeftLiters, 1)}
        />
        <span className="metric__unit">L in tank</span>
      </div>

      <div className="metric">
        <span className="metric__value">
          {derived.burnPerLap === null ? '—' : formatNumber(derived.burnPerLap, 2)}
        </span>
        <span className="metric__unit">L per lap</span>
      </div>

      <div className="metric metric--large">
        {/*
          The one number here that combines both rates, so it is the one that has to be recomputed
          per frame rather than per lap — the tank drains continuously and a laps-left figure that
          only moved at the line would be stale for a minute and a half at a time.
        */}
        <LiveReadout
          store={store}
          driverKey={driverKey}
          className="metric__value"
          render={(frame) =>
            derived.burnPerLap === null || !Number.isFinite(frame.fuelLeftLiters)
              ? '—'
              : formatNumber(frame.fuelLeftLiters / derived.burnPerLap, 1)
          }
        />
        <span className="metric__unit">laps left</span>
      </div>

      <p className="fuel__basis">
        {derived.lapsMeasured === 0
          ? 'No completed lap has reported fuel use yet.'
          : `Assumes the next laps burn what the last ${derived.lapsMeasured} did. Race length is not on the live wire, so no finish margin is shown.`}
      </p>
    </div>
  );
}
