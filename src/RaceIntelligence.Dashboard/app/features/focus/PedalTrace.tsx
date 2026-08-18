import { TRACE_CAPACITY, type LiveStore } from '../../shared/live/store';
import { LiveChart, type LiveChartSpec } from './LiveChart';
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
 * Throttle, brake, clutch and steering over the last thirty seconds.
 *
 * Two scales rather than one, because steering runs from lock to lock through zero and the pedals
 * run from nothing to everything. Sharing an axis would put a straight-ahead wheel in the middle of
 * the pedal range and a released pedal at full left lock, which is worse than two axes and reads
 * like a fault.
 *
 * The painting is `LiveChart`'s — see there for why none of this goes through React after mount.
 */
export function PedalTrace({ store, driverKey, height = 140 }: PedalTraceProps) {
  // `tracesFor` creates the rings on demand, so it is reached from inside the buffer closures
  // rather than here: those are called when the chart is built, which keeps a store write out of a
  // render pass. The panel routinely mounts before the driver's first frame has arrived, which is
  // exactly why the store creates on demand in the first place.
  const spec: LiveChartSpec = {
    capacity: TRACE_CAPACITY,
    scales: { pedal: { range: [0, 1] }, steer: { range: [-1, 1] } },
    axis: { scale: 'pedal' },
    series: [
      {
        label: 'Throttle',
        scale: 'pedal',
        stroke: TRACE_COLOURS.throttle,
        buffer: () => store.tracesFor(driverKey).throttle,
      },
      {
        label: 'Brake',
        scale: 'pedal',
        stroke: TRACE_COLOURS.brake,
        fill: BRAKE_FILL,
        buffer: () => store.tracesFor(driverKey).brake,
      },
      {
        label: 'Clutch',
        scale: 'pedal',
        stroke: TRACE_COLOURS.clutch,
        buffer: () => store.tracesFor(driverKey).clutch,
      },
      {
        label: 'Steering',
        scale: 'steer',
        stroke: TRACE_COLOURS.steering,
        width: 1,
        buffer: () => store.tracesFor(driverKey).steering,
      },
    ],
  };

  return (
    <LiveChart store={store} driverKey={driverKey} spec={spec} height={height} className="trace" />
  );
}
