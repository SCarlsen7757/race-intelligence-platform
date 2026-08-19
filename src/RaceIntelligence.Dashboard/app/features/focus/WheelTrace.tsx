import type { FocusFrameMessage } from '../../shared/live/contracts';
import { WHEELS } from '../../shared/live/contracts';
import {
  TYRE_TRACE_CAPACITY,
  type LiveStore,
  type TyreTraces,
  type WheelTraces,
} from '../../shared/live/store';
import { LiveReadout } from '../../shared/ui/LiveReadout';
import { ChannelLegend, type LegendChannel } from './ChannelLegend';
import { LiveChart, type LiveChartSpec, type OperatingWindowValues } from './LiveChart';
import { WHEEL_COLOURS } from './traceColours';

/**
 * The four corners as channels, in the wire's FL, FR, RL, RR order.
 *
 * Lower-case ids rather than the labels, because an id is written into somebody's saved wall and
 * outlives however the label is later spelled. Shared by every four-wheel chart — tyres and brakes
 * ask about the same four corners, and a wall that called them `fl` on one tile and `front-left` on
 * another would be two vocabularies for one car.
 */
export const WHEEL_CHANNELS: readonly LegendChannel[] = WHEELS.map((wheel, index) => ({
  id: wheel.toLowerCase(),
  label: wheel,
  stroke: WHEEL_COLOURS[index]!,
}));

interface WheelTraceProps {
  store: LiveStore;
  driverKey: string;
  /** Channel ids this placement has turned off, and where a click on the legend goes. */
  hiddenChannels: readonly string[];
  onToggleChannel: (channelId: string) => void;
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
  /**
   * Reads the simulator's operating window off a frame, for the channels that have one.
   *
   * Only temperature does. Pressure and wear are deliberately left without a band: RaceRoom reports
   * a window for tread temperature and nothing equivalent for the others, and a band drawn from a
   * nominal pressure would be this dashboard's opinion wearing the simulator's clothes.
   */
  window?: (frame: FocusFrameMessage) => OperatingWindowValues | null;
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
 * The rings behind it are the slow ones — see `TYRE_SAMPLE_INTERVAL_MS`. Plotting tyres on the
 * pedals' thirty-second window would show a flat line and call it information.
 *
 * The painting is `LiveChart`'s and the labels are `LiveReadout`s, so no React render happens per
 * frame here either.
 */
export function WheelTrace({
  store,
  driverKey,
  hiddenChannels,
  onToggleChannel,
  channel,
  read,
  format,
  unit,
  range,
  window: readWindow,
  height = 112,
}: WheelTraceProps) {
  const spec: LiveChartSpec = {
    capacity: TYRE_TRACE_CAPACITY,
    scales: { y: range === undefined ? {} : { range: [...range] } },
    // Resolved inside the closures rather than here, so the on-demand ring creation in `tracesFor`
    // stays out of a render pass. See the same note in `InputsTrace`.
    series: WHEEL_CHANNELS.map((wheel, index) => ({
      id: wheel.id,
      label: wheel.label,
      stroke: wheel.stroke,
      buffer: () => channel(store.tracesFor(driverKey).tyres)[index]!,
    })),
    ...(readWindow === undefined
      ? {}
      : {
          band: {
            // Read from the store on every draw rather than from a captured frame. The chart is
            // built before the first frame arrives, and a window captured then would be null for
            // the life of the panel.
            read: () => {
              const frame = store.frameFor(driverKey);
              return frame === null ? null : readWindow(frame);
            },
          },
        }),
  };

  return (
    <div className="wheel-chart">
      <LiveChart
        store={store}
        driverKey={driverKey}
        spec={spec}
        hidden={hiddenChannels}
        height={height}
        className="wheel-chart__plot"
      />

      <ChannelLegend
        channels={WHEEL_CHANNELS}
        hidden={hiddenChannels}
        onToggle={onToggleChannel}
        unit={unit}
        // Kept live even for a hidden channel. The line going away is what the user asked for; the
        // number is a reading they may still want, and blanking it would make hiding a corner look
        // like losing it.
        renderValue={(_, index) => (
          <LiveReadout
            store={store}
            driverKey={driverKey}
            className="wheel-chart__number"
            render={(liveFrame) => format(read(liveFrame, index))}
          />
        )}
      />
    </div>
  );
}
