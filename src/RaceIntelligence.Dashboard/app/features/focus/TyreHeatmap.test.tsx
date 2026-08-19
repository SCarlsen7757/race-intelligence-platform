import { act, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { LiveConnection } from '../../shared/live/connection';
import type { FocusFrameMessage, TreadTemperatures } from '../../shared/live/contracts';
import { LiveStore } from '../../shared/live/store';
import { LiveContext } from '../../shared/live/useLive';
import { firstReportedWindow } from './operatingWindow';
import { TyreHeatmap } from './TyreHeatmap';

const DRIVER = 'id:9';

function frame(tread: TreadTemperatures[]): FocusFrameMessage {
  return {
    type: 'focusFrame',
    roomId: 'room',
    driverKey: DRIVER,
    capturedAtUtc: '2026-08-19T10:00:00Z',
    simulationTime: 1,
    lapNumber: 2,
    sector: 1,
    speedMetersPerSecond: 50,
    steering: 0,
    engineRpm: 7000,
    fuelLeftLiters: 40,
    tyrePressureKpa: [null, null, null, null],
    tyreWear: [null, null, null, null],
    tyreTemperatureCelsius: tread,
  };
}

/** A tread reading with the same window on every corner, which is how a simulator reports one. */
function corner(inner: number, middle: number, outer: number): TreadTemperatures {
  return { inner, middle, outer, cold: 70, optimal: 90, hot: 110 };
}

/** A corner that reports a reading and no window at all. */
function windowless(middle: number): TreadTemperatures {
  return { middle };
}

async function renderHeatmap(tread: TreadTemperatures[], hidden: readonly string[] = []) {
  const store = new LiveStore();
  store.setFollowedDrivers([DRIVER]);
  store.apply(frame(tread));

  const rendered = render(
    <LiveContext.Provider value={{ store, connection: {} as LiveConnection }}>
      <TyreHeatmap
        store={store}
        driverKey={DRIVER}
        hiddenChannels={hidden}
        onToggleChannel={vi.fn()}
      />
    </LiveContext.Provider>,
  );

  // The paint loop is a requestAnimationFrame, which `vitest.setup.ts` backs with a timer. One tick
  // is enough for the first frame to be written to the DOM.
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 32));
  });

  return { store, ...rendered };
}

describe('firstReportedWindow', () => {
  /**
   * A window belongs to the compound rather than to a corner, so all four report the same numbers.
   * Drawing four identical bands would be drawing one band four times as opaque.
   */
  it('takes one window for the whole car', () => {
    expect(firstReportedWindow([corner(80, 85, 90), corner(81, 86, 91)])).toEqual({
      cold: 70,
      optimal: 90,
      hot: 110,
    });
  });

  /**
   * The corners a simulator declines to answer for are not always the ones you would guess, so the
   * first *reported* window wins rather than simply the front left's.
   */
  it('skips corners that report nothing and takes the first that does', () => {
    expect(firstReportedWindow([windowless(80), corner(81, 86, 91)])).toEqual({
      cold: 70,
      optimal: 90,
      hot: 110,
    });
  });

  /** RaceRoom's `-1` is "not available". A band drawn from it would sit below freezing. */
  it('reads the simulator sentinel as no window rather than as a window at minus one', () => {
    expect(firstReportedWindow([{ cold: -1, optimal: -1, hot: -1 }])).toBeNull();
  });

  it('has no window to give when nothing reported one', () => {
    expect(firstReportedWindow([windowless(80)])).toBeNull();
    expect(firstReportedWindow(undefined)).toBeNull();
  });

  /**
   * Bounds and optimum are drawn independently, so one without the others is still worth having —
   * requiring the full set would throw away two thirds of a window because the third was missing.
   */
  it('keeps a partial window rather than discarding it', () => {
    expect(firstReportedWindow([{ optimal: 90 }])).toEqual({
      cold: null,
      optimal: 90,
      hot: null,
    });
  });
});

describe('TyreHeatmap', () => {
  /**
   * Twelve readings, not four. The spread across a tread is the whole reason the wire was widened
   * to carry the shoulders — the middle alone can only say "warm".
   */
  it('shows all three tread positions for every corner', async () => {
    await renderHeatmap([
      corner(95, 90, 85),
      corner(84, 83, 82),
      corner(76, 75, 74),
      corner(73, 72, 71),
    ]);

    for (const reading of [95, 90, 85, 84, 83, 82, 76, 75, 74, 73, 72, 71]) {
      expect(screen.getByText(String(reading))).toBeTruthy();
    }
  });

  /**
   * A reading the simulator did not take is not a reading of zero — shown as a dash, and left
   * without a heat colour, because there is nothing to place on the scale.
   */
  it('shows an unreported shoulder as no reading rather than as cold', async () => {
    const { container } = await renderHeatmap([
      { middle: 90, cold: 70, optimal: 90, hot: 110 },
      corner(84, 83, 82),
      corner(76, 75, 74),
      corner(73, 72, 71),
    ]);

    expect(screen.getAllByText('—').length).toBe(2);

    const cells = [...container.querySelectorAll('.tyre-heatmap__cell')];
    const blank = cells.find((cell) => cell.textContent === '—')!;
    expect((blank as HTMLElement).style.background).toBe('transparent');
  });

  /**
   * The scale is the simulator's own cold and hot thresholds. Without them there is no answer to
   * "is 84 °C hot" this dashboard is entitled to invent, so the cells stay neutral and show numbers.
   */
  it('draws no heat colours at all when the simulator reports no window', async () => {
    const { container } = await renderHeatmap([
      { inner: 95, middle: 90, outer: 85 },
      { inner: 84, middle: 83, outer: 82 },
      { inner: 76, middle: 75, outer: 74 },
      { inner: 73, middle: 72, outer: 71 },
    ]);

    const cells = [...container.querySelectorAll('.tyre-heatmap__cell')];
    expect(cells).toHaveLength(12);

    for (const cell of cells) {
      expect((cell as HTMLElement).style.background).toBe('transparent');
    }

    // The numbers are still there — a heatmap with no scale is a table, not an empty panel.
    expect(screen.getByText('95')).toBeTruthy();
  });

  /** With a window, a tyre above its hot threshold and one below its cold one differ visibly. */
  it('colours a cooking shoulder differently from a cold one', async () => {
    const { container } = await renderHeatmap([
      corner(130, 90, 50),
      corner(84, 83, 82),
      corner(76, 75, 74),
      corner(73, 72, 71),
    ]);

    const cells = [...container.querySelectorAll('.tyre-heatmap__cell')] as HTMLElement[];
    const hot = cells.find((cell) => cell.textContent === '130')!;
    const cold = cells.find((cell) => cell.textContent === '50')!;

    expect(hot.style.background).not.toBe('');
    expect(hot.style.background).not.toBe(cold.style.background);
  });

  /**
   * A corner switched off is dimmed rather than removed. A missing quarter of the car would read as
   * a missing reading, which is the opposite of what the user asked for.
   */
  it('keeps the car whole when a corner is hidden', async () => {
    const { container } = await renderHeatmap(
      [corner(95, 90, 85), corner(84, 83, 82), corner(76, 75, 74), corner(73, 72, 71)],
      ['fl'],
    );

    expect(container.querySelectorAll('.tyre-heatmap__corner')).toHaveLength(4);
    expect(container.querySelectorAll('.tyre-heatmap__corner--off')).toHaveLength(1);
  });
});
