import { describe, expect, it } from 'vitest';
import {
  isDriverWidget,
  panelsFor,
  minSizeFor,
  registerSimPanels,
  WIDGET_GRID_COLUMNS,
  WIDGET_SIZES,
  type SimPanelDeclaration,
} from './registry';

const panel = (id: string, requires: string[]): SimPanelDeclaration => ({
  id,
  title: id,
  scope: 'driver',
  requires,
  component: () => null,
  defaultSize: { w: 4, h: 6 },
});

const roomPanel = (id: string): SimPanelDeclaration => ({
  id,
  title: id,
  scope: 'room',
  requires: [],
  component: () => null,
  defaultSize: { w: 6, h: 8 },
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
      'fuel',
      'systems',
      'events',
      'race-timeline',
      'tyre-pressure',
      'tyre-wear',
      'tyre-temperature',
      'tyre-tread',
      'tyre-grip',
      'brake-temperature',
      'brake-pressure',
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
      'fuel',
      'systems',
      'events',
      'race-timeline',
      'tyre-wear',
      'tyre-grip',
      'brake-pressure',
    ]);
  });

  /**
   * Several widgets need no capability at all, and that is deliberate rather than an oversight:
   * what the car is doing, what the driver is doing, what the car is set to, and the trace of the
   * last thirty seconds are built from channels every simulator reports. Gating them on a flag
   * would mean remembering to set that flag for every new simulator, to describe data none of them
   * can fail to send. The first four are also exactly the default wall — see `raceroom`.
   *
   * Tyre grip is here for a different reason and it is worth keeping the distinction visible: it
   * has no `SimCapabilities` flag because it rides in the extras document rather than the typed
   * wire, and RaceRoom's mapper writes it unconditionally. The registry being keyed by game key is
   * what gates it — borrowing `TyreWear` would be asserting a relationship between two channels
   * that do not have one.
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
      'fuel',
      'systems',
      'events',
      'race-timeline',
      'tyre-grip',
      'brake-pressure',
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
   * The vocabulary, enforced.
   *
   * Nineteen entries used to nominate their own numbers and between them used five widths and six
   * heights, which is why the wall never packed. A rule nothing checks is how the twentieth widget
   * invents a twentieth size, so this is the check — and it is deliberately about *every registered
   * widget* rather than a list kept in step by hand.
   */
  it('opens every RaceRoom widget at one of the sizes the catalogue has', async () => {
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
      expect(WIDGET_SIZES).toContainEqual(widget.defaultSize);
    }
  });

  /**
   * The minimum is the rule's, not the widget's. A catalogue entry cannot state one at all — the
   * type has no field for it — and this is what proves registration fills it in rather than
   * leaving it to whatever the declaration happened to carry.
   */
  it('derives every minimum from the size the widget opens at', async () => {
    await import('./raceroom');

    const widgets = panelsFor('raceroom', ['TyreWear', 'Damage']);

    expect(widgets).not.toHaveLength(0);

    for (const widget of widgets) {
      expect(widget.minSize).toEqual(minSizeFor(widget.defaultSize));
    }
  });

  it('fills in a minimum for a simulator that never mentioned one', () => {
    registerSimPanels('testsim', [panel('wear', ['TyreWear'])]);

    expect(panelsFor('testsim', ['TyreWear'])[0]?.minSize).toEqual({ w: 2, h: 3 });
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
