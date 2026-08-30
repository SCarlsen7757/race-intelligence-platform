import type { OperatingWindowValues } from './LiveChart';

/**
 * The shape a window arrives in, on a tyre or a brake.
 *
 * Structurally {@link OperatingWindow} from the live contracts, kept local so this helper does not
 * depend on the wire: it is equally happy with a window a read-path response built.
 */
interface RawWindow {
  optimal?: number | null;
  cold?: number | null;
  hot?: number | null;
}

/**
 * One window for a chart that draws four corners.
 *
 * **A window belongs to the compound or the pad, not to a corner.** All four tyres on a car are the
 * same rubber and all four brakes the same material, so the simulator reports the same three numbers
 * four times — and a chart drawing four identical bands would be drawing one band four times as
 * opaque, over a plot whose whole point is the space between the lines.
 *
 * So: the first corner that reports one wins. Taking the first *reported* rather than simply the
 * front left matters, because the corners a simulator declines to answer for are not always the ones
 * you would guess — a car with no reading at one axle still has a window worth drawing.
 *
 * Returns null when nothing reported anything, which is what stops a band being drawn at all rather
 * than being drawn from nothing.
 */
export function firstReportedWindow(
  corners: readonly (RawWindow | null | undefined)[] | null | undefined,
): OperatingWindowValues | null {
  if (corners === null || corners === undefined) {
    return null;
  }

  for (const corner of corners) {
    if (corner === null || corner === undefined) {
      continue;
    }

    // No sentinel filtering. These arrive already translated — the connector turned RaceRoom's
    // -1 into a null before the sample left the collector — so a negative here is a real bound, and
    // a brake window genuinely can sit below zero on a cold morning.
    const cold = corner.cold ?? null;
    const hot = corner.hot ?? null;
    const optimal = corner.optimal ?? null;

    // Any one of the three is enough to be worth drawing — `drawBand` renders the bounds and the
    // optimum independently, so a simulator reporting only an optimum still gets its line.
    if (cold !== null || hot !== null || optimal !== null) {
      return { cold, hot, optimal };
    }
  }

  return null;
}
