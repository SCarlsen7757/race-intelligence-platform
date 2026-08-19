import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { LiveConnection } from '../../shared/live/connection';
import type { FocusFrameMessage, LapHistoryMessage, LapRecord } from '../../shared/live/contracts';
import { LiveStore } from '../../shared/live/store';
import { LiveContext } from '../../shared/live/useLive';
import { LapDelta } from './LapDelta';

const DRIVER = 'id:5';

function frame(lapNumber: number, fraction: number, simulationTime: number): FocusFrameMessage {
  return {
    type: 'focusFrame',
    roomId: 'room-1',
    driverKey: DRIVER,
    capturedAtUtc: '2026-08-19T08:00:00Z',
    simulationTime,
    lapNumber,
    sector: 1,
    trackPositionFraction: fraction,
    speedMetersPerSecond: 50,
    steering: 0,
    engineRpm: 7000,
    fuelLeftLiters: 40,
    tyrePressureKpa: [null, null, null, null],
    tyreWear: [null, null, null, null],
    tyreTemperatureCelsius: [{}, {}, {}, {}],
  };
}

function lap(lapNumber: number, lapTimeMs: number, valid?: boolean): LapRecord {
  return {
    lapNumber,
    lapTimeMs,
    sectorMs: [null, null, lapTimeMs],
    ...(valid === undefined ? {} : { valid }),
  };
}

function history(laps: LapRecord[]): LapHistoryMessage {
  return { type: 'lapHistory', roomId: 'room-1', driverKey: DRIVER, laps, truncated: false };
}

/** Drives one whole lap past the store, line to line, so it can serve as a reference. */
function driveLap(store: LiveStore, lapNumber: number, lapSeconds: number): void {
  for (let step = 0; step <= 20; step++) {
    const fraction = step / 20;
    store.apply(frame(lapNumber, fraction, lapNumber * 1000 + fraction * lapSeconds));
  }
}

function following(): LiveStore {
  const store = new LiveStore();
  store.setFollowedDrivers(new Set([DRIVER]));
  return store;
}

function renderDelta(store: LiveStore) {
  return render(
    <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
      <LapDelta store={store} driverKey={DRIVER} />
    </LiveContext.Provider>,
  );
}

describe('the lap delta', () => {
  it('explains itself rather than drawing an empty plot before any lap is complete', () => {
    const view = renderDelta(following());

    expect(view.getByText(/No clean lap to compare against/)).toBeTruthy();
  });

  it('compares against a completed clean lap once there is one', () => {
    const store = following();
    driveLap(store, 1, 90);
    // The lap has to close before it can be a reference — an in-progress lap has no final bin.
    driveLap(store, 2, 90);
    store.apply(history([lap(1, 90_000)]));

    const view = renderDelta(store);

    expect(view.queryByText(/No clean lap to compare against/)).toBeNull();
    expect(view.getByText(/Against lap 1/)).toBeTruthy();
  });

  it('will not measure against a lap the simulator refused', () => {
    const store = following();
    driveLap(store, 1, 88);
    driveLap(store, 2, 90);
    store.apply(history([lap(1, 88_000, false)]));

    const view = renderDelta(store);

    expect(view.getByText(/No clean lap to compare against/)).toBeTruthy();
  });

  it('measures against a lap of unknown validity', () => {
    // Silence from the simulator is not a refusal. Treating it as one would leave most sessions
    // with no reference lap at all, which is the failure this rule exists to avoid.
    const store = following();
    driveLap(store, 1, 90);
    driveLap(store, 2, 90);
    store.apply(history([lap(1, 90_000)]));

    expect(renderDelta(store).getByText(/Against lap 1/)).toBeTruthy();
  });
});
