import { act, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { LiveConnection } from '../../shared/live/connection';
import type { BrakeTemperature } from '../../shared/live/contracts';
import { LiveStore } from '../../shared/live/store';
import { LiveContext } from '../../shared/live/useLive';
import { firstReportedWindow } from '../../features/focus/operatingWindow';
import { BrakePressurePanel, BrakeTemperaturePanel } from './BrakePanel';

const DRIVER = 'id:4';

/**
 * Renders the pressure panel against a focus frame rather than an extras document.
 *
 * Pressure moved to the fast channel because a braking event lasts about a second, which at the
 * extras cadence is one or two samples of the thing being asked about. Its own helper rather than a
 * flag on the one below, because the two panels genuinely read different channels now — temperature
 * is still a stint-rate reading from the connector's document.
 */
async function renderPressure(pressure: (number | null)[], hidden: readonly string[] = []) {
  const store = new LiveStore();
  store.setFollowedDrivers([DRIVER]);
  store.apply({
    type: 'focusFrame',
    roomId: 'room',
    driverKey: DRIVER,
    capturedAtUtc: '2026-08-19T11:00:00Z',
    simulationTime: 0,
    lapNumber: 1,
    sector: 1,
    speedMetersPerSecond: 50,
    throttle: 0,
    brake: 1,
    clutch: 0,
    steering: 0,
    gear: 3,
    engineRpm: 6000,
    fuelLeftLiters: 40,
    brakePressureKiloNewtons: pressure,
  });

  const rendered = render(
    <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
      <BrakePressurePanel
        store={store}
        driverKey={DRIVER}
        hiddenChannels={hidden}
        onToggleChannel={vi.fn()}
      />
    </LiveContext.Provider>,
  );

  // The readouts write their text from a paint loop rather than from a render, so a freshly mounted
  // panel still says "—" until one frame has run. Waiting here keeps that mechanism out of every
  // assertion below.
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 32));
  });

  return rendered;
}

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
  it('reads out pressure for every corner', async () => {
    await renderPressure([12.4, 12.9, 6.1, 6.0]);

    for (const reading of ['12.4', '12.9', '6.1', '6.0']) {
      expect(screen.getByText(reading)).toBeTruthy();
    }
  });

  /**
   * An unmeasured corner reads as no reading, never as a corner taking no pressure — which would be
   * a brake that has failed rather than one nobody measured.
   *
   * Null rather than RaceRoom's `-1` because pressure moved to the typed wire, where the connector
   * translates the sentinel (`NullIfNegative`) before it is ever sent. That is a real gain from the
   * move: the extras document carries the connector's raw values and leaves every reader to know
   * the sentinel, and this channel no longer does.
   */
  it('shows an unmeasured corner as no reading rather than as no braking', async () => {
    await renderPressure([null, 12.9, 6.1, 6.0]);

    expect(screen.getAllByText('—').length).toBeGreaterThan(0);
    expect(screen.queryByText('-1.0')).toBeNull();
    expect(screen.queryByText('0.0')).toBeNull();
  });

  /** An imbalance across an axle is the reading this chart exists for, so all four stay legible. */
  it('keeps a hidden corner readable, because the line going away is what was asked for', async () => {
    await renderPressure([12.4, 12.9, 6.1, 6.0], ['fl']);

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
        extras: JSON.stringify({ brakeTemperatureCelsius: [{ current: second }, {}, {}, {}] }),
      });
    }

    const rings = store.tracesFor(DRIVER).extras.brakeTemperatureCelsius;
    expect(rings[0].length).toBe(3);

    const { unmount } = render(
      <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
        <BrakeTemperaturePanel
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

    expect(store.tracesFor(DRIVER).extras.brakeTemperatureCelsius[0].length).toBe(3);
  });
});
