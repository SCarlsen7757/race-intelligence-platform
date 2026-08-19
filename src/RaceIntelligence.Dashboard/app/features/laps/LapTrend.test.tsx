import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { LapHistoryMessage, LapRecord } from '../../shared/live/contracts';
import { LiveStore } from '../../shared/live/store';
import { LiveContext } from '../../shared/live/useLive';
import type { LiveConnection } from '../../shared/live/connection';
import { LapTrend } from './LapTrend';

const DRIVER = 'id:11';

function history(laps: LapRecord[]): LapHistoryMessage {
  return { type: 'lapHistory', roomId: 'room-1', driverKey: DRIVER, laps, truncated: false };
}

function lap(lapNumber: number, lapTimeMs: number | null, valid?: boolean): LapRecord {
  return {
    lapNumber,
    lapTimeMs,
    sectorMs: [null, null, lapTimeMs],
    ...(valid === undefined ? {} : { valid }),
  };
}

function renderTrend(laps: LapRecord[]) {
  const store = new LiveStore();
  store.setFollowedDrivers(new Set([DRIVER]));
  store.apply(history(laps));

  return render(
    <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
      <LapTrend driverKey={DRIVER} />
    </LiveContext.Provider>,
  );
}

describe('the lap-time trend', () => {
  it('says so when no lap has been completed', () => {
    const view = renderTrend([]);

    expect(view.getByText(/No completed laps/)).toBeTruthy();
  });

  it('plots a point per timed lap', () => {
    const view = renderTrend([lap(1, 91_000), lap(2, 90_500), lap(3, 90_800)]);

    expect(view.container.querySelectorAll('.lap-trend__lap')).toHaveLength(3);
  });

  it('plots a lap the simulator refused, and marks it apart', () => {
    // It happened, so leaving it out would draw a stint shorter than the one that was driven.
    const view = renderTrend([lap(1, 91_000, true), lap(2, 80_000, false)]);

    expect(view.container.querySelectorAll('.lap-trend__lap')).toHaveLength(2);
    expect(view.container.querySelectorAll('.lap-trend__lap--invalid')).toHaveLength(1);
  });

  it('treats a lap of unknown validity as one that counts', () => {
    // `valid === undefined` is silence, not a refusal — see `counts`. A simulator that never reports
    // the flag must not end up with a chart of hollow dots and no average at all.
    const view = renderTrend([lap(1, 91_000), lap(2, 90_500)]);

    expect(view.container.querySelectorAll('.lap-trend__lap--invalid')).toHaveLength(0);
    expect(view.container.querySelector('.lap-trend__mean')?.getAttribute('d')).toBeTruthy();
  });

  it('draws no mean through laps that were all refused', () => {
    const view = renderTrend([lap(1, 91_000, false), lap(2, 90_500, false)]);

    // Both are on the chart; neither may move a pace line.
    expect(view.container.querySelectorAll('.lap-trend__lap')).toHaveLength(2);
    expect(view.container.querySelector('.lap-trend__mean')?.getAttribute('d')).toBe('');
  });
});
