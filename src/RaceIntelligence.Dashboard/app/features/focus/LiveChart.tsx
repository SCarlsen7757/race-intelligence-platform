import { useEffect, useRef } from 'react';
import uPlot from 'uplot';
import 'uplot/dist/uPlot.min.css';
import type { LiveStore } from '../../shared/live/store';
import { TRACE_COLOURS } from './traceColours';

/**
 * The default for `hidden`, hoisted so a chart with nothing to toggle is not handed a fresh array
 * on every render — which would re-run the visibility effect forever.
 */
const EMPTY_HIDDEN: readonly string[] = [];

function NOOP(): void {
  // Stands in until a chart exists to repaint. See `repaintRef`.
}

/**
 * Where one line's numbers come from.
 *
 * Structural rather than `TraceBuffer` itself, and deliberately the smallest surface that satisfies
 * the paint loop: a version to compare, a length to size the x axis, and a way to read the values
 * into a reused array. `TraceBuffer` already answers all three, so every existing chart satisfies
 * this without changing.
 *
 * The reason to name it at all is the delta chart, whose numbers are not a ring: they are a lap
 * measured against another lap, recomputed bin by bin rather than pushed sample by sample. That is a
 * different shape of data behind an identical chart, and the alternative — a second copy of the
 * uPlot lifecycle for it — is exactly what this component exists to prevent.
 */
export interface LiveChartSource {
  /** Changes when the numbers change. The paint loop skips a repaint while it has not. */
  readonly version: number;
  readonly length: number;
  /** Reads the values out, into the caller's array where it is the right size. */
  toNullableArray(into?: (number | null)[]): (number | null)[];
}

/** One line on the chart, and the source it is drawn from. */
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
   * How the points are joined, where a straight line between them would be a lie.
   *
   * Gear is the case this exists for: interpolating between third and fourth draws a car passing
   * through 3.5, and rounds off the very instant — the shift point — somebody is looking for.
   */
  paths?: uPlot.Series.PathBuilder;
  /**
   * Resolves the ring this line draws, at the moment the chart is built.
   *
   * A function rather than the buffer itself because the rings for a driver are replaced when that
   * driver is unfollowed and followed again, and a spec holding the old object would go quietly
   * flat. Called once per chart build, never per frame — see the remarks on the component.
   */
  buffer: () => LiveChartSource;
}

/**
 * The simulator's own operating window, drawn behind the lines.
 *
 * **This is the one thing on these charts the dashboard does not have to be clever about.** A tread
 * or brake temperature means nothing without the range it belongs in — 380 °C is cold on one car
 * and cooking on another — and rather than inferring that from the data, the game simply says. The
 * band turns "84 °C, climbing" into "eight degrees below the window, climbing" for somebody who has
 * never driven the car.
 *
 * Read per draw rather than captured at build time, because the window arrives on a frame and the
 * chart is built before the first one lands. Returning null draws nothing at all: a simulator that
 * does not report a window gets no band, never a plausible-looking one invented from a nominal
 * value, which would be a reading the chart made up.
 */
export interface LiveChartBand {
  /** The window now, or null while the simulator has not reported one. Sentinels already resolved. */
  read: () => OperatingWindowValues | null;
  /** Which named y scale the window's numbers belong to. Omitted uses uPlot's default `y`. */
  scale?: string;
}

/**
 * A window's three numbers, with the simulator's sentinels already resolved to null.
 *
 * Separate from the wire's `OperatingWindow` because that type is what arrived and this is what
 * survived being checked: the extras document carries raw `-1`s, and a band drawn from those would
 * put a brake's optimum a degree below zero. Whoever builds one of these has run the numbers
 * through `reportedNumber`.
 */
export interface OperatingWindowValues {
  cold?: number | null;
  hot?: number | null;
  optimal?: number | null;
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
  /** The simulator's operating window, where it reports one. See {@link LiveChartBand}. */
  band?: LiveChartBand;
}

/**
 * Paints the operating window across the plot, under the lines.
 *
 * Returns the hook rather than being one, so the band it draws is captured once when the chart is
 * built while the *values* are still read on every draw — the window arrives on a frame, and the
 * chart is built before the first frame lands.
 *
 * Each of the three numbers is optional on its own, and they are drawn independently rather than
 * all-or-nothing: a simulator reporting an optimum but no bounds should get its line, and one
 * reporting bounds but no optimum should get its band. Requiring the full set would throw away two
 * thirds of a window because the third was missing.
 */
function drawBand(band: LiveChartBand): (chart: uPlot) => void {
  return (chart) => {
    const window = band.read();
    if (window === null) {
      return;
    }

    const scale = band.scale ?? 'y';
    const { ctx } = chart;
    const { left, top, width, height } = chart.bbox;

    ctx.save();
    // Clipped to the plot area. `valToPos` happily returns a coordinate outside it for a window the
    // current data does not reach — a tyre thirty degrees below its cold threshold puts the band
    // off the top of the chart — and without a clip that would paint over the axis and the panel.
    ctx.beginPath();
    ctx.rect(left, top, width, height);
    ctx.clip();

    const { cold, hot, optimal } = window;

    if (cold !== null && cold !== undefined && hot !== null && hot !== undefined) {
      // Hot is the larger number and so the *smaller* y, canvas coordinates running downwards.
      // Taken as min/max rather than assumed, because a simulator reporting them the other way
      // round should still get a band rather than one of negative height, which draws nothing.
      const first = chart.valToPos(cold, scale, true);
      const second = chart.valToPos(hot, scale, true);

      ctx.fillStyle = TRACE_COLOURS.window;
      ctx.fillRect(left, Math.min(first, second), width, Math.abs(first - second));
    }

    if (optimal !== null && optimal !== undefined) {
      const y = chart.valToPos(optimal, scale, true);

      ctx.strokeStyle = TRACE_COLOURS.windowEdge;
      ctx.lineWidth = 1;
      ctx.beginPath();
      // Half a pixel off, so a one-pixel line lands on a pixel instead of straddling two and
      // drawing as a two-pixel smear.
      ctx.moveTo(left, Math.round(y) + 0.5);
      ctx.lineTo(left + width, Math.round(y) + 0.5);
      ctx.stroke();
    }

    ctx.restore();
  };
}

interface LiveChartProps {
  /**
   * The stream being drawn, as an identity rather than as a source.
   *
   * The rings themselves are reached through each series' `buffer`, so neither of these is read for
   * data. They are here because they say *when the rings become different rings* — a chart pointed
   * at another driver is drawing another car's stint and has to be rebuilt rather than repainted.
   *
   * **Both are optional, because not every source is a stream.** A chart drawn from a stored lap has
   * numbers that were fixed before the page loaded: there is no later moment at which they become
   * different numbers, so its identity is the constant `undefined` and it is built exactly once.
   * Leaving these required would have meant handing such a chart a `LiveStore` it does not read, to
   * satisfy a dependency array — a prop passed to mean nothing, which is worse than an absent one.
   */
  store?: LiveStore;
  driverKey?: string;
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
  /**
   * The shortest the plot may be drawn, in pixels.
   *
   * A floor, not a height. **The height is whatever the container is** — the tile the user dragged
   * — and this only exists for the two moments the container cannot answer: the first frame after
   * mount, before layout has run, and a tile briefly collapsed by its own reflow. A uPlot instance
   * built at zero pixels draws nothing and does not always recover.
   */
  minHeight?: number;
  className?: string;
}

/**
 * How tall to draw, given a container.
 *
 * `clientHeight` rather than a prop, which is the whole of #88: every chart used to take a fixed
 * number of pixels chosen at its call site, so a tile dragged taller grew a band of empty space
 * under the legend instead of a taller trace.
 */
function plotHeight(container: HTMLElement, minHeight: number): number {
  return Math.max(minHeight, container.clientHeight);
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
  minHeight = 96,
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

    const { capacity, scales, axis, series, band } = specRef.current;

    // Resolved once, then read every frame. The rings for a driver outlive every paint, so looking
    // them up per frame would be a map probe sixty times a second for an answer that never changes.
    const buffers = series.map((entry) => entry.buffer());

    const chart = new uPlot(
      {
        width: container.clientWidth,
        height: plotHeight(container, minHeight),
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
        // `drawClear` fires after uPlot has cleared the canvas and before it draws any series, which
        // is the only hook that puts the band *behind* the lines. Drawn on top it would be a tinted
        // sheet over the measurements, which is the wrong way round: the window is the context and
        // the traces are the reading.
        plugins: band === undefined ? [] : [{ hooks: { drawClear: drawBand(band) } }],
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
            ...(entry.paths === undefined ? {} : { paths: entry.paths }),
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

    /*
     * Both dimensions, from the container, and only when they have actually changed.
     *
     * Observed rather than listening for window resizes, because the container is what actually
     * decides the size: a chart in a tile that reflows — a sibling appearing, a grid column
     * changing, a widget dragged wider or taller — gets no window event at all and would sit at its
     * mount size for the life of the session.
     *
     * The guard is not an optimisation. Resizing the canvas *inside* the box being observed is the
     * documented way to produce "ResizeObserver loop completed with undelivered notifications", and
     * a loop needs a change on every pass — so a `setSize` that runs only when the numbers differ
     * cannot sustain one even if a feedback path exists. Deferring to the next frame gets the write
     * out of the callback's synchronous path as well; the loop is already running, so this costs no
     * extra frame. See #79.
     */
    let sizedWidth = container.clientWidth;
    let sizedHeight = plotHeight(container, minHeight);
    let pendingResize = 0;

    const observer = new ResizeObserver(() => {
      if (pendingResize !== 0) {
        return;
      }

      pendingResize = requestAnimationFrame(() => {
        pendingResize = 0;

        const width = container.clientWidth;
        const nextHeight = plotHeight(container, minHeight);
        if (width === sizedWidth && nextHeight === sizedHeight) {
          return;
        }

        sizedWidth = width;
        sizedHeight = nextHeight;
        chart.setSize({ width, height: nextHeight });
        // Otherwise a slow channel sits at the old size until its next sample happens to arrive and
        // the version guard above lets a repaint through, which for a tyre is up to a second.
        paintedVersion = -1;
      });
    });
    observer.observe(container);

    return () => {
      cancelAnimationFrame(frame);
      cancelAnimationFrame(pendingResize);
      observer.disconnect();
      chart.destroy();
      chartRef.current = null;
      repaintRef.current = NOOP;
    };
    // `minHeight` is here because the effect reads it, and it is safe to key on because it is a
    // constant at every call site — a floor for the first frame, not a size. The *size* is never in
    // this array: a chart that rebuilt when the tile was dragged would throw away the drawn window,
    // which is the one thing this component's own remarks say a size change must not do.
  }, [store, driverKey, minHeight]);

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
