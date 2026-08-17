import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { LiveConnection } from '../../shared/live/connection';
import { LiveStore } from '../../shared/live/store';
import { LiveContext } from '../../shared/live/useLive';
import { DamagePanel, toCondition } from './DamagePanel';

const DRIVER = 'id:2';

/**
 * Mounts the panel over a store holding one extras document.
 *
 * The store is fed directly rather than through a socket: the panel reads the low-rate extras
 * channel and nothing else, so a real connection would only add a network to the test.
 */
function renderDamage(damage: Record<string, number>) {
  const store = new LiveStore();
  store.setFollowedDrivers([DRIVER]);
  store.apply({
    type: 'extrasFrame',
    roomId: 'room',
    driverKey: DRIVER,
    capturedAtUtc: '2026-08-16T12:00:00Z',
    extras: JSON.stringify({ damage }),
  });

  return render(
    <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
      <DamagePanel store={store} driverKey={DRIVER} />
    </LiveContext.Provider>,
  );
}

describe('toCondition', () => {
  /**
   * RaceRoom's `-1` means "not available", not "destroyed". Reading it as a number would tell a race
   * engineer the opposite of the truth twice over: that there is a reading, and that it is the worst
   * possible one.
   */
  it('reads the simulator sentinel as no reading rather than as total damage', () => {
    expect(toCondition(-1)).toBeNull();
    expect(toCondition(undefined)).toBeNull();
    expect(toCondition(Number.NaN)).toBeNull();
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
    renderDamage({ engine: 0.75 });

    expect(screen.getByRole('meter', { name: 'Engine' }).getAttribute('aria-valuenow')).toBe('75');
    expect(screen.getByText('75%')).toBeDefined();
  });

  /**
   * A screen reader announcing "0%" for a channel the simulator never reported would be exactly the
   * lie the em dash avoids visually, so the attribute is omitted rather than defaulted.
   */
  it('omits aria-valuenow for an unreported channel rather than announcing zero', () => {
    renderDamage({ engine: 1, transmission: -1 });

    const gearbox = screen.getByRole('meter', { name: 'Gearbox' });

    expect(gearbox.hasAttribute('aria-valuenow')).toBe(false);
    expect(gearbox.querySelector('.damage__fill')).toBeNull();
  });

  it('marks a component below half condition as critical', () => {
    const { container } = renderDamage({ engine: 0.4, transmission: 0.9 });

    expect(container.querySelectorAll('.damage__fill--critical')).toHaveLength(1);
  });

  /** One malformed payload is not a reason to take the focus panel down with it. */
  it('renders every channel as unreported when the extras document will not parse', () => {
    const store = new LiveStore();
    store.setFollowedDrivers([DRIVER]);
    store.apply({
      type: 'extrasFrame',
      roomId: 'room',
      driverKey: DRIVER,
      capturedAtUtc: '2026-08-16T12:00:00Z',
      extras: '{ not json',
    });

    render(
      <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
        <DamagePanel store={store} driverKey={DRIVER} />
      </LiveContext.Provider>,
    );

    expect(screen.getAllByRole('meter')).toHaveLength(4);
    expect(screen.getAllByRole('meter').every((m) => !m.hasAttribute('aria-valuenow'))).toBe(true);
  });

  /**
   * Two drivers can be compared, so a panel reads its own driver's extras rather than whichever
   * document arrived last.
   */
  it('reads the extras of its own driver, not of the other car on screen', () => {
    const store = new LiveStore();
    store.setFollowedDrivers([DRIVER, 'id:9']);

    for (const [driverKey, engine] of [
      [DRIVER, 0.75],
      ['id:9', 0.25],
    ] as const) {
      store.apply({
        type: 'extrasFrame',
        roomId: 'room',
        driverKey,
        capturedAtUtc: '2026-08-16T12:00:00Z',
        extras: JSON.stringify({ damage: { engine } }),
      });
    }

    render(
      <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
        <DamagePanel store={store} driverKey={DRIVER} />
      </LiveContext.Provider>,
    );

    expect(screen.getByRole('meter', { name: 'Engine' }).getAttribute('aria-valuenow')).toBe('75');
  });
});
