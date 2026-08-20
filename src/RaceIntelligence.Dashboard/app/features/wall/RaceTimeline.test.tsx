import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { LiveConnection } from '../../shared/live/connection';
import type { TowerRow, TowerSnapshotMessage } from '../../shared/live/contracts';
import { LiveStore } from '../../shared/live/store';
import { LiveContext } from '../../shared/live/useLive';
import { RaceTimeline } from './RaceTimeline';

const DRIVER = 'id:5';

function row(overrides: Partial<TowerRow> & { driverKey: string }): TowerRow {
  return {
    displayName: overrides.driverKey,
    currentSectorMs: [],
    previousSectorMs: [],
    bestSectorMs: [],
    pitLaneState: -1,
    pitStopStatus: -1,
    finishStatus: 0,
    tier: 'Self',
    ...overrides,
  };
}

function snapshot(drivers: TowerRow[]): TowerSnapshotMessage {
  return {
    type: 'towerSnapshot',
    roomId: 'room-1',
    capturedAtUtc: '2026-08-19T12:00:00Z',
    drivers,
  };
}

function renderTimeline(drivers: TowerRow[]) {
  const store = new LiveStore();
  store.setFollowedDrivers([DRIVER]);
  store.apply(snapshot(drivers));

  return render(
    <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
      <RaceTimeline store={store} driverKey={DRIVER} />
    </LiveContext.Provider>,
  );
}

describe('the race timeline', () => {
  it('says so before any standings have arrived', () => {
    const view = renderTimeline([]);

    expect(view.getByText(/No standings yet/)).toBeTruthy();
  });

  /**
   * The comparison a race engineer is making by default: are we taking time out of the front, or
   * losing it. Everything else costs legibility for every other line, so it is opt-in.
   */
  it('starts on the selected car and the leader', () => {
    const view = renderTimeline([
      row({ driverKey: 'id:1', displayName: 'Leader', position: 1 }),
      row({ driverKey: 'id:4', displayName: 'Midfield', position: 4 }),
      row({ driverKey: DRIVER, displayName: 'Mine', position: 6 }),
    ]);

    const on = [...view.container.querySelectorAll('.race-timeline__car--on')].map(
      (node) => node.textContent,
    );

    expect(on).toHaveLength(2);
    expect(on.join(' ')).toContain('Leader');
    expect(on.join(' ')).toContain('Mine');
  });

  it('lists the whole field, not only the cars with a collector', () => {
    const view = renderTimeline([
      row({ driverKey: 'id:1', displayName: 'Leader', position: 1, tier: 'Observed' }),
      row({ driverKey: 'id:2', displayName: 'Rival', position: 2, tier: 'Observed' }),
      row({ driverKey: DRIVER, displayName: 'Mine', position: 3 }),
    ]);

    expect(view.container.querySelectorAll('.race-timeline__car')).toHaveLength(3);
  });

  it('marks a car that is in the pit lane', () => {
    const view = renderTimeline([
      row({ driverKey: 'id:1', displayName: 'Leader', position: 1, inPitLane: true }),
      row({ driverKey: DRIVER, displayName: 'Mine', position: 2, inPitLane: false }),
    ]);

    expect(view.getAllByText('BOX')).toHaveLength(1);
  });

  /**
   * `-1` on the pit codes is "unavailable", and reading it as a negative would report a field in
   * which nobody has stopped all race — a claim a strategist might act on.
   */
  it('does not report an unavailable pit code as a car out on track', () => {
    const view = renderTimeline([
      row({ driverKey: 'id:1', displayName: 'Leader', position: 1, pitLaneState: -1 }),
      row({ driverKey: DRIVER, displayName: 'Mine', position: 2, pitLaneState: -1 }),
    ]);

    // Nothing claimed either way: no BOX marker, and no assertion that they are running.
    expect(view.queryByText('BOX')).toBeNull();
  });

  it('shows how many stops a car has made', () => {
    const view = renderTimeline([
      row({ driverKey: 'id:1', displayName: 'Leader', position: 1, pitStopCount: 2 }),
      row({ driverKey: DRIVER, displayName: 'Mine', position: 2, pitStopCount: 0 }),
    ]);

    // Scoped to the stop count rather than to the text: the leader's position is also "2", and a
    // bare text query would pass whether or not the stop count rendered at all.
    const stops = [...view.container.querySelectorAll('.race-timeline__stops')].map(
      (node) => node.textContent,
    );

    expect(stops).toEqual(['2']);
  });
});
