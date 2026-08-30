import { NOT_REPORTED } from '../../shared/format/format';
import type { OperatingWindowRow, RaceRoomSample } from '../../shared/live/contracts';
import { TYRE_TRACE_CAPACITY, type SlowTraces, type WheelTraces } from '../../shared/live/store';
import { useSlowFrame } from '../../shared/live/useLive';
import type { ChannelPanelProps } from '../../sims/registry';
import { ChannelLegend } from './ChannelLegend';
import { LiveChart, type LiveChartSpec, type OperatingWindowValues } from './LiveChart';
import { WHEEL_CHANNELS } from './WheelTrace';

/**
 * Which slow ring a four-wheel chart draws, and where the same channel is read for its readouts.
 *
 * Two accessors rather than one because the line and the number come from different places by
 * design: the line is a stint held in a ring, the number is this second's sample. They agree because
 * the store fills the ring from that same sample — see `pushSlowSample`.
 */
export interface SlowWheelChannel {
  ring: (slow: SlowTraces) => WheelTraces;
  read: (sample: RaceRoomSample | null, wheel: number) => number | undefined;
  /**
   * The simulator's window for this channel, where it reports one.
   *
   * Takes the whole set of window rows rather than one corner's, because a window is a property of
   * the compound or the pad rather than of a corner — see {@link firstReportedWindow}.
   */
  window?: (windows: readonly OperatingWindowRow[] | undefined) => OperatingWindowValues | null;
}

interface SlowWheelTraceProps extends ChannelPanelProps {
  channel: SlowWheelChannel;
  unit: string;
  format: (value: number | null | undefined) => string;
  /**
   * A fixed y range, where the channel has one.
   *
   * A fraction belongs on 0..1, so a stint that has barely moved draws as the flat line it is
   * rather than being auto-scaled into a dramatic-looking slope. Channels with no natural bounds —
   * temperature, force — are left to scale to what arrived.
   */
  range?: readonly [number, number];
}

/**
 * One slow channel over a stint, four wheels on one axis, with the current value beside each.
 *
 * The slow-channel counterpart of `WheelTrace`, and separate from it only because the two read from
 * different places: `WheelTrace` draws the stint frame's tyre rings, this draws the slow rings at
 * roughly one sample a second. Everything above that — four corners on one
 * axis, a clickable legend that is also the readout, a band where the simulator reports one — is
 * deliberately identical, because a brake and a tyre are the same question asked of different
 * hardware and an engineer should not have to learn two charts.
 *
 * This used to live inside `BrakePanel` as a private component. It moved when tyre grip turned out
 * to want exactly the same chart: two callers is a shape, three would have been a copy.
 */
export function SlowWheelTrace({
  store,
  driverKey,
  hiddenChannels,
  onToggleChannel,
  channel,
  unit,
  format,
  range,
}: SlowWheelTraceProps) {
  const slow = useSlowFrame(driverKey);
  const { window: readWindow } = channel;

  const spec: LiveChartSpec = {
    capacity: TYRE_TRACE_CAPACITY,
    scales: { y: range === undefined ? {} : { range: [...range] } },
    // Resolved inside the closures rather than here, so the on-demand ring creation in `tracesFor`
    // stays out of a render pass.
    series: WHEEL_CHANNELS.map((wheel, index) => ({
      id: wheel.id,
      label: wheel.label,
      stroke: wheel.stroke,
      buffer: () => channel.ring(store.tracesFor(driverKey).slow)[index]!,
    })),
    ...(readWindow === undefined
      ? {}
      : {
          band: {
            // Read from the store rather than from the `slow` above, so the band survives a chart
            // built between renders — and so the closure does not pin the frame this render saw.
            read: () => readWindow(store.getSlowFrames()[driverKey]?.message.operatingWindows),
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
        className="wheel-chart__plot"
      />

      <ChannelLegend
        channels={WHEEL_CHANNELS}
        hidden={hiddenChannels}
        onToggle={onToggleChannel}
        unit={unit}
        renderValue={(_, index) => {
          const value = channel.read(slow?.message.sample ?? null, index) ?? null;

          return (
            <span className="wheel-chart__number">
              {value === null ? NOT_REPORTED : format(value)}
            </span>
          );
        }}
      />
    </div>
  );
}
