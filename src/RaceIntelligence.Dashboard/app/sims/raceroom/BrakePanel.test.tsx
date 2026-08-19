import { act, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { LiveConnection } from '../../shared/live/connection';
import type { BrakeTemperature } from '../../shared/live/contracts';
import { LiveStore } from '../../shared/live/store';
import { LiveContext } from '../../shared/live/useLive';
import { firstReportedWindow } from '../../features/focus/operatingWindow';
import { BrakePressurePanel, BrakeTemperaturePanel } from './BrakePanel';

const DRIVER = 'id:4';

function renderPanel(
  Panel: typeof BrakePressurePanel,
  extras: Record<string, unknown>,
  hidden: readonly string[] = [],
) {
  const store = new LiveStore();
  store.setFollowedDrivers([DRIVER]);
  store.apply({
    type: 'extrasFrame',
    roomId: 'room',
    driverKey: DRIVER,
    capturedAtUtc: '2026-08-19T11:00:00Z',
    extras: JSON.stringify(extras),
  });

  return render(
    <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
      <Panel store={store} driverKey={DRIVER} hiddenChannels={hidden} onToggleChannel={vi.fn()} />
    </LiveContext.Provider>,
  );
}

describe('BrakePressurePanel', () => {
  /**
   * The channel the extras document has carried all along and nothing read. Temperature says the
   * discs are working; pressure says how they were asked to.
   */
  it('reads out pressure for every corner', () => {
    renderPanel(BrakePressurePanel, {
      brakePressureKiloNewtons: [12.4, 12.9, 6.1, 6.0],
    });

    for (const reading of ['12.4', '12.9', '6.1', '6.0']) {
      expect(screen.getByText(reading)).toBeTruthy();
    }
  });

  /**
   * RaceRoom's `-1` is "not available". Drawn as a number it would read as a corner taking no
   * pressure at all — a brake that has failed rather than one nobody measured.
   */
  it('shows the simulator sentinel as no reading rather than as no braking', () => {
    renderPanel(BrakePressurePanel, {
      brakePressureKiloNewtons: [-1, 12.9, 6.1, 6.0],
    });

    expect(screen.getByText('—')).toBeTruthy();
    expect(screen.queryByText('-1.0')).toBeNull();
  });

  /** An imbalance across an axle is the reading this chart exists for, so all four stay legible. */
  it('keeps a hidden corner readable, because the line going away is what was asked for', () => {
    renderPanel(BrakePressurePanel, { brakePressureKiloNewtons: [12.4, 12.9, 6.1, 6.0] }, ['fl']);

    expect(screen.getByText('12.4')).toBeTruthy();
  });
});

describe('brake operating window', () => {
  /**
   * The echo of the tyre window, deliberately the same shape and read by the same helper. A brake
   * and a tyre are the same question asked of different hardware, and 380 °C is cold on one car and
   * cooking on another.
   */
  it('takes one window for the whole car, from the first corner that reports it', () => {
    const corners: BrakeTemperature[] = [
      { current: 380 },
      { current: 390, optimal: 450, cold: 300, hot: 700 },
      { current: 300 },
      { current: 310 },
    ];

    expect(firstReportedWindow(corners)).toEqual({ cold: 300, optimal: 450, hot: 700 });
  });

  /** A simulator reporting no window gets no band, never one invented from a nominal value. */
  it('has no window when the simulator reports none', () => {
    const corners: BrakeTemperature[] = [{ current: 380 }, { current: 390 }];

    expect(firstReportedWindow(corners)).toBeNull();
  });

  it('reads out brake temperature beside the band', () => {
    renderPanel(BrakeTemperaturePanel, {
      brakeTemperatureCelsius: [
        { current: 380, optimal: 450, cold: 300, hot: 700 },
        { current: 390, optimal: 450, cold: 300, hot: 700 },
        { current: 300, optimal: 450, cold: 300, hot: 700 },
        { current: 310, optimal: 450, cold: 300, hot: 700 },
      ],
    });

    expect(screen.getByText('380')).toBeTruthy();
    expect(screen.getByText('390')).toBeTruthy();
  });
});

describe('the extras rings behind both panels', () => {
  /**
   * The rings live in the store rather than in the panel, which is what lets a tile dragged to a new
   * position keep its stint instead of starting from empty.
   */
  it('keeps a stint across a remount', async () => {
    const store = new LiveStore();
    store.setFollowedDrivers([DRIVER]);

    for (let second = 0; second < 3; second++) {
      store.apply({
        type: 'extrasFrame',
        roomId: 'room',
        driverKey: DRIVER,
        capturedAtUtc: `2026-08-19T11:00:0${second}Z`,
        extras: JSON.stringify({ brakePressureKiloNewtons: [second, second, second, second] }),
      });
    }

    const rings = store.tracesFor(DRIVER).extras.brakePressureKiloNewtons;
    expect(rings[0].length).toBe(3);

    const { unmount } = render(
      <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
        <BrakePressurePanel
          store={store}
          driverKey={DRIVER}
          hiddenChannels={[]}
          onToggleChannel={vi.fn()}
        />
      </LiveContext.Provider>,
    );

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 32));
    });
    unmount();

    expect(store.tracesFor(DRIVER).extras.brakePressureKiloNewtons[0].length).toBe(3);
  });
});
