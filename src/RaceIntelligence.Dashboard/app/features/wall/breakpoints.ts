import { WIDGET_GRID_COLUMNS } from '../../sims/registry';

/**
 * Column counts by container width.
 *
 * Four steps, chosen for the monitor the wall is on rather than for a device class: a 1080p right
 * region, a 1440p one, a 4K one, and an ultrawide. `lg` is twelve because that is
 * {@link WIDGET_GRID_COLUMNS}, the vocabulary every catalogue entry's `defaultSize` is written in —
 * so a widget added at that width opens at exactly the size it declared, and the other breakpoints
 * are that layout rescaled.
 *
 * Fewer columns on a narrower wall rather than more, which reads backwards until you hold the
 * widget's width fixed: three four-column charts sit side by side at `lg` and the same three stack
 * two-up at `md`, each keeping enough pixels to still be a chart. More columns would shrink them
 * instead, which is how a stint trace becomes a smear. The consequence is that one column is
 * roughly the same number of pixels on every monitor, which is the property that makes a declared
 * `defaultSize` mean something.
 *
 * Measured against the grid's own container, not the viewport, because the timing column takes its
 * share first and the wall only ever gets what is left.
 */
export const BREAKPOINTS = { xl: 2000, lg: 1400, md: 900, sm: 0 } as const;

export const COLUMNS = { xl: 16, lg: WIDGET_GRID_COLUMNS, md: 8, sm: 4 } as const;

export type WallBreakpoint = keyof typeof BREAKPOINTS;

/**
 * The width a saved arrangement is written in.
 *
 * A wall has one arrangement the user thinks of as *theirs*, and it has to be expressed in some
 * fixed number of columns or it is not one arrangement. `lg` is that number for the same reason it
 * is twelve columns: it is the vocabulary the catalogue already speaks. Every other breakpoint is
 * a placement of the same widgets for a different monitor, stored separately — see `WallWidget.at`.
 */
export const CANONICAL_BREAKPOINT: WallBreakpoint = 'lg';

/** Widest first, which is the order {@link breakpointFor} needs. */
export const WALL_BREAKPOINTS: readonly WallBreakpoint[] = (
  Object.keys(BREAKPOINTS) as WallBreakpoint[]
).sort((a, b) => BREAKPOINTS[b] - BREAKPOINTS[a]);

/**
 * Which breakpoint a container width falls in.
 *
 * Derived from the width the wall already measures rather than tracked through the grid's
 * `onBreakpointChange`, and that is deliberate: a layout callback and a breakpoint callback firing
 * in an order nobody controls is exactly how geometry gets written against the wrong column count,
 * which is the failure this whole arrangement exists to prevent. From the width it is a pure
 * function and cannot be stale.
 */
export function breakpointFor(width: number): WallBreakpoint {
  return WALL_BREAKPOINTS.find((name) => width >= BREAKPOINTS[name]) ?? CANONICAL_BREAKPOINT;
}
