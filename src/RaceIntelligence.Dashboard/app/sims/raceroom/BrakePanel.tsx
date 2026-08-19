import { formatNumber, formatPercent, NOT_REPORTED } from '../../shared/format/format';
import { reportedNumber } from '../../shared/live/extras';
import { TYRE_TRACE_CAPACITY, type ExtrasTraces } from '../../shared/live/store';
import { useExtras } from '../../shared/live/useLive';
import { ChannelLegend } from '../../features/focus/ChannelLegend';
import { LiveChart, type LiveChartSpec } from '../../features/focus/LiveChart';
import { WHEEL_CHANNELS } from '../../features/focus/WheelTrace';
import type { ChannelPanelProps } from '../registry';

/**
 * Which extras ring a brake chart draws, and where the same channel is read for its readouts.
 *
 * Two accessors rather than one because the line and the number come from different places by
 * design: the line is a stint held in a ring, the number is this second's document. They agree
 * because the store fills the ring from that same document — see `pushExtrasSample`.
 */
interface BrakeChannel {
  ring: (extras: ExtrasTraces) => ExtrasTraces['brakeTemperatureCelsius'];
  read: (extras: ReturnType<typeof useExtras>, wheel: number) => number | undefined;
}

function BrakeTrace({
  store,
  driverKey,
  hiddenChannels,
  onToggleChannel,
  channel,
  unit,
  format,
  range,
}: ChannelPanelProps & {
  channel: BrakeChannel;
  unit: string;
  format: (value: number | null | undefined) => string;
  range?: readonly [number, number];
}) {
  const extras = useExtras(driverKey);

  /*
   * Resolved out of the store like every other chart on the wall.
   *
   * This panel used to keep four `TraceBuffer`s in a ref and fill them from an effect, because the
   * extras document was parsed per panel and there was nowhere else for them to live. With the
   * store parsing once and pushing the channels itself, the exception is gone: the rings outlive
   * the panel, so a tile dragged to a new position keeps its stint instead of starting from empty.
   */
  const spec: LiveChartSpec = {
    capacity: TYRE_TRACE_CAPACITY,
    scales: { y: range === undefined ? {} : { range: [...range] } },
    series: WHEEL_CHANNELS.map((wheel, index) => ({
      id: wheel.id,
      label: wheel.label,
      stroke: wheel.stroke,
      buffer: () => channel.ring(store.tracesFor(driverKey).extras)[index]!,
    })),
  };

  return (
    <div className="wheel-chart">
      <LiveChart
        store={store}
        driverKey={driverKey}
        spec={spec}
        hidden={hiddenChannels}
        height={112}
        className="wheel-chart__plot"
      />

      <ChannelLegend
        channels={WHEEL_CHANNELS}
        hidden={hiddenChannels}
        onToggle={onToggleChannel}
        unit={unit}
        renderValue={(_, index) => {
          const value = reportedNumber(channel.read(extras, index));

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

const TEMPERATURE: BrakeChannel = {
  ring: (extras) => extras.brakeTemperatureCelsius,
  // The reading, not the window. `optimal`, `cold` and `hot` ride alongside it on the same object
  // and are what a band behind this trace will read; the line and its readout stay the temperature.
  read: (extras, wheel) => extras?.document?.brakeTemperatureCelsius?.[wheel]?.current,
};

const WEAR: BrakeChannel = {
  ring: (extras) => extras.brakeWear,
  read: (extras, wheel) => extras?.document?.brakeWear?.[wheel],
};

const formatTemperature = (value: number | null | undefined) => formatNumber(value, 0);
const WEAR_RANGE = [0, 1] as const;

export function BrakeTemperaturePanel(props: ChannelPanelProps) {
  return <BrakeTrace {...props} channel={TEMPERATURE} unit="°C" format={formatTemperature} />;
}

export function BrakeWearPanel(props: ChannelPanelProps) {
  return (
    <BrakeTrace {...props} channel={WEAR} unit="worn" format={formatPercent} range={WEAR_RANGE} />
  );
}
