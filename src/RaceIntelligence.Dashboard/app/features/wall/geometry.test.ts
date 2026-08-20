import { beforeEach, describe, expect, it } from 'vitest';
import {
  registerSimPanels,
  WIDGET_UNIT,
  WIDGET_WIDE,
  type SimPanelDeclaration,
} from '../../sims/registry';
import type { WallWidget } from '../../shared/view/wallView';
import { fitToGrid, fitWidget, geometryAt, withGeometryAt } from './geometry';

const GAME = 'geometry-test';

/** Opens at the unit, so its derived floor is 2 x 3 — see `minSizeFor`. */
function panel(): SimPanelDeclaration {
  return {
    id: 'reading',
    title: 'Reading',
    scope: 'driver',
    requires: [],
    component: () => null,
    defaultSize: WIDGET_UNIT,
  };
}

/** Opens wide, so its floor is 4 x 3 — as many columns as the narrowest wall has in total. */
function widePanel(): SimPanelDeclaration {
  return {
    id: 'trace',
    title: 'Trace',
    scope: 'driver',
    requires: [],
    component: () => null,
    defaultSize: WIDGET_WIDE,
  };
}

function widget(overrides: Partial<WallWidget> = {}): WallWidget {
  return { instanceId: 'i-1', widgetId: 'reading', x: 0, y: 0, w: 4, h: 6, ...overrides };
}

describe('wall geometry', () => {
  beforeEach(() => {
    registerSimPanels(GAME, [panel(), widePanel()]);
  });

  describe('one arrangement per monitor', () => {
    it('reads the canonical fields at the width the wall is written in', () => {
      expect(geometryAt(widget({ x: 4, y: 2 }), 'lg')).toEqual({ x: 4, y: 2, w: 4, h: 6 });
    });

    it('falls back to the canonical fields at a width nobody has arranged', () => {
      expect(geometryAt(widget({ x: 4, y: 2 }), 'md')).toEqual({ x: 4, y: 2, w: 4, h: 6 });
    });

    it('reads that width once it has been arranged', () => {
      const placed = widget({ at: { md: { x: 1, y: 1, w: 2, h: 3 } } });

      expect(geometryAt(placed, 'md')).toEqual({ x: 1, y: 1, w: 2, h: 3 });
      expect(geometryAt(placed, 'lg')).toEqual({ x: 0, y: 0, w: 4, h: 6 });
    });

    /**
     * The defect this whole side table exists for. The grid rescales a layout for a narrower wall
     * and hands the rescaled numbers back through the same callback a drag uses — so writing them
     * to the canonical fields meant opening the wall on a laptop once permanently squashed the
     * arrangement the 4K screen was set up with, with no way back.
     */
    it('a placement made on a narrower monitor never touches the canonical arrangement', () => {
      const moved = withGeometryAt(widget({ x: 8 }), 'sm', { x: 0, y: 3, w: 4, h: 6 });

      expect(moved.x).toBe(8);
      expect(moved.at?.sm).toEqual({ x: 0, y: 3, w: 4, h: 6 });
    });

    it('a placement made at the canonical width is the canonical arrangement', () => {
      const moved = withGeometryAt(widget(), 'lg', { x: 8, y: 3, w: 4, h: 6 });

      expect(moved).toMatchObject({ x: 8, y: 3, w: 4, h: 6 });
      expect(moved.at).toBeUndefined();
    });

    it('keeps the placements made at other widths', () => {
      const moved = withGeometryAt(widget({ at: { sm: { x: 0, y: 0, w: 4, h: 6 } } }), 'md', {
        x: 4,
        y: 0,
        w: 4,
        h: 6,
      });

      expect(moved.at?.sm).toEqual({ x: 0, y: 0, w: 4, h: 6 });
      expect(moved.at?.md).toEqual({ x: 4, y: 0, w: 4, h: 6 });
    });
  });

  describe('a placement the grid can reach', () => {
    it('leaves a sound placement alone', () => {
      const geometry = { x: 4, y: 2, w: 4, h: 6 };

      expect(fitToGrid(widget(), GAME, 'lg', geometry)).toEqual(geometry);
    });

    it('narrows a widget wider than the wall', () => {
      expect(fitToGrid(widget(), GAME, 'lg', { x: 0, y: 0, w: 9999, h: 6 })).toMatchObject({
        x: 0,
        w: 12,
      });
    });

    /** Slid back against the edge rather than dumped in the first column, where everything else is. */
    it('slides a widget past the right edge back inside it', () => {
      expect(fitToGrid(widget(), GAME, 'lg', { x: 40, y: 0, w: 4, h: 6 })).toMatchObject({
        x: 8,
        w: 4,
      });
    });

    it('refuses a negative position', () => {
      expect(fitToGrid(widget(), GAME, 'lg', { x: -5, y: 0, w: 4, h: 6 })).toMatchObject({ x: 0 });
    });

    it('grows a widget below its own floor', () => {
      expect(fitToGrid(widget(), GAME, 'lg', { x: 0, y: 0, w: 1, h: 1 })).toMatchObject({
        w: 2,
        h: 3,
      });
    });

    /**
     * A four-column wall cannot honour a widget asking for more than four, and a floor it cannot
     * stand on would put the tile back outside the grid the line above just pulled it into.
     */
    it('never lets a floor exceed the columns the wall has', () => {
      const wide = widget({ widgetId: 'trace' });

      // Four columns wide is this widget's floor and the whole of a four-column wall. Anything
      // that let the floor win outright would push the tile straight back outside the grid the
      // clamp above just pulled it into.
      expect(fitToGrid(wide, GAME, 'sm', { x: 0, y: 0, w: 1, h: 6 })).toMatchObject({ x: 0, w: 4 });
      expect(fitToGrid(wide, GAME, 'sm', { x: 3, y: 0, w: 12, h: 6 })).toMatchObject({
        x: 0,
        w: 4,
      });
    });

    it('still places a widget the catalogue has never heard of, so it can be removed', () => {
      const stranger = widget({ widgetId: 'gone', w: 9999, x: -1 });

      expect(fitToGrid(stranger, GAME, 'lg', stranger)).toMatchObject({ x: 0, w: 12 });
    });

    it('fits every width a widget has been placed at', () => {
      const fitted = fitWidget(widget({ x: 99, at: { sm: { x: -3, y: 0, w: 99, h: 1 } } }), GAME);

      expect(fitted).toMatchObject({ x: 8, w: 4 });
      expect(fitted.at?.sm).toEqual({ x: 0, y: 0, w: 4, h: 3 });
    });

    /** A placement for a monitor this build no longer has a breakpoint for has nothing to fit to. */
    it('drops a placement for a width this build does not have', () => {
      const fitted = fitWidget(widget({ at: { enormous: { x: 0, y: 0, w: 4, h: 6 } } }), GAME);

      expect(fitted.at).toBeUndefined();
      expect('at' in fitted).toBe(false);
    });
  });
});
