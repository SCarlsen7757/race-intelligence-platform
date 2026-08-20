import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { LiveConnection } from '../../shared/live/connection';
import type {
  FocusFrameMessage,
  LapRecord,
  RaceLengthState,
  TowerRow,
} from '../../shared/live/contracts';
import { LiveStore, type LapSummary } from '../../shared/live/store';
import { LiveContext } from '../../shared/live/useLive';
import { FuelPanel, lapsRemainingIn, modelFrom } from './FuelPanel';

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
    brakePressureKiloNewtons: [],
  };
}

function lap(lapNumber: number): LapRecord {
  return { lapNumber, lapTimeMs: 90_000, sectorMs: [null, null, 90_000] };
}

/** The one row the margin reads: how many laps this driver has finished. */
function towerRow(completedLaps: number): TowerRow {
  return {
    driverKey: DRIVER,
    displayName: 'Driver 9',
    completedLaps,
    currentSectorMs: [],
    previousSectorMs: [],
    bestSectorMs: [],
    pitLaneState: -1,
    pitStopStatus: -1,
    finishStatus: 0,
    tier: 'Self',
  };
}

/**
 * Drives a stint through the store, so the panel reads summaries the store actually derived.
 *
 * Fuel reaches a lap summary only when a focus frame carries the lap counter past it — the tank
 * reading belongs to the frame channel and the lap time to the history channel, and they meet in
 * the store. Feeding summaries directly would test the panel against a shape the store never
 * produces.
 */
function renderFuel(
  tankAtEndOfLap: number[],
  session?: { raceLength?: RaceLengthState; completedLaps?: number },
) {
  const store = new LiveStore();
  store.setFollowedDrivers([DRIVER]);
  store.apply({
    type: 'lapHistory',
    roomId: 'room-1',
    driverKey: DRIVER,
    laps: tankAtEndOfLap.map((_, index) => lap(index + 1)),
    truncated: false,
  });

  if (session !== undefined) {
    store.apply({
      type: 'sessionState',
      roomId: 'room-1',
      ...(session.raceLength === undefined ? {} : { raceLength: session.raceLength }),
    });

    store.apply({
      type: 'towerSnapshot',
      roomId: 'room-1',
      capturedAtUtc: '2026-08-19T12:00:00Z',
      drivers: [towerRow(session.completedLaps ?? 0)],
    });
  }

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
  it('states the assumption behind the rate', () => {
    const view = renderFuel([60, 57, 54]);

    expect(view.getByText(/Assumes the next laps burn what the last 2 did/)).toBeTruthy();
  });

  /**
   * 54 litres at 3 a lap is 18 laps in the tank; 10 left to run leaves 8 spare.
   *
   * Awaited rather than read straight off the render, because the margin is a `LiveReadout` — it
   * writes its own `textContent` from a paint loop rather than through React, which is the whole
   * reason a 60 Hz channel costs no renders. `findByText` polls until that loop has run.
   */
  it('shows the margin over the laps left to run', async () => {
    const view = renderFuel([60, 57, 54], {
      raceLength: { laps: 12, unit: 'Laps' },
      completedLaps: 2,
    });

    expect(await view.findByText('+8.0')).toBeTruthy();
    expect(view.getByText(/10 laps left to run/)).toBeTruthy();
  });

  /** The sign is the decision: 18 laps in the tank against 25 still to run is a stop, planned or not. */
  it('shows a negative margin when the tank will not reach the flag', async () => {
    const view = renderFuel([60, 57, 54], {
      raceLength: { laps: 30, unit: 'Laps' },
      completedLaps: 5,
    });

    expect(await view.findByText('-7.0')).toBeTruthy();
  });

  /**
   * A timed race has no knowable lap count from this wire, and inventing one is exactly the guess
   * this panel refuses to make.
   */
  it('shows no margin in a timed race, and says why', () => {
    const view = renderFuel([60, 57, 54], {
      raceLength: { durationSeconds: 3_600, unit: 'Time' },
      completedLaps: 2,
    });

    expect(view.queryByText('laps margin')).toBeNull();
    expect(view.getByText(/only known in a lap race/)).toBeTruthy();
  });
});

describe('the laps left to run', () => {
  it('counts down from the race length', () => {
    expect(lapsRemainingIn({ laps: 30, unit: 'Laps' }, 12)).toBe(18);
  });

  /**
   * The unit decides, never which figure is present. RaceRoom reports a lap count in a timed
   * session too, and counting down to it would name a flag that is not there.
   */
  it('refuses a lap count the simulator did not say governs', () => {
    expect(lapsRemainingIn({ laps: 30, durationSeconds: 3_600, unit: 'Time' }, 12)).toBeNull();
    expect(lapsRemainingIn({ laps: 30, unit: 'Unknown' }, 12)).toBeNull();
    expect(lapsRemainingIn(null, 12)).toBeNull();
  });

  /** The leader keeps being reported for a moment after the flag; a surplus there is meaningless. */
  it('never goes negative once the distance is run', () => {
    expect(lapsRemainingIn({ laps: 30, unit: 'Laps' }, 31)).toBe(0);
  });

  /** Before the lights, a driver the tower has not placed has run nothing. */
  it('treats an unplaced driver as having completed nothing', () => {
    expect(lapsRemainingIn({ laps: 30, unit: 'Laps' }, null)).toBe(30);
  });
});
