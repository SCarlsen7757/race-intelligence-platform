import { beforeEach, describe, expect, it } from 'vitest';
import {
  isWallWidget,
  loadWallView,
  normaliseWidget,
  wallViewKey,
  WALL_VIEW_VERSION,
  type WallWidget,
} from './wallView';

const GAME = 'wall-view-test';

function widget(overrides: Record<string, unknown> = {}): WallWidget {
  // Deliberately loose: half of what this file checks is what happens to values the type says
  // cannot be there — a fractional cell, a negative position, a placement that is not one.
  return {
    instanceId: 'i-1',
    widgetId: 'reading',
    x: 0,
    y: 0,
    w: 4,
    h: 6,
    ...overrides,
  };
}

function save(widgets: unknown[]): void {
  window.localStorage.setItem(
    wallViewKey(GAME),
    JSON.stringify({ version: WALL_VIEW_VERSION, widgets }),
  );
}

describe('wallView', () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  describe('what counts as a widget', () => {
    it('accepts a widget with no placements for other widths', () => {
      expect(isWallWidget(widget())).toBe(true);
    });

    it('accepts placements for other widths', () => {
      expect(isWallWidget(widget({ at: { md: { x: 1, y: 1, w: 2, h: 3 } } }))).toBe(true);
    });

    it('refuses a placement that is not one', () => {
      expect(isWallWidget(widget({ at: { md: { x: 1, y: 1 } } }))).toBe(false);
      expect(isWallWidget(widget({ at: 'somewhere' }))).toBe(false);
    });
  });

  /**
   * The file is meant to be hand-edited and shared, so malformed geometry is a normal input rather
   * than an attack — and it should cost a moved tile, not a wall the user cannot repair.
   */
  describe('geometry that is not a position', () => {
    it('rounds a fractional cell', () => {
      expect(normaliseWidget(widget({ x: 1.4, y: 2.6, w: 3.5, h: 6.2 }))).toMatchObject({
        x: 1,
        y: 3,
        w: 4,
        h: 6,
      });
    });

    it('refuses a negative position', () => {
      expect(normaliseWidget(widget({ x: -5, y: -1 }))).toMatchObject({ x: 0, y: 0 });
    });

    /** `typeof NaN === 'number'`, which is exactly why the shape check alone was never enough. */
    it('replaces a non-number that passed the shape check', () => {
      expect(normaliseWidget(widget({ x: Number.NaN, w: Number.POSITIVE_INFINITY }))).toMatchObject(
        { x: 0, w: 1 },
      );
    });

    it('never leaves a widget with no width or height to grab', () => {
      expect(normaliseWidget(widget({ w: 0, h: 0 }))).toMatchObject({ w: 1, h: 1 });
    });

    it('cleans the placements made at other widths too', () => {
      const cleaned = normaliseWidget(widget({ at: { md: { x: -2, y: 0.5, w: 0, h: 3 } } }));

      expect(cleaned.at?.md).toEqual({ x: 0, y: 1, w: 1, h: 3 });
    });

    /**
     * `NaN` is deliberately not exercised here: `JSON.stringify` writes it as `null`, which fails
     * the shape check and takes the whole wall with it — so it cannot reach this path from a file
     * or from storage, only from memory, which the case above covers.
     */
    it('cleans a wall on the way out of storage', () => {
      save([widget({ x: -3, w: 0.4, h: 6.5 })]);

      expect(loadWallView(GAME)?.widgets[0]).toMatchObject({ x: 0, w: 1, h: 7 });
    });
  });
});
