import { useEffect, useRef } from 'react';
import uPlot from 'uplot';
import 'uplot/dist/uPlot.min.css';
import type { LiveStore, TraceBuffer } from '../../shared/live/store';
import { TRACE_COLOURS } from './traceColours';

/**
 * The default for `hidden`, hoisted so a chart with nothing to toggle is not handed a fresh array
 * on every render — which would re-run the visibility effect forever.
 */
const EMPTY_HIDDEN: readonly string[] = [];

function NOOP(): void {
  // Stands in until a chart exists to repaint. See `repaintRef`.
}

/** One line on the chart, and the ring it is drawn from. */
export interface LiveChartSeries {
  /**
   * The channel id this line draws, where the caller lets the user turn channels off.
   *
   * Matched against {@link LiveChartProps.hidden}. Omitted on a chart with nothing to toggle, which
   * is why it is not simply the array index: an id survives a series being reordered, and it is what
   * a saved wall remembers.
   */
  id?: string;
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
  /**
   * Channel ids not to draw, matched against each series' `id`.
   *
   * A prop rather than part of the spec, and deliberately so: the spec is read once when the chart
   * is built, whereas this changes while the user is looking at the chart. Applied in place through
   * uPlot's `setSeries`, so turning a corner off does not rebuild the chart and flash away the
   * fifteen minutes of stint already drawn.
   */
  hidden?: readonly string[];
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
  hidden = EMPTY_HIDDEN,
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

  // Read by the paint loop, which is not a React consumer and must not become one — sixty renders a
  // second is the cost this whole component exists to avoid. The visibility effect below writes it
  // and then forces one repaint, which is the entire mechanism.
  const hiddenRef = useRef<ReadonlySet<string>>(new Set(hidden));

  // Kept so the visibility effect can reach the chart the build effect made. Cleared on teardown,
  // so a toggle arriving between a driver change and the next build has nothing to talk to rather
  // than a destroyed instance.
  const chartRef = useRef<uPlot | null>(null);
  const repaintRef = useRef<() => void>(NOOP);

  // A string rather than the array itself, because a caller rebuilding `['fl']` on every render
  // would otherwise re-run the visibility effect forever. Joined on a character no id contains, so
  // two ids cannot be spelled to collide with a third.
  const hiddenKey = hidden.join(',');

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
            // Applied at build time as well as through `setSeries` below, so a chart rebuilt for
            // another driver comes back with the same corners turned off. Without it, changing car
            // would quietly undo the narrowing the user set up.
            show: entry.id === undefined || !hiddenRef.current.has(entry.id),
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
    const columns: (number | null)[][] = series.map(() => []);

    /*
     * What a hidden channel is handed instead of its ring.
     *
     * uPlot wants a column per series whatever their state, so a hidden one still needs an array as
     * long as the x axis — but it need not be *its* array, and building that is the cost worth
     * avoiding. Copying a ring is O(capacity) per series per repaint, and the arrangement this
     * feature exists for is exactly the expensive one: a wall of four-channel tiles narrowed to one
     * corner each, where three quarters of the copying draws nothing.
     *
     * One array shared by every hidden series on the chart, rebuilt only when the window grows.
     * Nulls rather than zeroes because uPlot excludes a hidden series from its scale calculation
     * anyway, and a column of zeroes would be a trap for the day that stops being true.
     */
    let blanks: null[] = [];

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

          blanks = new Array<null>(count).fill(null);
        }

        // The scratch arrays stay per series and keep their identity across frames, so re-showing a
        // channel finds its buffer where it left it. Only what is handed to uPlot switches to the
        // shared blank — writing the blank into `columns` would let the next `toNullableArray` fill
        // the array every other hidden series is also using.
        const drawn: (number | null)[][] = [];

        for (let index = 0; index < buffers.length; index++) {
          const id = series[index]!.id;

          if (id !== undefined && hiddenRef.current.has(id)) {
            drawn.push(blanks);
            continue;
          }

          columns[index] = buffers[index]!.toNullableArray(columns[index]);
          drawn.push(columns[index]!);
        }

        chart.setData([xs, ...drawn], true);
        paintedVersion = version;
      }

      frame = requestAnimationFrame(paint);
    };

    chartRef.current = chart;
    // Lets the visibility effect force the next frame to redraw. A toggle changes what should be on
    // screen without advancing any ring, and the version guard above would otherwise hold the old
    // picture until the next sample happened to arrive — up to a second for a tyre channel.
    repaintRef.current = () => {
      paintedVersion = -1;
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
      chartRef.current = null;
      repaintRef.current = NOOP;
    };
  }, [store, driverKey, height]);

  /*
   * Turns channels on and off in place.
   *
   * Declared after the build effect and that ordering is load bearing: React runs setups in
   * declaration order, so on a driver change the chart below has already been rebuilt — with the
   * right channels hidden from the start — by the time this runs against it.
   *
   * `setSeries` rather than a rebuild, because a rebuild would throw away the drawn window. On a
   * tyre chart that is fifteen minutes of stint disappearing and slowly redrawing because somebody
   * wanted to look at one corner, which would make the feature not worth using.
   */
  useEffect(() => {
    hiddenRef.current = new Set(hiddenKey === '' ? [] : hiddenKey.split(','));

    const chart = chartRef.current;
    if (chart === null) {
      return;
    }

    specRef.current.series.forEach((entry, index) => {
      if (entry.id === undefined) {
        return;
      }

      // uPlot's series are one-based to the caller: index 0 is the x axis.
      chart.setSeries(index + 1, { show: !hiddenRef.current.has(entry.id) });
    });

    repaintRef.current();
  }, [hiddenKey, store, driverKey]);

  return <div ref={containerRef} className={className} />;
}
