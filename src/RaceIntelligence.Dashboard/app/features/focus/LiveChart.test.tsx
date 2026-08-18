import { act, render } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { FocusFrameMessage } from '../../shared/live/contracts';
import { LiveStore, TRACE_CAPACITY } from '../../shared/live/store';
import { LiveChart, type LiveChartSpec } from './LiveChart';

/**
 * uPlot is replaced rather than driven, because what this component promises is about *when* it
 * talks to the chart, not about pixels: how many charts it builds, what it hands them, and when it
 * stops. None of that is observable through a real canvas, and the jsdom stub cannot rasterise one
 * anyway. The real integration is covered by the panels that mount a live chart for effect.
 */
const uplot = vi.hoisted(() => ({
  builds: [] as {
    data: unknown[];
    setData: ReturnType<typeof vi.fn>;
    destroy: ReturnType<typeof vi.fn>;
  }[],
}));

vi.mock('uplot', () => {
  class FakeUPlot {
    readonly setData = vi.fn();
    readonly setSize = vi.fn();
    readonly destroy = vi.fn();

    constructor(_options: unknown, data: unknown[]) {
      uplot.builds.push({ data, setData: this.setData, destroy: this.destroy });
    }
  }

  return { default: FakeUPlot };
});

const DRIVER = 'id:2';

function focusFrame(overrides: Partial<FocusFrameMessage> = {}): FocusFrameMessage {
  return {
    type: 'focusFrame',
    roomId: 'room',
    driverKey: DRIVER,
    capturedAtUtc: '2026-08-16T12:00:00Z',
    simulationTime: 0,
    lapNumber: 1,
    sector: 1,
    speedMetersPerSecond: 50,
    throttle: 1,
    brake: 0,
    steering: 0,
    engineRpm: 6000,
    fuelLeftLiters: 40,
    tyrePressureKpa: [null, null, null, null],
    tyreWear: [null, null, null, null],
    tyreTemperatureCelsius: [null, null, null, null],
    ...overrides,
  };
}

/**
 * A chart whose spec is rebuilt on every render, which is exactly the shape the old components
 * forbade by convention. `unused` exists only to force a re-render without changing anything the
 * chart is pointed at.
 */
function Harness({
  store,
  driverKey,
}: {
  store: LiveStore;
  driverKey: string;
  /** Changed by a test purely to force a re-render. Nothing reads it. */
  unused?: number;
}) {
  const spec: LiveChartSpec = {
    capacity: TRACE_CAPACITY,
    scales: { pedal: { range: [0, 1] } },
    series: [
      {
        label: 'Throttle',
        scale: 'pedal',
        stroke: '#0f0',
        buffer: () => store.tracesFor(driverKey).throttle,
      },
      {
        label: 'Brake',
        scale: 'pedal',
        stroke: '#f00',
        buffer: () => store.tracesFor(driverKey).brake,
      },
    ],
  };

  return <LiveChart store={store} driverKey={driverKey} spec={spec} />;
}

function following(): LiveStore {
  const store = new LiveStore();
  store.setFollowedDrivers([DRIVER]);

  return store;
}

/** Lets the rAF-backed paint loop run. The setup file schedules animation frames as timers. */
async function paint() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 40));
  });
}

beforeEach(() => {
  uplot.builds.length = 0;
});

describe('LiveChart', () => {
  /**
   * The point of the whole component. Three separate chart components used to key their effect on
   * the caller's own functions, which meant an inline arrow destroyed and rebuilt the chart on
   * every render of the parent — survivable only because a comment told every caller to hoist them
   * to module scope, a rule enforced by nothing. Holding the spec in a ref removes the rule.
   */
  it('does not rebuild the chart when the caller passes a fresh spec object', async () => {
    const store = following();
    const { rerender } = render(<Harness store={store} driverKey={DRIVER} unused={1} />);

    expect(uplot.builds).toHaveLength(1);

    rerender(<Harness store={store} driverKey={DRIVER} unused={2} />);
    rerender(<Harness store={store} driverKey={DRIVER} unused={3} />);

    expect(uplot.builds).toHaveLength(1);
    expect(uplot.builds[0]!.destroy).not.toHaveBeenCalled();
  });

  /**
   * The other half of the same rule: the stream *is* keyed on, because another driver's rings are
   * different objects and repainting the old chart would draw one car's stint under another car's
   * name.
   */
  it('rebuilds against fresh rings when the driver changes', () => {
    const store = following();
    store.setFollowedDrivers([DRIVER, 'id:9']);
    const { rerender } = render(<Harness store={store} driverKey={DRIVER} />);

    rerender(<Harness store={store} driverKey="id:9" />);

    expect(uplot.builds).toHaveLength(2);
    expect(uplot.builds[0]!.destroy).toHaveBeenCalled();
  });

  /**
   * A channel the simulator did not report has to reach uPlot as null, because that is the only
   * value it treats as a break in the line. NaN is drawn as a number and silently bridged, which
   * is a confident line through data nobody captured.
   */
  it('hands unreported samples over as gaps rather than as numbers', async () => {
    const store = following();
    store.apply(focusFrame({ throttle: 1 }));
    store.apply(focusFrame({ throttle: null }));
    store.apply(focusFrame({ throttle: 0.5 }));

    render(<Harness store={store} driverKey={DRIVER} />);
    await paint();

    const [, throttle] = uplot.builds[0]!.setData.mock.lastCall![0] as (number | null)[][];
    expect(throttle).toEqual([1, null, 0.5]);
  });

  /**
   * A partially filled ring is right-aligned against the fixed x range, so a stream five seconds
   * old occupies the last sixth of a thirty-second window instead of being stretched across it.
   */
  it('right-aligns a partly filled ring against the full window', async () => {
    const store = following();
    store.apply(focusFrame());
    store.apply(focusFrame());

    render(<Harness store={store} driverKey={DRIVER} />);
    await paint();

    const [xs] = uplot.builds[0]!.setData.mock.lastCall![0] as [Float64Array];
    expect(Array.from(xs)).toEqual([TRACE_CAPACITY - 2, TRACE_CAPACITY - 1]);
  });

  /**
   * Rings can advance far more slowly than the screen refreshes, so a loop that repainted every
   * frame would copy them into uPlot dozens of times over for data that had not changed.
   */
  it('repaints only when the rings have actually advanced', async () => {
    const store = following();
    store.apply(focusFrame());

    render(<Harness store={store} driverKey={DRIVER} />);
    await paint();

    const settled = uplot.builds[0]!.setData.mock.calls.length;
    await paint();
    expect(uplot.builds[0]!.setData.mock.calls.length).toBe(settled);

    store.apply(focusFrame());
    await paint();
    expect(uplot.builds[0]!.setData.mock.calls.length).toBe(settled + 1);
  });

  /** A chart left painting after its panel closed would keep a dead driver's loop alive forever. */
  it('stops painting and destroys the chart on unmount', async () => {
    const store = following();
    const { unmount } = render(<Harness store={store} driverKey={DRIVER} />);
    await paint();

    unmount();
    const settled = uplot.builds[0]!.setData.mock.calls.length;
    await paint();

    expect(uplot.builds[0]!.destroy).toHaveBeenCalled();
    expect(uplot.builds[0]!.setData.mock.calls.length).toBe(settled);
  });
});
