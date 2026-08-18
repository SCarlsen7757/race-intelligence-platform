import { useMemo } from 'react';
import { NOT_REPORTED } from '../../shared/format/format';
import { useExtras } from '../../shared/live/useLive';
import type { SimPanelProps } from '../registry';
import { parseExtras } from './extras';

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
 * **`-1` is "not available", not "destroyed".** Extras cross the wire exactly as the connector
 * wrote them, so nothing upstream has translated the simulator's sentinel — and rendering it as a
 * number would tell a race engineer the opposite of the truth twice over: once by claiming there
 * is a reading, and once by claiming the worst possible one.
 *
 * RaceRoom's scale runs the other way from the word "damage": 1.0 is a pristine component and 0.0
 * is a broken one. This keeps the simulator's direction and calls it condition, rather than
 * inverting into a "damage" number that would then have to be inverted back to read a bar.
 */
export function toCondition(value: number | undefined): number | null {
  if (value === undefined || Number.isNaN(value) || value < 0) {
    return null;
  }

  return Math.min(1, value);
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
  const extras = useExtras(driverKey);
  const damage = useMemo(() => parseExtras(extras?.extras ?? null)?.damage, [extras]);

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
