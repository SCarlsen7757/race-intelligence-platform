import { formatNumber, formatPercent } from '../../shared/format/format';
import { TRACE_CAPACITY } from '../../shared/live/store';
import { LiveReadout } from '../../shared/ui/LiveReadout';
import { ChannelLegend, type LegendChannel } from '../../features/focus/ChannelLegend';
import { LiveChart, type LiveChartSpec } from '../../features/focus/LiveChart';
import { WHEEL_CHANNELS } from '../../features/focus/WheelTrace';
import { TRACE_COLOURS } from '../../features/focus/traceColours';
import { ExtrasWheelTrace, type ExtrasWheelChannel } from '../../features/focus/ExtrasWheelTrace';
import { firstReportedWindow } from '../../features/focus/operatingWindow';
import type { ChannelPanelProps } from '../registry';

const TEMPERATURE: ExtrasWheelChannel = {
  ring: (extras) => extras.brakeTemperatureCelsius,
  // The reading, not the window. `optimal`, `cold` and `hot` ride alongside it on the same object
  // and are what the band reads; the line and its readout stay the temperature.
  read: (document, wheel) => document?.brakeTemperatureCelsius?.[wheel]?.current,
  window: (document) => firstReportedWindow(document?.brakeTemperatureCelsius),
};

const WEAR: ExtrasWheelChannel = {
  ring: (extras) => extras.brakeWear,
  read: (document, wheel) => document?.brakeWear?.[wheel],
};

/**
 * The brake pedal, as a channel on the pressure chart.
 *
 * Its own entry rather than a wheel, because it is not one: it shares the plot and the legend but
 * sits on a 0..1 axis while the corners are in kilonewtons.
 */
const PEDAL_CHANNEL: LegendChannel = {
  id: 'pedal',
  label: 'Pedal',
  stroke: TRACE_COLOURS.brake,
};

const PRESSURE_CHANNELS: readonly LegendChannel[] = [...WHEEL_CHANNELS, PEDAL_CHANNEL];

const formatTemperature = (value: number | null | undefined) => formatNumber(value, 0);
const formatPressure = (value: number | null | undefined) => formatNumber(value, 1);
const WEAR_RANGE = [0, 1] as const;

/**
 * Brake temperature per corner, against the window the simulator says the pads want.
 *
 * The band is the whole reason this panel is worth more than four numbers. **380 °C is cold on one
 * car and cooking on another**, and an engineer who has not memorised the pad compound cannot read a
 * raw temperature at all — with the window behind it, "climbing out of the top of the band" is
 * legible to anybody.
 */
export function BrakeTemperaturePanel(props: ChannelPanelProps) {
  return <ExtrasWheelTrace {...props} channel={TEMPERATURE} unit="°C" format={formatTemperature} />;
}

/**
 * Brake pressure per corner, in kilonewtons.
 *
 * Temperature says the discs are working; pressure says how they were *asked* to. Read together,
 * the pair is how an imbalance shows up before it becomes a temperature: **a front left
 * consistently taking less than the front right is a car that will be inconsistent under braking**
 * long before it overheats anything, and that difference is a gap between two lines here and
 * invisible in a single brake-pedal trace.
 *
 * No fixed range. Force has no natural ceiling to scale against — it depends on the car and on how
 * hard this driver brakes — so the axis fits what arrived, and the shape of the stint is the
 * message rather than the absolute height.
 *
 * ### And the pedal beside it
 *
 * Brake input is drawn on its own 0..1 axis as a fifth channel. This is only honest because pressure
 * moved to the focus frame: the two now share a sample index, so a point on the pedal line and a
 * point on a corner line are the same instant. While pressure rode the once-a-second extras
 * document they shared no index at all, and drawing them together would have looked like a
 * comparison while sampling a one-second braking event once — a chart that appears to show locking
 * and cannot is worse than no chart.
 *
 * It is a channel like any other, so an engineer who wants only the corners turns it off.
 */
export function BrakePressurePanel({
  store,
  driverKey,
  hiddenChannels,
  onToggleChannel,
}: ChannelPanelProps) {
  const spec: LiveChartSpec = {
    capacity: TRACE_CAPACITY,
    scales: { pedal: { range: [0, 1] } },
    series: [
      ...WHEEL_CHANNELS.map((wheel, index) => ({
        id: wheel.id,
        label: wheel.label,
        stroke: wheel.stroke,
        // Resolved inside the closure so the on-demand ring creation stays out of a render pass.
        buffer: () => store.tracesFor(driverKey).brakePressureKiloNewtons[index]!,
      })),
      {
        id: PEDAL_CHANNEL.id,
        label: PEDAL_CHANNEL.label,
        stroke: PEDAL_CHANNEL.stroke,
        scale: 'pedal',
        buffer: () => store.tracesFor(driverKey).brake,
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
        className="wheel-chart__plot"
      />

      <ChannelLegend
        channels={PRESSURE_CHANNELS}
        hidden={hiddenChannels}
        onToggle={onToggleChannel}
        unit="kN"
        renderValue={(channel, index) => (
          <LiveReadout
            store={store}
            driverKey={driverKey}
            className="wheel-chart__number"
            render={(frame) =>
              channel.id === PEDAL_CHANNEL.id
                ? formatPercent(frame.brake)
                : formatPressure(frame.brakePressureKiloNewtons[index])
            }
          />
        )}
      />
    </div>
  );
}

/**
 * Brake pad wear per corner.
 *
 * **Not registered in the catalogue**, and deliberately so: `SimCapabilities.BrakeWear` is set by
 * nothing because RaceRoom's shared memory has no pad-wear member to set it from. The component
 * stays because the flag and the `brakeWear` field stay — they are waiting on a connector that
 * reports the channel, and re-registering is one entry. See the remark in `sims/raceroom/index.tsx`.
 */
export function BrakeWearPanel(props: ChannelPanelProps) {
  return (
    <ExtrasWheelTrace
      {...props}
      channel={WEAR}
      unit="worn"
      format={formatPercent}
      range={WEAR_RANGE}
    />
  );
}
