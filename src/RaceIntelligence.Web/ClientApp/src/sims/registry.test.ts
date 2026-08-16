import { describe, expect, it } from 'vitest';
import { panelsFor, registerSimPanels, type SimPanel } from './registry';

const panel = (id: string, requires: string[]): SimPanel => ({
  id,
  title: id,
  requires,
  component: () => null,
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
  it('registers the RaceRoom tyre panels against real capability names', async () => {
    await import('./raceroom');

    const ids = panelsFor('raceroom', [
      'TyreWear',
      'TyrePressure',
      'TyreTemperature',
    ]).map((p) => p.id);

    expect(ids).toEqual(['tyre-pressure', 'tyre-wear', 'tyre-temperature']);
  });
});
