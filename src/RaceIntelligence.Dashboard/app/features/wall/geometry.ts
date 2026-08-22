import type { Layout } from 'react-grid-layout';
import { findPanel } from '../../sims/registry';
import type { WallWidget, WidgetGeometry } from '../../shared/view/wallView';
import { CANONICAL_BREAKPOINT, COLUMNS, type WallBreakpoint } from './breakpoints';

/**
 * Where one widget sits at one breakpoint.
 *
 * The canonical fields are the arrangement; `at` holds the placements made on other monitors. A
 * breakpoint nobody has arranged falls back to the canonical numbers, which the grid then corrects
 * into its own column count — and the correction is written to `at` on the next layout callback, so
 * a width becomes explicit the first time it is used and stays that way.
 */
export function geometryAt(widget: WallWidget, breakpoint: WallBreakpoint): WidgetGeometry {
  if (breakpoint === CANONICAL_BREAKPOINT) {
    return { x: widget.x, y: widget.y, w: widget.w, h: widget.h };
  }

  return widget.at?.[breakpoint] ?? { x: widget.x, y: widget.y, w: widget.w, h: widget.h };
}

/**
 * Writes a placement back, to the canonical fields or to the right breakpoint's side table.
 *
 * The whole point of the split: a drag performed on a laptop must not be able to touch the numbers
 * the 4K wall is written in. See `WallWidget.at`.
 */
export function withGeometryAt(
  widget: WallWidget,
  breakpoint: WallBreakpoint,
  geometry: WidgetGeometry,
): WallWidget {
  if (breakpoint === CANONICAL_BREAKPOINT) {
    return { ...widget, ...geometry };
  }

  return { ...widget, at: { ...widget.at, [breakpoint]: geometry } };
}

/**
 * One placement, forced inside the grid and above the widget's own floor.
 *
 * `normaliseWidget` has already made the four numbers whole and non-negative; it deliberately knows
 * nothing about columns or catalogue entries, because it lives in `shared/`. This is the half that
 * needs both, and it is what stops a hand-edited `w: 9999` or `x: -5` from placing a tile somewhere
 * with no Remove button the user can reach.
 *
 * A widget the catalogue has never heard of gets the grid's bounds and no floor — there is nobody
 * left to ask what its minimum is, and it still has to be draggable so it can be removed.
 */
export function fitToGrid(
  widget: WallWidget,
  gameKey: string,
  breakpoint: WallBreakpoint,
  geometry: WidgetGeometry,
): WidgetGeometry {
  const columns = COLUMNS[breakpoint];
  const entry = findPanel(gameKey, widget.widgetId);

  // Never wider than the wall, and never below what the widget says it needs — in that order,
  // because a `minSize` wider than the breakpoint's whole grid would otherwise win and put the tile
  // back outside it. On a four-column wall a widget asking for six gets four.
  const floorW = Math.min(entry?.minSize.w ?? 1, columns);
  const w = Math.min(columns, Math.max(floorW, geometry.w));
  const h = Math.max(entry?.minSize.h ?? 1, geometry.h);

  return {
    // Slid back inside rather than clamped to zero, so a tile that was off the right edge lands
    // against it instead of piling up in the first column with everything else that was out there.
    x: Math.max(0, Math.min(geometry.x, columns - w)),
    y: geometry.y,
    w,
    h,
  };
}

/**
 * The grid's own view of one breakpoint's arrangement, with each widget's floor attached.
 *
 * `minW`/`minH` come off the catalogue entry, so the size below which a widget stops being worth
 * reading is set by the widget and enforced by the grid — the drag simply stops. Capped at the
 * breakpoint's column count, because a widget asking for six columns on a four-column wall would
 * otherwise be given a floor it cannot stand on. A widget whose entry has gone missing gets no
 * floor, because there is nobody left to ask what its minimum is — and it still has to be movable,
 * so it can be removed.
 *
 * Not memoised: every caller either wants a fresh, disposable array to mutate (the keyboard
 * arrange path) or is itself inside a `useMemo` that has already keyed on `widgets`.
 */
export function layoutAt(
  widgets: WallWidget[],
  gameKey: string,
  breakpoint: WallBreakpoint,
): Layout {
  return widgets.map((widget) => {
    const entry = findPanel(gameKey, widget.widgetId);
    const geometry = fitToGrid(widget, gameKey, breakpoint, geometryAt(widget, breakpoint));

    return {
      i: widget.instanceId,
      ...geometry,
      ...(entry === null
        ? {}
        : {
            minW: Math.min(entry.minSize.w, COLUMNS[breakpoint]),
            minH: entry.minSize.h,
          }),
    };
  });
}

/** Every placement of one widget, put inside the grid at whichever widths it has one for. */
export function fitWidget(widget: WallWidget, gameKey: string): WallWidget {
  const fitted: WallWidget = {
    ...widget,
    ...fitToGrid(widget, gameKey, CANONICAL_BREAKPOINT, geometryAt(widget, CANONICAL_BREAKPOINT)),
  };

  if (widget.at === undefined) {
    return fitted;
  }

  const at = Object.entries(widget.at)
    // A breakpoint this build no longer has is a placement for a monitor nobody is using. Dropped
    // rather than kept, because there is no column count to fit it to and it would ride along in
    // every export forever.
    .filter(([name]) => name in COLUMNS)
    .map(
      ([name, geometry]) =>
        [name, fitToGrid(widget, gameKey, name as WallBreakpoint, geometry)] as const,
    );

  if (at.length === 0) {
    // Deleted rather than set to undefined: `exactOptionalPropertyTypes` tells an absent property
    // from one present and undefined, and only the first survives a round trip through JSON.
    const { at: dropped, ...rest } = fitted;
    void dropped;
    return rest;
  }

  return { ...fitted, at: Object.fromEntries(at) };
}
