import { useEffect, useRef } from 'react';
import uPlot from 'uplot';
import 'uplot/dist/uPlot.min.css';
import type { LiveStore, TraceBuffer } from '../../shared/live/store';
import { TRACE_COLOURS } from './traceColours';

/** One line on the chart, and the ring it is drawn from. */
export interface LiveChartSeries {
  label: string;
  stroke: string;
  /**
   * Which named y scale this line belongs to, where the chart has more than one.
   *
   * Omitted puts it on uPlot's default `y`, which is what a chart whose lines share units wants —
   * four tyre temperatures belong on one axis precisely so the gap between them is visible.
   */
  scale?: string;
  width?: number;
  /** An area under the line, where one channel has to be told apart from another by shape. */
  fill?: string;
  /**
   * Resolves the ring this line draws, at the moment the chart is built.
   *
   * A function rather than the buffer itself because the rings for a driver are replaced when that
   * driver is unfollowed and followed again, and a spec holding the old object would go quietly
   * flat. Called once per chart build, never per frame — see the remarks on the component.
   */
  buffer: () => TraceBuffer;
}

/**
 * Everything about a chart except which stream it is showing.
 *
 * Deliberately data rather than props: it is held in a ref and read only when the chart is built,
 * which is what lets a caller write it inline without destroying the chart on every render. See
 * the component's remarks.
 */
export interface LiveChartSpec {
  /**
   * The ring capacity, which is also the x range.
   *
   * Fixed rather than fitted to what has arrived: a five-second-old stream should occupy the first
   * sixth of a thirty-second window, not be stretched across the whole plot and then appear to
   * shrink as the session goes on.
   */
  capacity: number;
  /** The y scales, by name. The x scale belongs to the chart and cannot be set here. */
  scales?: Record<string, uPlot.Scale>;
  /** Which scale the one visible axis is drawn against. Omitted uses uPlot's default. */
  axis?: { scale?: string };
  series: readonly LiveChartSeries[];
}

interface LiveChartProps {
  /**
   * The stream being drawn, as an identity rather than as a source.
   *
   * The rings themselves are reached through each series' `buffer`, so neither of these is read for
   * data. They are here because they say *when the rings become different rings* — a chart pointed
   * at another driver is drawing another car's stint and has to be rebuilt rather than repainted.
   */
  store: LiveStore;
  driverKey: string;
  spec: LiveChartSpec;
  height?: number;
  className?: string;
}

/**
 * A line chart fed by ring buffers, painted outside React.
 *
 * **This component renders once.** After mount, nothing here goes through React again — a
 * `requestAnimationFrame` loop reads the rings and hands them to uPlot. Sixty React renders a
 * second would drop frames on a laptop long before the canvas did, and the traces are the one thing
 * on screen where dropped frames are visible as a stutter. uPlot rather than an SVG chart library
 * for the same reason: a thousand points per series in the DOM is a layout cost per frame, whereas
 * a canvas redraw is one.
 *
 * The x axis is sample index, not wall clock. The rings are a rolling window of the last N samples,
 * and a time axis would need the capture timestamps evenly spaced, which a poll on a busy machine
 * is not.
 *
 * ### Why the spec is held in a ref
 *
 * Every chart before this one was built in an effect keyed on the caller's own functions and
 * arrays, which meant a caller passing an inline arrow function tore the chart down and rebuilt it
 * on every render of its parent. That was survivable only because a comment told every caller to
 * hoist those functions to module scope — a rule enforced by nothing, which three separate
 * components then had to remember.
 *
 * Holding the spec in a ref and keying the effect on the stream alone removes the rule rather than
 * restating it. The spec is read when the chart is built and never compared, so an inline object
 * literal is as safe as a hoisted constant, and a caller cannot get this wrong any more.
 *
 * The cost is that changing the *shape* of a chart in place does not restyle it: a spec whose
 * series or scales differ takes effect the next time the chart is built. That is the right trade
 * for these charts, whose shape is a property of the channel and never changes for the life of a
 * panel.
 */
export function LiveChart({
  store,
  driverKey,
  spec,
  height = 112,
  className = 'trace',
}: LiveChartProps) {
  const containerRef = useRef<HTMLDivElement>(null);

  // Written after every render, read only when a chart is built.
  //
  // Deliberately the first effect in the component and deliberately without a dependency array.
  // React runs every cleanup for a commit before it runs any setup, and runs the setups in
  // declaration order — so on a driver change this has already replaced the spec by the time the
  // effect below builds the new chart from it. Writing it during render would be simpler and is
  // what `react-hooks/refs` exists to prevent.
  const specRef = useRef(spec);
  useEffect(() => {
    specRef.current = spec;
  });

  useEffect(() => {
    const container = containerRef.current;
    if (container === null) {
      return;
    }

    const { capacity, scales, axis, series } = specRef.current;

    // Resolved once, then read every frame. The rings for a driver outlive every paint, so looking
    // them up per frame would be a map probe sixty times a second for an answer that never changes.
    const buffers = series.map((entry) => entry.buffer());

    const chart = new uPlot(
      {
        width: container.clientWidth,
        height,
        scales: {
          x: { time: false, range: [0, capacity - 1] },
          ...scales,
        },
        axes: [
          { show: false },
          {
            ...(axis?.scale === undefined ? {} : { scale: axis.scale }),
            stroke: TRACE_COLOURS.axis,
            grid: { stroke: TRACE_COLOURS.grid },
          },
        ],
        legend: { show: false },
        cursor: { show: false },
        series: [
          {},
          // spanGaps: false throughout, and not negotiable per series. The rings hold NaN for a
          // channel the simulator did not report, and a bridged gap would draw a confident line
          // through data that does not exist.
          ...series.map((entry) => ({
            label: entry.label,
            stroke: entry.stroke,
            width: entry.width ?? 1.5,
            spanGaps: false,
            ...(entry.scale === undefined ? {} : { scale: entry.scale }),
            ...(entry.fill === undefined ? {} : { fill: entry.fill }),
          })),
        ],
      },
      [new Float64Array(0), ...series.map(() => new Float64Array(0))],
      container,
    );

    // Reused across frames so the paint loop allocates nothing. Resized only when the window the
    // buffers cover actually grows, which stops once the rings are full.
    //
    // Plain arrays for the channels, not typed ones: a missing reading has to reach uPlot as null
    // to draw as a gap, and a Float64Array cannot hold one — see `TraceBuffer.toNullableArray`.
    // The x axis stays typed, because a sample index is never absent.
    let xs = new Float64Array(0);
    let columns: (number | null)[][] = series.map(() => []);

    let frame = 0;

    // Rings can be pushed far more slowly than the screen refreshes — the tyre channels advance
    // once a second against sixty animation frames — so repainting regardless would copy every
    // ring into uPlot fifty-nine times more often than the data changes. -1 never matches a
    // version, so the first frame after mount still paints even when the rings are empty.
    let paintedVersion = -1;

    const paint = () => {
      // The first series speaks for all of them. Every line on one chart shares an x axis, which is
      // only meaningful if they share a sample index — so they are pushed together, and one
      // version describes the lot.
      const version = buffers[0]?.version ?? 0;

      if (version !== paintedVersion) {
        const count = buffers[0]?.length ?? 0;

        if (count !== xs.length) {
          xs = new Float64Array(count);
          for (let i = 0; i < count; i++) {
            xs[i] = capacity - count + i;
          }
        }

        columns = columns.map((column, index) => buffers[index]!.toNullableArray(column));

        chart.setData([xs, ...columns], true);
        paintedVersion = version;
      }

      frame = requestAnimationFrame(paint);
    };

    frame = requestAnimationFrame(paint);

    // Observed rather than listening for window resizes, because the container is what actually
    // decides the width: a chart in a panel that reflows — a sibling appearing, a grid column
    // changing, a widget dragged wider — gets no window event at all and would sit at its mount
    // width for the life of the session.
    const observer = new ResizeObserver(() => {
      chart.setSize({ width: container.clientWidth, height });
      // Otherwise a slow channel sits at the old width until its next sample happens to arrive and
      // the version guard above lets a repaint through, which for a tyre is up to a second.
      paintedVersion = -1;
    });
    observer.observe(container);

    return () => {
      cancelAnimationFrame(frame);
      observer.disconnect();
      chart.destroy();
    };
  }, [store, driverKey, height]);

  return <div ref={containerRef} className={className} />;
}
