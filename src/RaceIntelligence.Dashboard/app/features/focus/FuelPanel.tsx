import { useMemo } from 'react';
import { formatNumber } from '../../shared/format/format';
import type { RaceLengthState } from '../../shared/live/contracts';
import type { LapSummary } from '../../shared/live/store';
import { LiveReadout } from '../../shared/ui/LiveReadout';
import { useLapSummaries, useSessionState, useTower } from '../../shared/live/useLive';
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
 * How many laps are left to run, when the session is one where that is a knowable number.
 *
 * **Only a lap race has one.** A timed race ends on the clock, and the number of laps it takes to
 * get there depends on how fast everyone drives the rest of it — and the wire carries the session's
 * total duration but not how much of it has elapsed, so even the estimate is out of reach. Returning
 * null and letting the panel say so is the whole of the honesty here: the alternative is dividing a
 * duration by a lap time and presenting the result as a lap count.
 *
 * `unit` is what decides, never which figure happens to be present. RaceRoom will report a lap count
 * in a timed session, and a consumer that read it would count down to a flag that is not there.
 */
export function lapsRemainingIn(
  raceLength: RaceLengthState | null | undefined,
  completedLaps: number | null | undefined,
): number | null {
  if (raceLength?.unit !== 'Laps') {
    return null;
  }

  const total = raceLength.laps;
  if (total === null || total === undefined || !Number.isFinite(total)) {
    return null;
  }

  // A driver the tower has not placed yet has completed nothing, which is the right assumption for
  // the only moment it applies: before the lights.
  const done = completedLaps ?? 0;

  // Never negative. The leader crosses the line and keeps being reported for a moment afterwards,
  // and a margin computed against minus one lap remaining would read as an enormous surplus at
  // exactly the moment the number stops mattering.
  return Math.max(0, total - done);
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
 * ### The margin, and where it stops
 *
 * A fourth reading when the session is a lap race: **laps in the tank minus laps left to run**.
 * Positive is fuel to spare, negative is a stop or a lift-and-coast, and zero is the number nobody
 * wants to see. This is the reading a strategist actually acts on, and it only became possible when
 * the race length reached the wire — before that the panel showed the first three and said why the
 * fourth was missing.
 *
 * It still says so in a timed race, because there the lap count genuinely is not known: the wire
 * carries the session's total duration but not how much of it has run, so the laps remaining cannot
 * even be estimated, let alone measured. Saying "not in a timed race" is the honest answer; dividing
 * a duration by a lap time and printing the result would be a guess wearing a decimal point.
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
  const sessionState = useSessionState();
  const tower = useTower();
  const derived = useMemo(() => modelFrom(laps), [laps]);

  // From the tower rather than the focus frame's lap number, because they answer different
  // questions: the frame says which lap the car is on, the tower says how many it has finished, and
  // counting a lap in progress as run would report the margin one lap better than it is.
  const lapsRemaining = useMemo(
    () =>
      lapsRemainingIn(
        sessionState?.raceLength,
        tower?.drivers.find((row) => row.driverKey === driverKey)?.completedLaps,
      ),
    [sessionState, tower, driverKey],
  );

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

      {lapsRemaining !== null && (
        <div className="metric metric--large">
          {/*
            Per frame for the same reason laps-left is: it is laps-left minus a constant, so it
            drains just as continuously. Signed deliberately — "+1.4" and "−1.4" are the two
            opposite decisions this panel exists to tell apart, and an unsigned number would need
            reading twice.
          */}
          <LiveReadout
            store={store}
            driverKey={driverKey}
            className="metric__value"
            render={(frame) => {
              if (derived.burnPerLap === null || !Number.isFinite(frame.fuelLeftLiters)) {
                return '—';
              }

              const margin = frame.fuelLeftLiters / derived.burnPerLap - lapsRemaining;
              return `${margin >= 0 ? '+' : ''}${formatNumber(margin, 1)}`;
            }}
          />
          <span className="metric__unit">laps margin</span>
        </div>
      )}

      <p className="fuel__basis">
        {derived.lapsMeasured === 0
          ? 'No completed lap has reported fuel use yet.'
          : `Assumes the next laps burn what the last ${derived.lapsMeasured} did.${
              lapsRemaining === null
                ? ' No margin: the laps left to run are only known in a lap race.'
                : ` ${lapsRemaining} ${lapsRemaining === 1 ? 'lap' : 'laps'} left to run.`
            }`}
      </p>
    </div>
  );
}
