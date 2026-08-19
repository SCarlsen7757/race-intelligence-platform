import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { LiveConnection } from '../../shared/live/connection';
import type { FocusFrameMessage, LapRecord } from '../../shared/live/contracts';
import { LiveStore, type LapSummary } from '../../shared/live/store';
import { LiveContext } from '../../shared/live/useLive';
import { FuelPanel, modelFrom } from './FuelPanel';

const DRIVER = 'id:9';

function summary(lapNumber: number, fuelUsedLiters: number | null): LapSummary {
  return { lapNumber, fuelLeftLiters: null, fuelUsedLiters, lapTimeMs: 90_000 };
}

function frame(lapNumber: number, fuelLeftLiters: number): FocusFrameMessage {
  return {
    type: 'focusFrame',
    roomId: 'room-1',
    driverKey: DRIVER,
    capturedAtUtc: '2026-08-19T12:00:00Z',
    simulationTime: lapNumber * 90,
    lapNumber,
    sector: 1,
    trackPositionFraction: 0,
    speedMetersPerSecond: 60,
    steering: 0,
    engineRpm: 8000,
    fuelLeftLiters,
    tyrePressureKpa: [],
    tyreWear: [],
    tyreTemperatureCelsius: [],
  };
}

function lap(lapNumber: number): LapRecord {
  return { lapNumber, lapTimeMs: 90_000, sectorMs: [null, null, 90_000] };
}

/**
 * Drives a stint through the store, so the panel reads summaries the store actually derived.
 *
 * Fuel reaches a lap summary only when a focus frame carries the lap counter past it — the tank
 * reading belongs to the frame channel and the lap time to the history channel, and they meet in
 * the store. Feeding summaries directly would test the panel against a shape the store never
 * produces.
 */
function renderFuel(tankAtEndOfLap: number[]) {
  const store = new LiveStore();
  store.setFollowedDrivers([DRIVER]);
  store.apply({
    type: 'lapHistory',
    roomId: 'room-1',
    driverKey: DRIVER,
    laps: tankAtEndOfLap.map((_, index) => lap(index + 1)),
    truncated: false,
  });

  tankAtEndOfLap.forEach((litres, index) => {
    // Two frames per lap: one carrying the tank as the lap runs, one whose lap counter has moved
    // and so closes the previous lap with the reading it ended on.
    store.apply(frame(index + 1, litres));
    store.apply(frame(index + 2, litres));
  });

  return render(
    <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
      <FuelPanel store={store} driverKey={DRIVER} />
    </LiveContext.Provider>,
  );
}

describe('the fuel model', () => {
  it('averages the burn over the recent laps rather than trusting the last one', () => {
    // The last lap alone would report 1.0 — a safety-car lap, and a rate that would have somebody
    // staying out four laps too long.
    const model = modelFrom([summary(1, 3), summary(2, 3), summary(3, 1)]);

    expect(model.burnPerLap).toBeCloseTo(7 / 3);
    expect(model.lapsMeasured).toBe(3);
  });

  /**
   * A refuelling stop makes a lap's use negative, and the store keeps it negative on purpose. Left
   * in the mean it would drag the rate below zero and report a car making fuel.
   */
  it('ignores the lap the car refuelled on', () => {
    const model = modelFrom([summary(1, 3), summary(2, -40), summary(3, 3)]);

    expect(model.burnPerLap).toBe(3);
    expect(model.lapsMeasured).toBe(2);
  });

  /** Fuel use can be switched off entirely, and a tank that never moves is not a car burning zero. */
  it('treats a tank that never moves as no reading rather than as no consumption', () => {
    const model = modelFrom([summary(1, 0), summary(2, 0)]);

    expect(model.burnPerLap).toBeNull();
    expect(model.lapsMeasured).toBe(0);
  });

  it('reports nothing before a lap has been completed', () => {
    expect(modelFrom([]).burnPerLap).toBeNull();
    expect(modelFrom([summary(1, null)]).burnPerLap).toBeNull();
  });
});

describe('the fuel panel', () => {
  it('says so before any lap has reported fuel use', () => {
    const view = renderFuel([]);

    expect(view.getByText(/No completed lap has reported fuel use/)).toBeTruthy();
  });

  it('shows the burn rate the stint has actually run at', () => {
    // 60 → 57 → 54: three litres a lap.
    const view = renderFuel([60, 57, 54]);

    expect(view.getByText('3.00')).toBeTruthy();
  });

  /**
   * A projection is arithmetic over an assumption, and the panel has to say which. Presenting it as
   * a measurement is how a fuel number nobody questioned puts a car out on the last lap.
   */
  it('states the assumption behind the rate, and that no finish margin is shown', () => {
    const view = renderFuel([60, 57, 54]);

    expect(view.getByText(/Assumes the next laps burn what the last 2 did/)).toBeTruthy();
    expect(view.getByText(/Race length is not on the live wire/)).toBeTruthy();
  });
});
