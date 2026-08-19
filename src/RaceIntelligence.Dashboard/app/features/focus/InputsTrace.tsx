import uPlot from 'uplot';
import { TRACE_CAPACITY, type LiveStore } from '../../shared/live/store';
import { ChannelLegend, type LegendChannel } from './ChannelLegend';
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

/**
 * Where the assist markers sit, as a fraction of the plot's height.
 *
 * The two flags get their own scales rather than being shaded across the pedals, because a band
 * behind the trace is a band *through* the trace: it dims the two lines a coach is reading at
 * exactly the moment they are most worth reading, which is under braking. Two thin lanes near the
 * foot of the plot say the same thing and take nothing away from what is above them.
 *
 * A reported value of 1 lands at a sixteenth of the height for ABS and an eighth for traction
 * control, so the two markers occupy separate lanes and can never be mistaken for each other. Both
 * read the same three-valued ring — raised in its lane while intervening, flat on the floor while
 * watched and quiet, and a gap where the simulator never said.
 */
const ABS_LANE_RANGE: [number, number] = [0, 16];
const TRACTION_LANE_RANGE: [number, number] = [0, 8];

/**
 * Gear is a staircase, not a slope.
 *
 * Interpolating between third and fourth draws a car passing through 3.5, which no gearbox does.
 * The step also makes the shift *point* readable, which is the thing being looked for — a rounded
 * corner hides exactly where the change happened.
 *
 * Resolved defensively because `uPlot.paths` is a bundle of optional path builders rather than a
 * guaranteed shape, and a missing one should cost a step, not the whole chart.
 */
const GEAR_PATHS = uPlot.paths.stepped?.({ align: 1 });

/**
 * The channels the inputs trace draws.
 *
 * All nine are on by default, and that is deliberate for an overlay of this kind: the reason to put
 * them on one plot rather than nine is reading a shift against a lift against a steering input, and
 * a chart that opened mostly blank would make somebody rebuild the very thing they asked for. The
 * toggles are how it narrows — the direction that costs one click rather than eight.
 *
 * Ordered pedals first, then the car's own channels, then the assists. That is roughly the order
 * they get switched off in, so the legend reads the way it is used.
 */
export const INPUT_CHANNELS: readonly LegendChannel[] = [
  { id: 'throttle', label: 'Throttle', stroke: TRACE_COLOURS.throttle },
  { id: 'brake', label: 'Brake', stroke: TRACE_COLOURS.brake },
  { id: 'clutch', label: 'Clutch', stroke: TRACE_COLOURS.clutch },
  { id: 'steering', label: 'Steering', stroke: TRACE_COLOURS.steering },
  { id: 'speed', label: 'Speed', stroke: TRACE_COLOURS.speed },
  { id: 'gear', label: 'Gear', stroke: TRACE_COLOURS.gear },
  { id: 'rpm', label: 'RPM', stroke: TRACE_COLOURS.rpm },
  { id: 'abs', label: 'ABS', stroke: TRACE_COLOURS.abs },
  { id: 'tc', label: 'TC', stroke: TRACE_COLOURS.tractionControl },
];

interface InputsTraceProps {
  store: LiveStore;
  /** Which driver's stream to plot. */
  driverKey: string;
  /** Channel ids this placement has turned off, and where a click on the legend goes. */
  hiddenChannels: readonly string[];
  onToggleChannel: (channelId: string) => void;
  height?: number;
}

/**
 * Everything the driver and the car were doing, over the last thirty seconds, on one plot.
 *
 * The first entry in the telemetry backlog and the one rated highest, because a lap is explained by
 * how its channels line up rather than by any one of them alone: where the throttle lifts relative
 * to where the brake comes in, whether the shift landed before or after turn-in, whether the speed
 * that went missing was lost braking or scrubbed off in the corner. Nine channels is dense, and the
 * density is the point — reading one question at a time is what the toggles are for.
 *
 * **Scales, not normalisation.** Speed, gear and RPM keep their own axes rather than being squeezed
 * onto a shared 0..1, because a normalised channel silently rescales as the session goes on: the
 * same 180 km/h sits at a different height the moment somebody goes faster, and two laps stop being
 * comparable by eye. Only one axis is ever drawn and it is the pedals' — the rest are read as
 * shapes against it, which is how a trace is read anyway.
 *
 * **The assists are here for free.** Both flags already ride on the focus frame; nothing is derived,
 * no wheel diameter is assumed, no slip is computed. And they change what the brake trace means: a
 * lock-up and a car that saved itself are the same pedal pressure, and differ entirely once the
 * marker underneath says whether the system was doing anything.
 *
 * The painting is `LiveChart`'s — see there for why none of this goes through React after mount.
 */
export function InputsTrace({
  store,
  driverKey,
  hiddenChannels,
  onToggleChannel,
  height = 180,
}: InputsTraceProps) {
  // `tracesFor` creates the rings on demand, so it is reached from inside the buffer closures
  // rather than here: those are called when the chart is built, which keeps a store write out of a
  // render pass. The widget routinely mounts before the driver's first frame has arrived, which is
  // exactly why the store creates on demand in the first place.
  const spec: LiveChartSpec = {
    capacity: TRACE_CAPACITY,
    scales: {
      pedal: { range: [0, 1] },
      steer: { range: [-1, 1] },
      // Left to fit what arrived. Unlike a pedal these have no natural bounds — a GT car and a
      // formula car share neither a top speed nor a rev limit — and pinning a range here would be
      // the dashboard inventing one on their behalf.
      speed: {},
      gear: {},
      rpm: {},
      abs: { range: ABS_LANE_RANGE },
      tc: { range: TRACTION_LANE_RANGE },
    },
    axis: { scale: 'pedal' },
    series: [
      {
        id: 'throttle',
        label: 'Throttle',
        scale: 'pedal',
        stroke: TRACE_COLOURS.throttle,
        buffer: () => store.tracesFor(driverKey).throttle,
      },
      {
        id: 'brake',
        label: 'Brake',
        scale: 'pedal',
        stroke: TRACE_COLOURS.brake,
        fill: BRAKE_FILL,
        buffer: () => store.tracesFor(driverKey).brake,
      },
      {
        id: 'clutch',
        label: 'Clutch',
        scale: 'pedal',
        stroke: TRACE_COLOURS.clutch,
        buffer: () => store.tracesFor(driverKey).clutch,
      },
      {
        id: 'steering',
        label: 'Steering',
        scale: 'steer',
        stroke: TRACE_COLOURS.steering,
        width: 1,
        buffer: () => store.tracesFor(driverKey).steering,
      },
      {
        id: 'speed',
        label: 'Speed',
        scale: 'speed',
        stroke: TRACE_COLOURS.speed,
        buffer: () => store.tracesFor(driverKey).speed,
      },
      {
        id: 'gear',
        label: 'Gear',
        scale: 'gear',
        stroke: TRACE_COLOURS.gear,
        width: 1,
        ...(GEAR_PATHS === undefined ? {} : { paths: GEAR_PATHS }),
        buffer: () => store.tracesFor(driverKey).gear,
      },
      {
        id: 'rpm',
        label: 'RPM',
        scale: 'rpm',
        stroke: TRACE_COLOURS.rpm,
        width: 1,
        buffer: () => store.tracesFor(driverKey).engineRpm,
      },
      {
        id: 'abs',
        label: 'ABS',
        scale: 'abs',
        stroke: TRACE_COLOURS.abs,
        width: 2,
        buffer: () => store.tracesFor(driverKey).absActive,
      },
      {
        id: 'tc',
        label: 'TC',
        scale: 'tc',
        stroke: TRACE_COLOURS.tractionControl,
        width: 2,
        buffer: () => store.tracesFor(driverKey).tractionControlActive,
      },
    ],
  };

  return (
    <div className="wheel-chart">
      <LiveChart
        store={store}
        driverKey={driverKey}
        spec={spec}
        hidden={hiddenChannels}
        height={height}
        className="trace"
      />

      {/* No readouts here: the car and pedal tiles beside this on the wall already carry the
          numbers, and a second set under the trace would be the same values twice. */}
      <ChannelLegend channels={INPUT_CHANNELS} hidden={hiddenChannels} onToggle={onToggleChannel} />
    </div>
  );
}
