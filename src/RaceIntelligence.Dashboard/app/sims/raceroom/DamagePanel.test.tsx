import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { LiveConnection } from '../../shared/live/connection';
import type { RaceRoomSample } from '../../shared/live/contracts';
import { LiveStore } from '../../shared/live/store';
import { LiveContext } from '../../shared/live/useLive';
import { slowFrame } from '../../testing/slowFrame';
import { DamagePanel, toCondition } from './DamagePanel';

const DRIVER = 'id:2';

/**
 * Mounts the panel over a store holding one slow frame.
 *
 * The store is fed directly rather than through a socket: the panel reads the low-rate slow channel
 * and nothing else, so a real connection would only add a network to the test.
 */
function renderDamage(sample: RaceRoomSample) {
  const store = new LiveStore();
  store.setFollowedDrivers([DRIVER]);
  store.apply(slowFrame(DRIVER, sample));

  return render(
    <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
      <DamagePanel store={store} driverKey={DRIVER} />
    </LiveContext.Provider>,
  );
}

describe('toCondition', () => {
  /**
   * A channel the simulator did not report is absent, not zero. It used to arrive as `-1` and this
   * function was where that was caught; the connector translates it now, so what is left is the
   * absence — and reading absence as total damage would tell a race engineer the opposite of the
   * truth twice over: that there is a reading, and that it is the worst possible one.
   */
  it('reads an unreported channel as no reading rather than as total damage', () => {
    expect(toCondition(undefined)).toBeNull();
  });

  /** 1.0 is pristine and 0.0 is broken, keeping the simulator's direction rather than inverting. */
  it('keeps the simulator direction, clamped at pristine', () => {
    expect(toCondition(1)).toBe(1);
    expect(toCondition(0)).toBe(0);
    expect(toCondition(0.5)).toBe(0.5);
    expect(toCondition(1.4)).toBe(1);
  });
});

describe('DamagePanel', () => {
  it('reports a known condition on the meter', () => {
    renderDamage({ damageEngine: 0.75 });

    expect(screen.getByRole('meter', { name: 'Engine' }).getAttribute('aria-valuenow')).toBe('75');
    expect(screen.getByText('75%')).toBeDefined();
  });

  /**
   * A screen reader announcing "0%" for a channel the simulator never reported would be exactly the
   * lie the em dash avoids visually, so the attribute is omitted rather than defaulted.
   */
  it('omits aria-valuenow for an unreported channel rather than announcing zero', () => {
    renderDamage({ damageEngine: 1 });

    const gearbox = screen.getByRole('meter', { name: 'Gearbox' });

    expect(gearbox.hasAttribute('aria-valuenow')).toBe(false);
    expect(gearbox.querySelector('.damage__fill')).toBeNull();
  });

  it('marks a component below half condition as critical', () => {
    const { container } = renderDamage({ damageEngine: 0.4, damageTransmission: 0.9 });

    expect(container.querySelectorAll('.damage__fill--critical')).toHaveLength(1);
  });

  /**
   * A car reporting no damage channels at all still renders four meters, all of them silent.
   *
   * This used to be the "malformed document" case — the payload was a JSON string, so a panel could
   * be handed text that would not parse. There is no parse left to fail; what remains is the case
   * that mattered anyway, which is a simulator that reports nothing.
   */
  it('renders every channel as unreported when the sample carries no damage', () => {
    renderDamage({});

    expect(screen.getAllByRole('meter')).toHaveLength(4);
    expect(screen.getAllByRole('meter').every((m) => !m.hasAttribute('aria-valuenow'))).toBe(true);
  });

  /**
   * Two drivers can be compared, so a panel reads its own driver's frame rather than whichever one
   * arrived last.
   */
  it('reads the slow channels of its own driver, not of the other car on screen', () => {
    const store = new LiveStore();
    store.setFollowedDrivers([DRIVER, 'id:9']);

    for (const [driverKey, damageEngine] of [
      [DRIVER, 0.75],
      ['id:9', 0.25],
    ] as const) {
      store.apply(slowFrame(driverKey, { damageEngine }));
    }

    render(
      <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
        <DamagePanel store={store} driverKey={DRIVER} />
      </LiveContext.Provider>,
    );

    expect(screen.getByRole('meter', { name: 'Engine' }).getAttribute('aria-valuenow')).toBe('75');
  });
});
