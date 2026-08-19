import { describe, expect, it } from 'vitest';
import {
  isDriverWidget,
  panelsFor,
  registerSimPanels,
  WIDGET_GRID_COLUMNS,
  type SimPanel,
} from './registry';

const panel = (id: string, requires: string[]): SimPanel => ({
  id,
  title: id,
  scope: 'driver',
  requires,
  component: () => null,
  defaultSize: { w: 4, h: 6 },
  minSize: { w: 3, h: 4 },
});

const roomPanel = (id: string): SimPanel => ({
  id,
  title: id,
  scope: 'room',
  requires: [],
  component: () => null,
  defaultSize: { w: 6, h: 8 },
  minSize: { w: 4, h: 6 },
});

describe('sim panel registry', () => {
  it('shows a panel whose capabilities the collector reports', () => {
    registerSimPanels('testsim', [panel('wear', ['TyreWear'])]);

    expect(panelsFor('testsim', ['TyreWear']).map((p) => p.id)).toEqual(['wear']);
  });

  /**
   * The platform's rule for staying simulator-agnostic, enforced at the point it matters: a
   * simulator that cannot report a channel simply has no panel for it, rather than a panel showing
   * dashes or zeroes.
   */
  it('hides a panel the collector cannot feed', () => {
    registerSimPanels('testsim', [panel('wear', ['TyreWear'])]);

    expect(panelsFor('testsim', ['TyrePressure'])).toEqual([]);
  });

  it('requires every declared capability, not merely one of them', () => {
    registerSimPanels('testsim', [panel('combined', ['TyreWear', 'TyrePressure'])]);

    expect(panelsFor('testsim', ['TyreWear'])).toEqual([]);
    expect(panelsFor('testsim', ['TyreWear', 'TyrePressure'])).toHaveLength(1);
  });

  it('returns nothing for a simulator with no panels registered', () => {
    expect(panelsFor('unregistered-sim', ['TyreWear'])).toEqual([]);
  });

  /**
   * The RaceRoom panels are registered as a side effect of importing the module, so this is what
   * catches a capability name that no longer matches the C# enum — a typo there would silently
   * hide the panel forever.
   */
  it('registers the RaceRoom panels against real capability names', async () => {
    await import('./raceroom');

    const ids = panelsFor('raceroom', [
      'TyreWear',
      'TyrePressure',
      'TyreTemperature',
      'BrakeTemperature',
      'Damage',
    ]).map((p) => p.id);

    expect(ids).toEqual([
      'car-metrics',
      'pedals',
      'assists',
      'inputs-trace',
      'lap-delta',
      'lap-trend',
      'tyre-pressure',
      'tyre-wear',
      'tyre-temperature',
      'brake-temperature',
      'damage',
    ]);
  });

  /**
   * Damage is the sharpest case for capability gating: the collector advertised it long before
   * anything delivered it, so a panel chosen by game key would have shown four dashes for months.
   */
  it('hides the damage panel for a collector that cannot produce damage', async () => {
    await import('./raceroom');

    const ids = panelsFor('raceroom', ['TyreWear']).map((p) => p.id);

    expect(ids).toEqual([
      'car-metrics',
      'pedals',
      'assists',
      'inputs-trace',
      'lap-delta',
      'lap-trend',
      'tyre-wear',
    ]);
  });

  /**
   * Four widgets need no capability at all, and that is deliberate rather than an oversight: what
   * the car is doing, what the driver is doing, what the car is set to, and the trace of the last
   * thirty seconds are built from channels every simulator reports. Gating them on a flag would
   * mean remembering to set that flag for every new simulator, to describe data none of them can
   * fail to send. They are also exactly the default wall, for the same reason — see `raceroom`.
   */
  it('offers the core widgets to a collector that reports nothing at all', async () => {
    await import('./raceroom');

    expect(panelsFor('raceroom', []).map((p) => p.id)).toEqual([
      'car-metrics',
      'pedals',
      'assists',
      'inputs-trace',
      'lap-delta',
      'lap-trend',
    ]);
  });

  it('carries a scope, a default size and a minimum size on every RaceRoom widget', async () => {
    await import('./raceroom');

    const widgets = panelsFor('raceroom', [
      'TyreWear',
      'TyrePressure',
      'TyreTemperature',
      'BrakeTemperature',
      'BrakeWear',
      'Damage',
      'IncidentPoints',
    ]);

    expect(widgets).not.toHaveLength(0);

    for (const widget of widgets) {
      expect(widget.scope).toBe('driver');
      expect(widget.defaultSize.w).toBeGreaterThan(0);
      expect(widget.defaultSize.h).toBeGreaterThan(0);
      expect(widget.minSize.w).toBeGreaterThan(0);
      expect(widget.minSize.h).toBeGreaterThan(0);
    }
  });

  /**
   * A widget that opened smaller than it may be dragged, or wider than the wall, would be a
   * layout that cannot be satisfied — so the two sizes have to agree with each other and with the
   * grid before the grid ever sees them.
   */
  it('never asks to open smaller than its own minimum, or wider than the grid', async () => {
    await import('./raceroom');

    const widgets = panelsFor('raceroom', [
      'TyreWear',
      'TyrePressure',
      'TyreTemperature',
      'BrakeTemperature',
      'BrakeWear',
      'Damage',
      'IncidentPoints',
    ]);

    for (const widget of widgets) {
      expect(widget.defaultSize.w).toBeGreaterThanOrEqual(widget.minSize.w);
      expect(widget.defaultSize.h).toBeGreaterThanOrEqual(widget.minSize.h);
      expect(widget.defaultSize.w).toBeLessThanOrEqual(WIDGET_GRID_COLUMNS);
    }
  });

  /**
   * The union's whole value: a driver column cannot be handed a widget that has no car to be about.
   */
  it('narrows a mixed catalogue to the widgets a driver column can mount', () => {
    registerSimPanels('testsim', [panel('wear', ['TyreWear']), roomPanel('tower')]);

    const ids = panelsFor('testsim', ['TyreWear'])
      .filter(isDriverWidget)
      .map((p) => p.id);

    expect(ids).toEqual(['wear']);
  });
});
