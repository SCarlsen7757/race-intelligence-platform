import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { FocusFrameMessage } from '../../shared/live/contracts';
import { LiveStore } from '../../shared/live/store';
import { INPUT_CHANNELS, InputsTrace } from './InputsTrace';

const DRIVER = 'id:7';

function frame(overrides: Partial<FocusFrameMessage> = {}): FocusFrameMessage {
  return {
    type: 'focusFrame',
    roomId: 'room-1',
    driverKey: DRIVER,
    capturedAtUtc: '2026-08-19T08:00:00Z',
    simulationTime: 1,
    lapNumber: 1,
    sector: 1,
    speedMetersPerSecond: 55,
    steering: 0,
    engineRpm: 7200,
    fuelLeftLiters: 40,
    tyrePressureKpa: [null, null, null, null],
    tyreWear: [null, null, null, null],
    tyreTemperatureCelsius: [{}, {}, {}, {}],
    ...overrides,
  };
}

function following(): LiveStore {
  const store = new LiveStore();
  store.setFollowedDrivers(new Set([DRIVER]));
  return store;
}

describe('the inputs trace', () => {
  it('offers every channel the overlay is for', () => {
    // The list is what the catalogue turns into toggles, so a channel missing here is a channel the
    // user can never reach — which is the failure this guards, not the ordering.
    expect(INPUT_CHANNELS.map((channel) => channel.id)).toEqual([
      'throttle',
      'brake',
      'clutch',
      'steering',
      'speed',
      'gear',
      'rpm',
      'abs',
      'tc',
    ]);
  });

  it('renders a legend entry per channel', () => {
    const store = following();
    const view = render(
      <InputsTrace
        store={store}
        driverKey={DRIVER}
        hiddenChannels={[]}
        onToggleChannel={() => {}}
      />,
    );

    for (const channel of INPUT_CHANNELS) {
      expect(view.getByRole('button', { name: new RegExp(channel.label) })).toBeTruthy();
    }
  });
});

describe('the assist channels', () => {
  it('leaves a gap where the simulator reports no assist at all', () => {
    const store = following();
    store.apply(frame());

    // Not zero. A car whose simulator says nothing about ABS must not draw the flat baseline that
    // means "watched, and it never engaged" — that is a claim nobody made.
    expect(store.tracesFor(DRIVER).absActive.last()).toBeNaN();
    expect(store.tracesFor(DRIVER).tractionControlActive.last()).toBeNaN();
  });

  it('draws a baseline where an assist is reported and quiet', () => {
    const store = following();
    store.apply(frame({ absActive: false, tractionControlActive: false }));

    expect(store.tracesFor(DRIVER).absActive.last()).toBe(0);
    expect(store.tracesFor(DRIVER).tractionControlActive.last()).toBe(0);
  });

  it('raises the marker while an assist is intervening', () => {
    const store = following();
    store.apply(frame({ absActive: true, tractionControlActive: true }));

    expect(store.tracesFor(DRIVER).absActive.last()).toBe(1);
    expect(store.tracesFor(DRIVER).tractionControlActive.last()).toBe(1);
  });
});

describe('the car channels', () => {
  it('records speed and rpm, and leaves an unreported gear as a hole', () => {
    const store = following();
    store.apply(frame({ speedMetersPerSecond: 61, engineRpm: 8100 }));

    const traces = store.tracesFor(DRIVER);
    expect(traces.speed.last()).toBe(61);
    expect(traces.engineRpm.last()).toBe(8100);
    // Neutral is a real gear, so an unreported gearbox cannot be plotted as one.
    expect(traces.gear.last()).toBeNaN();
  });

  it('records a reported gear, including neutral', () => {
    const store = following();
    store.apply(frame({ gear: 0 }));

    expect(store.tracesFor(DRIVER).gear.last()).toBe(0);
  });
});
