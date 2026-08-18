import { useMemo } from 'react';
import { useExtras } from '../../shared/live/useLive';
import type { SimPanelProps } from '../registry';
import { parseExtras } from './extras';

/**
 * The share of the limit at which the readout starts asking to be looked at.
 *
 * Three quarters, because the interesting moment is the one where the remaining budget stops being
 * enough to absorb an ordinary racing incident — not the moment the limit is reached, by which time
 * the warning has nothing left to warn about.
 */
const WARNING_FRACTION = 0.75;

/**
 * Reads the driver's incident count out of the extras document.
 *
 * **`-1` is "not available", not "no incidents".** Extras cross the wire exactly as the connector
 * wrote them, so nothing upstream has translated the simulator's sentinel, and a panel rendering it
 * would report a number the simulator never gave. Zero is a different thing entirely: it is a real
 * answer, and a clean sheet is worth showing.
 */
export function toIncidentCount(value: number | undefined): number | null {
  if (value === undefined || Number.isNaN(value) || value < 0) {
    return null;
  }

  return value;
}

/**
 * Reads the server's incident limit, which most sessions do not have.
 *
 * `-1` is the simulator's "not available" — offline, or a server that disqualifies nobody. Zero is
 * rejected as well, and that is the one place this differs from the count: a limit of zero would
 * render as `4 / 0` and divide into a meaningless ratio, so there is nothing it can usefully mean.
 */
export function toIncidentLimit(value: number | undefined): number | null {
  if (value === undefined || Number.isNaN(value) || value <= 0) {
    return null;
  }

  return value;
}

/**
 * Incident points, from the low-rate extras channel.
 *
 * A `Self`-tier readout by necessity: RaceRoom reports incident points at the root of the shared
 * block, for the car the simulator is running and for no other, so this belongs beside fuel, tyre
 * wear and damage rather than in a timing tower.
 *
 * Three states, and the difference between them is the whole point of the panel:
 *
 * - Count and limit both reported: `4 / 10`, the number that decides whether the race is finishable.
 * - Count only: `4`. Never `4 / -1`, and never `4 / 0` — an absent limit is not a limit of nothing.
 * - No count: nothing at all. There is no honest thing to draw for a number the simulator withheld,
 *   and `0` would be the specific lie that the driver has a clean sheet.
 */
export function IncidentsPanel({ driverKey }: SimPanelProps) {
  const extras = useExtras(driverKey);
  const parsed = useMemo(() => parseExtras(extras?.extras ?? null), [extras]);

  const points = toIncidentCount(parsed?.incidentPoints);

  if (points === null) {
    return null;
  }

  const limit = toIncidentLimit(parsed?.maxIncidentPoints);

  // Only where a limit is actually reported: without one there is nothing to be close to, and any
  // count would be as alarming as any other.
  const nearLimit = limit !== null && points / limit >= WARNING_FRACTION;

  return (
    <p className={`incidents ${nearLimit ? 'incidents--critical' : ''}`}>
      <span className="incidents__count">{points}</span>
      {limit !== null && <span className="incidents__limit">/ {limit}</span>}
    </p>
  );
}
