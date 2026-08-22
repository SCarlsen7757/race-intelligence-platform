import { render } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { LiveConnection } from '../../shared/live/connection';
import type { TowerRow } from '../../shared/live/contracts';
import { formatLapTime } from '../../shared/format/format';
import { LiveStore } from '../../shared/live/store';
import { LiveContext } from '../../shared/live/useLive';
import { LapFeedPanel } from './LapFeedPanel';

/** A tower row with only the fields this panel or its roster diff read. */
function row(driverKey: string, displayName: string): TowerRow {
  return {
    driverKey,
    displayName,
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
 * A stand-in for `LiveConnection` that only spies on the two calls this panel makes. A real
 * connection's refcounting is `connection.test.ts`'s concern; this file is about the roster diff
 * that decides when those two calls happen.
 */
function fakeConnection(): LiveConnection {
  return {
    subscribeLapHistory: vi.fn(),
    unsubscribeLapHistory: vi.fn(),
  } as unknown as LiveConnection;
}

describe('LapFeedPanel', () => {
  it('subscribes lap history for every driver currently in the room', () => {
    const store = new LiveStore();
    const connection = fakeConnection();

    render(
      <LiveContext.Provider value={{ store, connection }}>
        <LapFeedPanel rows={[row('id:1', 'One'), row('id:2', 'Two')]} />
      </LiveContext.Provider>,
    );

    expect(connection.subscribeLapHistory).toHaveBeenCalledWith('id:1');
    expect(connection.subscribeLapHistory).toHaveBeenCalledWith('id:2');
  });

  it('follows the room roster: subscribes who joined, unsubscribes who left', () => {
    const store = new LiveStore();
    const connection = fakeConnection();

    const view = render(
      <LiveContext.Provider value={{ store, connection }}>
        <LapFeedPanel rows={[row('id:1', 'One')]} />
      </LiveContext.Provider>,
    );

    view.rerender(
      <LiveContext.Provider value={{ store, connection }}>
        <LapFeedPanel rows={[row('id:2', 'Two')]} />
      </LiveContext.Provider>,
    );

    expect(connection.unsubscribeLapHistory).toHaveBeenCalledWith('id:1');
    expect(connection.subscribeLapHistory).toHaveBeenCalledWith('id:2');
    // The driver who stayed subscribed the whole time is never re-asked for.
    expect(connection.unsubscribeLapHistory).not.toHaveBeenCalledWith('id:2');
  });

  it('unsubscribes everyone it owns on unmount', () => {
    const store = new LiveStore();
    const connection = fakeConnection();

    const view = render(
      <LiveContext.Provider value={{ store, connection }}>
        <LapFeedPanel rows={[row('id:1', 'One'), row('id:2', 'Two')]} />
      </LiveContext.Provider>,
    );

    view.unmount();

    expect(connection.unsubscribeLapHistory).toHaveBeenCalledWith('id:1');
    expect(connection.unsubscribeLapHistory).toHaveBeenCalledWith('id:2');
  });

  it('says so before any lap has come in', () => {
    const store = new LiveStore();
    const connection = fakeConnection();

    const view = render(
      <LiveContext.Provider value={{ store, connection }}>
        <LapFeedPanel rows={[row('id:1', 'One')]} />
      </LiveContext.Provider>,
    );

    expect(view.getByText('No laps completed yet.')).toBeTruthy();
  });

  /**
   * Feeds the store the same way the hub would — a seeding message, then the full snapshot with one
   * more lap in it — rather than reaching into the panel's private state, so this exercises the same
   * `updateLapFeed` path `store.test.ts` covers directly.
   */
  it('renders the feed the store has built, driver name and lap time included', () => {
    const store = new LiveStore();
    const connection = fakeConnection();

    store.apply({
      type: 'towerSnapshot',
      roomId: 'room',
      capturedAtUtc: '2026-08-19T12:00:00Z',
      drivers: [row('id:1', 'Driver One')],
    });
    store.apply({
      type: 'lapHistory',
      roomId: 'room',
      driverKey: 'id:1',
      truncated: false,
      laps: [{ lapNumber: 1, lapTimeMs: 90_000, sectorMs: [30_000, 60_000, 90_000] }],
    });
    store.apply({
      type: 'lapHistory',
      roomId: 'room',
      driverKey: 'id:1',
      truncated: false,
      laps: [
        { lapNumber: 1, lapTimeMs: 90_000, sectorMs: [30_000, 60_000, 90_000] },
        { lapNumber: 2, lapTimeMs: 88_000, sectorMs: [29_000, 58_000, 88_000] },
      ],
    });

    const view = render(
      <LiveContext.Provider value={{ store, connection }}>
        <LapFeedPanel rows={[row('id:1', 'Driver One')]} />
      </LiveContext.Provider>,
    );

    expect(view.getByText('Driver One')).toBeTruthy();
    expect(view.getByText('2')).toBeTruthy();
    expect(view.getByText(formatLapTime(88_000))).toBeTruthy();
  });
});
