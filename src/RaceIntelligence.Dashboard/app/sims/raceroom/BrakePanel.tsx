import { formatNumber, formatPercent } from '../../shared/format/format';
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

const PRESSURE: ExtrasWheelChannel = {
  ring: (extras) => extras.brakePressureKiloNewtons,
  read: (document, wheel) => document?.brakePressureKiloNewtons?.[wheel],
};

const WEAR: ExtrasWheelChannel = {
  ring: (extras) => extras.brakeWear,
  read: (document, wheel) => document?.brakeWear?.[wheel],
};

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
 * ### The pedal is deliberately not on this chart
 *
 * Putting brake input beside corner pressure would show what the driver asked for against what
 * arrived, which is where bias and locking live — and it cannot be done honestly from this wire.
 * The pedal is a focus channel at sixty samples a second; pressure rides in the extras document at
 * roughly one. A brake application lasts about a second, so at the extras cadence a whole braking
 * event is one or two samples, and the two series share no index at all — the store keeps them in
 * separate rings for exactly that reason.
 *
 * Drawn together anyway, it would look like a comparison while sampling far too coarsely to be one:
 * a chart that appears to show locking and cannot is worse than no chart. It wants brake pressure
 * on the focus frame, which is a wire change rather than a widget.
 *
 * The narrower question it was reaching for is already answered elsewhere — `brakeBias` is on the
 * focus frame and reads out in the assists widget, straight from the simulator rather than inferred.
 */
export function BrakePressurePanel(props: ChannelPanelProps) {
  return <ExtrasWheelTrace {...props} channel={PRESSURE} unit="kN" format={formatPressure} />;
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
