import { NOT_REPORTED } from '../../shared/format/format';
import { reportedNumber } from '../../shared/live/extras';
import { useExtras } from '../../shared/live/useLive';
import type { SimPanelProps } from '../registry';

/**
 * The four damage channels RaceRoom reports, in the order a race engineer triages them.
 *
 * Engine and transmission first because they end a race; aerodynamics and suspension after,
 * because they cost lap time and can be driven around.
 */
const PARTS = [
  { key: 'engine', label: 'Engine' },
  { key: 'transmission', label: 'Gearbox' },
  { key: 'aerodynamics', label: 'Aero' },
  { key: 'suspension', label: 'Suspension' },
] as const;

/**
 * Turns one raw RaceRoom damage value into a condition fraction, or null.
 *
 * The sentinel judgement it used to make itself now lives in `reportedNumber`, which says the same
 * thing for every extras channel: `-1` is "not available", not "destroyed", and rendering it would
 * tell a race engineer the opposite of the truth twice over.
 *
 * What remains here is the part that is specific to damage. RaceRoom's scale runs the other way
 * from the word: 1.0 is a pristine component and 0.0 is a broken one. This keeps the simulator's
 * direction and calls it condition, rather than inverting into a "damage" number that would then
 * have to be inverted back to read a bar.
 */
export function toCondition(value: number | undefined): number | null {
  const reported = reportedNumber(value);

  return reported === null ? null : Math.min(1, reported);
}

/**
 * Car damage, from the low-rate extras channel.
 *
 * Updated at roughly 1 Hz and rendered through React on purpose: damage changes on contact, not
 * per frame, and giving it its own slow channel is precisely what keeps the 60 Hz path free of a
 * JSON parse.
 *
 * A meter is a glanceable state, not a measurement, so the bars are a fixed width rather than the
 * panel's. Four of them stretched across a full-width strip is a great deal of green to say "100%",
 * and no more legible for it — what has to be readable is the difference between fine, hurt, and
 * about to end the race.
 */
export function DamagePanel({ driverKey }: SimPanelProps) {
  const damage = useExtras(driverKey)?.document?.damage;

  return (
    <div className="damage">
      {PARTS.map((part) => {
        const condition = toCondition(damage?.[part.key]);

        return (
          <div key={part.key} className="damage__part">
            <span className="damage__label">{part.label}</span>

            <div
              className="damage__track"
              role="meter"
              aria-label={part.label}
              aria-valuemin={0}
              aria-valuemax={100}
              {...(condition === null ? {} : { 'aria-valuenow': Math.round(condition * 100) })}
            >
              {condition !== null && (
                <div
                  className={`damage__fill ${condition < 0.5 ? 'damage__fill--critical' : ''}`}
                  style={{ width: `${condition * 100}%` }}
                />
              )}
            </div>

            <span className="damage__value">
              {condition === null ? NOT_REPORTED : `${Math.round(condition * 100)}%`}
            </span>
          </div>
        );
      })}
    </div>
  );
}
