import { formatNumber, NOT_REPORTED } from '../../shared/format/format';
import type { RaceRoomExtras } from '../../shared/live/contracts';
import { reportedNumber } from '../../shared/live/extras';
import { TYRE_TRACE_CAPACITY, type ExtrasTraces } from '../../shared/live/store';
import { useExtras } from '../../shared/live/useLive';
import type { ChannelPanelProps, WidgetChannel } from '../../sims/registry';
import { ChannelLegend } from './ChannelLegend';
import { LiveChart, type LiveChartSpec } from './LiveChart';
import { TRACE_COLOURS } from './traceColours';

/**
 * The health channels, in the order a failure announces itself.
 *
 * Water and oil temperature first, because they are what climbs before something lets go. The
 * pressures next, because they are what falls. Turbo and battery last, because they describe how
 * hard the car is being asked to work rather than whether it will survive being asked.
 *
 * Every one gets its own scale. They share no units — Celsius, kilopascals, bar, percent — and
 * putting them on one axis would place an oil pressure and a water temperature at comparable
 * heights, inviting exactly the comparison that means nothing.
 *
 * Each entry names its ring and its document field separately. They answer different questions —
 * the ring is the stint, the field is this second — even though the store fills the first from the
 * second, and naming both here keeps a channel whole rather than split between a table and a
 * switch.
 */
export const SYSTEM_CHANNELS = [
  {
    id: 'engineTemp',
    label: 'Water',
    stroke: TRACE_COLOURS.brake,
    ring: (extras: ExtrasTraces) => extras.engineTempCelsius,
    field: 'engineTempCelsius',
    unit: '°C',
    digits: 0,
  },
  {
    id: 'oilTemp',
    label: 'Oil temp',
    stroke: TRACE_COLOURS.clutch,
    ring: (extras: ExtrasTraces) => extras.engineOilTempCelsius,
    field: 'engineOilTempCelsius',
    unit: '°C',
    digits: 0,
  },
  {
    id: 'oilPressure',
    label: 'Oil press',
    stroke: TRACE_COLOURS.throttle,
    ring: (extras: ExtrasTraces) => extras.engineOilPressureKpa,
    field: 'engineOilPressureKpa',
    unit: 'kPa',
    digits: 0,
  },
  {
    id: 'fuelPressure',
    label: 'Fuel press',
    stroke: TRACE_COLOURS.steering,
    ring: (extras: ExtrasTraces) => extras.fuelPressureKpa,
    field: 'fuelPressureKpa',
    unit: 'kPa',
    digits: 0,
  },
  {
    id: 'turbo',
    label: 'Turbo',
    stroke: TRACE_COLOURS.speed,
    ring: (extras: ExtrasTraces) => extras.turboPressureBar,
    field: 'turboPressureBar',
    unit: 'bar',
    digits: 2,
  },
  {
    id: 'battery',
    label: 'Battery',
    stroke: TRACE_COLOURS.rpm,
    ring: (extras: ExtrasTraces) => extras.batteryStateOfChargePercent,
    field: 'batteryStateOfChargePercent',
    unit: '%',
    digits: 0,
  },
] as const;

/** The catalogue's view of the same list, so a saved wall's hidden ids match the series drawn. */
export const SYSTEM_WIDGET_CHANNELS: readonly WidgetChannel[] = SYSTEM_CHANNELS.map(
  ({ id, label }) => ({ id, label }),
);

/** One channel's current reading, or a dash where the simulator reported none. */
function readingOf(document: RaceRoomExtras | null, index: number): string {
  const channel = SYSTEM_CHANNELS[index]!;
  const raw = document === null ? undefined : document[channel.field];
  const value = reportedNumber(typeof raw === 'number' ? raw : undefined);

  return value === null ? NOT_REPORTED : `${formatNumber(value, channel.digits)}${channel.unit}`;
}

/**
 * Engine, oil, fuel and turbo over a stint, with the current reading beside each.
 *
 * **Trends rather than instants, and that is the whole argument for the widget.** An oil
 * temperature of 118 °C means nothing on its own: it is either a car that has sat at 118 all race,
 * or a car that was at 104 ten laps ago, and only one of those is about to end somebody's
 * afternoon. The number answers neither question and the line answers both.
 *
 * These are the channels that explain a pace loss after the fact — what a race engineer goes back
 * to when a driver reports the car went off a second a lap and nothing on the tyre charts says why.
 * All of them have been crossing the wire in the extras document since the connector was written,
 * decoded once a second, and thrown away.
 *
 * A missing channel draws as a gap and reads as a dash. Several of these are plausible at zero —
 * **zero turbo pressure is a naturally aspirated engine and zero battery is a flat one** — so the
 * sentinel rule matters here more than anywhere else. Reading `-1` as a number would report minus
 * one bar of boost; reading it as zero would report a normally aspirated engine that is not one.
 */
export function SystemsTrend({
  store,
  driverKey,
  hiddenChannels,
  onToggleChannel,
}: ChannelPanelProps) {
  const extras = useExtras(driverKey);

  const spec: LiveChartSpec = {
    capacity: TYRE_TRACE_CAPACITY,
    // One scale per channel, named for it, so each line auto-scales to its own range. The axis is
    // deliberately drawn against none of them: six scales cannot share one set of numbers, and the
    // readouts under the plot are where an actual value is read.
    scales: Object.fromEntries(SYSTEM_CHANNELS.map((channel) => [channel.id, {}])),
    series: SYSTEM_CHANNELS.map((channel) => ({
      id: channel.id,
      label: channel.label,
      stroke: channel.stroke,
      scale: channel.id,
      width: 1.5,
      buffer: () => channel.ring(store.tracesFor(driverKey).extras),
    })),
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
        channels={SYSTEM_CHANNELS.map(({ id, label, stroke }) => ({ id, label, stroke }))}
        hidden={hiddenChannels}
        onToggle={onToggleChannel}
        renderValue={(_, index) => readingOf(extras?.document ?? null, index)}
      />
    </div>
  );
}
