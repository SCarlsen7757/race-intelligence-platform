import { useEffect, useRef } from 'react';
import uPlot from 'uplot';
import 'uplot/dist/uPlot.min.css';
import type { FocusFrameMessage } from '../../shared/live/contracts';
import { WHEELS } from '../../shared/live/contracts';
import type { LiveStore, TyreTraces, WheelTraces } from '../../shared/live/store';
import { LiveReadout } from '../../shared/ui/LiveReadout';
import { TRACE_COLOURS, WHEEL_COLOURS } from './traceColours';

interface WheelTraceProps {
  store: LiveStore;
  driverKey: string;
  /** Which of the tyre rings to plot. */
  channel: (tyres: TyreTraces) => WheelTraces;
  /** The same channel read off a frame, for the current-value labels. */
  read: (frame: FocusFrameMessage, wheel: number) => number | null | undefined;
  format: (value: number | null | undefined) => string;
  unit: string;
  /**
   * A fixed y range, where the channel has one.
   *
   * Wear is a fraction and belongs on 0..1, so a stint that has barely worn the tyres draws as the
   * flat line it is rather than being auto-scaled into a dramatic-looking slope. Pressure and
   * temperature have no natural bounds and are left to scale to what arrived.
   */
  range?: readonly [number, number];
  height?: number;
}

/**
 * One tyre channel over a stint, four wheels on one axis, with the current value beside each.
 *
 * **A number says where a tyre is; the line says where it is going**, and mid-stint the second is
 * the question actually being asked. Both are here rather than in two panels because they answer it
 * together — the label is the line's newest point, read out of the same store, so they cannot
 * disagree.
 *
 * Four wheels share one axis on purpose. Divergence is the signal for every one of these channels:
 * a left front climbing away from the right front is a car that is about to understeer, and that is
 * visible as a gap between two lines and invisible in four separate charts.
 *
 * **This component renders once.** Like `PedalTrace`, everything after mount is a
 * `requestAnimationFrame` loop handing ring buffers to uPlot, and the labels are `LiveReadout`s
 * writing `textContent` from their own loop. No React render happens per frame.
 *
 * The rings behind it are the slow ones — see `TYRE_SAMPLE_INTERVAL_MS`. Plotting tyres on the
 * pedals' sixty-second window would show a flat line and call it information.
 */
export function WheelTrace({
  store,
  driverKey,
  channel,
  read,
  format,
  unit,
  range,
  height = 112,
}: WheelTraceProps) {
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const container = containerRef.current;
    if (container === null) {
      return;
    }

    const wheels = channel(store.tracesFor(driverKey).tyres);

    const chart = new uPlot(
      {
        width: container.clientWidth,
        height,
        // Sample index, not wall clock, for the same reason the pedal trace uses one: the rings are
        // a rolling window of samples and a time axis would need them evenly spaced, which a poll
        // on a busy machine is not.
        scales: { x: { time: false }, y: range === undefined ? {} : { range: [...range] } },
        axes: [
          { show: false },
          { stroke: TRACE_COLOURS.axis, grid: { stroke: TRACE_COLOURS.grid } },
        ],
        legend: { show: false },
        cursor: { show: false },
        series: [
          {},
          // spanGaps: false throughout. Tyre values are nullable on the wire and the store pushes
          // NaN for an unreported wheel, so a hole stays a hole: bridging it would draw a confident
          // line through a reading that was never taken.
          ...WHEELS.map((wheel, index) => ({
            label: wheel,
            stroke: WHEEL_COLOURS[index]!,
            width: 1.5,
            spanGaps: false,
          })),
        ],
      },
      [
        new Float64Array(0),
        new Float64Array(0),
        new Float64Array(0),
        new Float64Array(0),
        new Float64Array(0),
      ],
      container,
    );

    // Reused across frames so the paint loop allocates nothing, exactly as the pedal trace does.
    //
    // Plain arrays for the wheels, not typed ones: an unreported wheel has to reach uPlot as null
    // to draw as a gap, and a Float64Array cannot hold one — see `TraceBuffer.toNullableArray`.
    // Drawn at zero instead, a missing pressure reads as a flat tyre. The x axis stays typed,
    // because a sample index is never absent.
    let xs = new Float64Array(0);
    const series: (number | null)[][] = [[], [], [], []];

    let frame = 0;

    // Tyre rings are pushed once a second (TYRE_SAMPLE_INTERVAL_MS), but requestAnimationFrame
    // runs up to sixty times a second, so redrawing on every frame copies four full rings into
    // uPlot fifty-nine times more often than the data changes. -1 never matches a version, so the
    // first frame after mount still paints even when the ring is empty.
    let paintedVersion = -1;

    const paint = () => {
      // Every wheel is pushed in the same call, so one version describes all four.
      const version = wheels[0].version;

      if (version !== paintedVersion) {
        const count = wheels[0].length;

        if (count !== xs.length) {
          xs = new Float64Array(count);
          for (let i = 0; i < count; i++) {
            xs[i] = i;
          }
        }

        for (let wheel = 0; wheel < series.length; wheel++) {
          series[wheel] = wheels[wheel]!.toNullableArray(series[wheel]);
        }

        chart.setData([xs, series[0]!, series[1]!, series[2]!, series[3]!], true);
        paintedVersion = version;
      }

      frame = requestAnimationFrame(paint);
    };

    frame = requestAnimationFrame(paint);

    const resize = () => {
      chart.setSize({ width: container.clientWidth, height });
      // Otherwise the chart sits at the old width for up to a second, until the next sample
      // happens to arrive and the version guard above lets a repaint through.
      paintedVersion = -1;
    };
    window.addEventListener('resize', resize);

    return () => {
      cancelAnimationFrame(frame);
      window.removeEventListener('resize', resize);
      chart.destroy();
    };
  }, [store, driverKey, channel, range, height]);

  return (
    <div className="wheel-chart">
      <div ref={containerRef} className="wheel-chart__plot" />

      <div className="wheel-chart__values">
        {WHEELS.map((wheel, index) => (
          <div key={wheel} className="wheel-chart__value">
            <span className="wheel-chart__key" style={{ background: WHEEL_COLOURS[index]! }} />
            <span className="wheel-chart__wheel">{wheel}</span>
            <LiveReadout
              store={store}
              driverKey={driverKey}
              className="wheel-chart__number"
              render={(liveFrame) => format(read(liveFrame, index))}
            />
          </div>
        ))}
        <span className="wheel-chart__unit">{unit}</span>
      </div>
    </div>
  );
}
