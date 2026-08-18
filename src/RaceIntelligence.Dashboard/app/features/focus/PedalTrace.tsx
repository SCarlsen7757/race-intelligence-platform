import { useEffect, useRef } from 'react';
import uPlot from 'uplot';
import 'uplot/dist/uPlot.min.css';
import type { LiveStore } from '../../shared/live/store';
import { TRACE_COLOURS } from './traceColours';

/* Derived from the channel colour rather than written out as a second red, so the fill cannot be
   left behind by a change to TRACE_COLOURS.brake — the trace and the bar for one pedal drifting
   apart is the whole reason those colours live in one module. */
function hexToRgba(hex: string, alpha: number) {
  const red = Number.parseInt(hex.slice(1, 3), 16);
  const green = Number.parseInt(hex.slice(3, 5), 16);
  const blue = Number.parseInt(hex.slice(5, 7), 16);

  return `rgba(${red}, ${green}, ${blue}, ${alpha})`;
}

/**
 * Brake is the one series with a fill, so throttle and brake are told apart by shape and not only
 * by hue. Green and red collapse toward the same muddy yellow-brown under deuteranopia, and these
 * are the two lines a coach spends the most time reading against each other — where the throttle
 * lifts relative to where the brake comes in is most of what a pedal trace is for. It is the same
 * argument the INV pill beside a struck-through lap time is there to make.
 *
 * An area also happens to be the better reading for everyone: trail-braking is a shape you can see
 * the size of, which two crossing lines do not show. At 28% the throttle stroke stays legible
 * where it crosses the fill, which it would not at the strength that makes brake unmissable alone.
 */
const BRAKE_FILL = hexToRgba(TRACE_COLOURS.brake, 0.28);

interface PedalTraceProps {
  store: LiveStore;
  /** Which driver's stream to plot. Two can be on screen at once. */
  driverKey: string;
  height?: number;
}

/**
 * Throttle, brake, clutch and steering over the last minute, painted at the display's refresh rate.
 *
 * **This component renders once.** After mount, nothing here goes through React again — a
 * `requestAnimationFrame` loop reads the store's ring buffers and hands them to uPlot. Sixty React
 * renders a second would drop frames on a laptop long before the canvas did, and the traces are
 * the one thing on screen where dropped frames are visible as a stutter.
 *
 * uPlot rather than an SVG chart library for the same reason: a thousand points per series in the
 * DOM is a layout cost per frame, whereas a canvas redraw is one.
 */
export function PedalTrace({ store, driverKey, height = 140 }: PedalTraceProps) {
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const container = containerRef.current;
    if (container === null) {
      return;
    }

    // Resolved once, then read every frame. The rings for a driver outlive every paint, so looking
    // them up per frame would be a map probe sixty times a second for an answer that never changes.
    const traces = store.tracesFor(driverKey);

    const chart = new uPlot(
      {
        width: container.clientWidth,
        height,
        // The x axis is sample index, not time: the traces are a rolling window of the last N
        // frames, and a wall-clock axis would need the capture timestamps to be evenly spaced,
        // which a 60 Hz poll on a busy machine is not.
        scales: { x: { time: false }, pedal: { range: [0, 1] }, steer: { range: [-1, 1] } },
        axes: [
          { show: false },
          {
            scale: 'pedal',
            stroke: TRACE_COLOURS.axis,
            grid: { stroke: TRACE_COLOURS.grid },
          },
        ],
        legend: { show: false },
        cursor: { show: false },
        // spanGaps: false throughout, because the store pushes NaN for a pedal the simulator did
        // not report. A bridged gap would draw a confident line through data that does not exist.
        series: [
          {},
          {
            label: 'Throttle',
            scale: 'pedal',
            stroke: TRACE_COLOURS.throttle,
            width: 1.5,
            spanGaps: false,
          },
          {
            label: 'Brake',
            scale: 'pedal',
            stroke: TRACE_COLOURS.brake,
            fill: BRAKE_FILL,
            width: 1.5,
            spanGaps: false,
          },
          {
            label: 'Clutch',
            scale: 'pedal',
            stroke: TRACE_COLOURS.clutch,
            width: 1.5,
            spanGaps: false,
          },
          {
            label: 'Steering',
            scale: 'steer',
            stroke: TRACE_COLOURS.steering,
            width: 1,
            spanGaps: false,
          },
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

    // Reused across frames so the paint loop allocates nothing. Resized only when the window the
    // buffers cover actually grows, which stops once the ring is full.
    //
    // Plain arrays for the channels, not typed ones: a missing pedal has to reach uPlot as null to
    // draw as a gap, and a Float64Array cannot hold one — see `TraceBuffer.toNullableArray`. The x
    // axis stays typed, because a sample index is never absent.
    let xs = new Float64Array(0);
    let throttle: (number | null)[] = [];
    let brake: (number | null)[] = [];
    let clutch: (number | null)[] = [];
    let steering: (number | null)[] = [];

    let frame = 0;

    const paint = () => {
      const count = traces.throttle.length;

      if (count !== xs.length) {
        xs = new Float64Array(count);
        for (let i = 0; i < count; i++) {
          xs[i] = i;
        }
      }

      throttle = traces.throttle.toNullableArray(throttle);
      brake = traces.brake.toNullableArray(brake);
      clutch = traces.clutch.toNullableArray(clutch);
      steering = traces.steering.toNullableArray(steering);

      chart.setData([xs, throttle, brake, clutch, steering], true);
      frame = requestAnimationFrame(paint);
    };

    frame = requestAnimationFrame(paint);

    const resize = () => chart.setSize({ width: container.clientWidth, height });
    window.addEventListener('resize', resize);

    return () => {
      cancelAnimationFrame(frame);
      window.removeEventListener('resize', resize);
      chart.destroy();
    };
  }, [store, driverKey, height]);

  return <div ref={containerRef} className="trace" />;
}
