import { formatNumber, formatPercent } from '../../shared/format/format';
import type { StintFrameMessage } from '../../shared/live/contracts';
import type { TyreTraces } from '../../shared/live/store';
import { AssistSettings } from '../../features/focus/AssistSettings';
import { CarMetrics } from '../../features/focus/CarMetrics';
import { PedalInputs } from '../../features/focus/PedalInputs';
import { INPUT_CHANNELS, InputsTrace } from '../../features/focus/InputsTrace';
import { LapDelta } from '../../features/focus/LapDelta';
import { FuelPanel } from '../../features/focus/FuelPanel';
import { EventTimeline } from '../../features/focus/EventTimeline';
import { SystemsTrend, SYSTEM_WIDGET_CHANNELS } from '../../features/focus/SystemsTrend';
import { RaceTimeline } from '../../features/wall/RaceTimeline';
import { LapTrend } from '../../features/laps/LapTrend';
import { WHEEL_CHANNELS, WheelTrace } from '../../features/focus/WheelTrace';
import { ExtrasWheelTrace, type ExtrasWheelChannel } from '../../features/focus/ExtrasWheelTrace';
import { firstReportedWindow } from '../../features/focus/operatingWindow';
import { TyreHeatmap } from '../../features/focus/TyreHeatmap';
import {
  registerDefaultWall,
  registerSimPanels,
  WIDGET_HALF,
  WIDGET_UNIT,
  WIDGET_WIDE,
  type ChannelPanelProps,
  type WidgetChannel,
} from '../registry';
import { DamagePanel } from './DamagePanel';
import { IncidentsPanel, incidentsPanelIsEmpty } from './IncidentsPanel';
import { BrakePressurePanel, BrakeTemperaturePanel } from './BrakePanel';

/*
 * Every function below is declared at module scope rather than inline in the JSX, and that is load
 * bearing rather than tidiness: `WheelTrace` builds its uPlot chart in an effect keyed on these, so
 * a fresh closure per render would tear the chart down and rebuild it every time the focus panel
 * re-rendered.
 */

const pressureChannel = (tyres: TyreTraces) => tyres.pressureKpa;
const wearChannel = (tyres: TyreTraces) => tyres.wear;
const temperatureChannel = (tyres: TyreTraces) => tyres.temperatureCelsius;

const gripChannel: ExtrasWheelChannel = {
  ring: (extras) => extras.tyreGrip,
  read: (document, wheel) => document?.tyreGrip?.[wheel],
};

const readPressure = (frame: StintFrameMessage, wheel: number) => frame.tyrePressureKpa[wheel];
const readWear = (frame: StintFrameMessage, wheel: number) => frame.tyreWear[wheel];

/**
 * The tread window, taken off the frame for the temperature chart's band.
 *
 * Every corner reports the same three numbers because they are the compound's, not the corner's —
 * see `firstReportedWindow` for why only the first reported one is drawn.
 */
const readTyreWindow = (frame: StintFrameMessage) =>
  firstReportedWindow(frame.tyreTemperatureCelsius);
// The middle of the tread, matching the line this chart draws. The shoulders and the simulator's
// window arrive on the same frame and are what the tread heatmap and the window band will read;
// this readout stays the one number that belongs beside a single line.
const readTemperature = (frame: StintFrameMessage, wheel: number) =>
  frame.tyreTemperatureCelsius[wheel]?.middle;

const formatPressure = (value: number | null | undefined) => formatNumber(value, 1);
const formatTemperature = (value: number | null | undefined) => formatNumber(value, 0);

/**
 * Wear is a fraction, so it is plotted on its own full scale.
 *
 * Auto-scaling it would take five laps of a tyre losing two percent and draw them as a cliff, which
 * is the opposite of what the chart is for: the question is how close the stint is to the point
 * where it has to end, and that only reads against the whole range.
 */
const WEAR_RANGE = [0, 1] as const;

/** Grip is RaceRoom's own 0..1 fraction, and belongs on the whole of it for the same reason. */
const GRIP_RANGE = [0, 1] as const;

/**
 * The channels a four-wheel chart declares, as the catalogue wants them.
 *
 * Derived from the same list the charts and their legends draw from, rather than written out again
 * here: an id in a saved wall has to match the id on the series it hides, and two hand-kept lists
 * are one edit away from a wall that quietly stops hiding anything.
 */
const WHEEL_WIDGET_CHANNELS: readonly WidgetChannel[] = WHEEL_CHANNELS.map(({ id, label }) => ({
  id,
  label,
}));

const INPUT_WIDGET_CHANNELS: readonly WidgetChannel[] = INPUT_CHANNELS.map(({ id, label }) => ({
  id,
  label,
}));

function TyrePressurePanel({
  store,
  driverKey,
  hiddenChannels,
  onToggleChannel,
}: ChannelPanelProps) {
  return (
    <WheelTrace
      store={store}
      driverKey={driverKey}
      hiddenChannels={hiddenChannels}
      onToggleChannel={onToggleChannel}
      channel={pressureChannel}
      read={readPressure}
      format={formatPressure}
      unit="kPa"
    />
  );
}

function TyreWearPanel({ store, driverKey, hiddenChannels, onToggleChannel }: ChannelPanelProps) {
  return (
    <WheelTrace
      store={store}
      driverKey={driverKey}
      hiddenChannels={hiddenChannels}
      onToggleChannel={onToggleChannel}
      channel={wearChannel}
      read={readWear}
      format={formatPercent}
      unit="worn"
      range={WEAR_RANGE}
    />
  );
}

function TyreTemperaturePanel({
  store,
  driverKey,
  hiddenChannels,
  onToggleChannel,
}: ChannelPanelProps) {
  return (
    <WheelTrace
      store={store}
      driverKey={driverKey}
      hiddenChannels={hiddenChannels}
      onToggleChannel={onToggleChannel}
      channel={temperatureChannel}
      read={readTemperature}
      format={formatTemperature}
      unit="°C"
      window={readTyreWindow}
    />
  );
}

/**
 * How much grip the tyre is actually giving, per corner, over the stint.
 *
 * The companion to wear rather than a duplicate of it: **wear is how much rubber has gone, grip is
 * what is left of the tyre's job**, and the two come apart exactly where the interesting answers
 * are. A tyre a third worn and still gripping is a stint worth extending; one barely worn and
 * losing grip has been overheated, and the wear chart alone would call that a healthy tyre.
 *
 * A fraction, so it takes the full scale for the same reason wear does — auto-scaling a channel
 * that has dropped four percent would draw a cliff and call it degradation.
 */
function TyreGripPanel(props: ChannelPanelProps) {
  return (
    <ExtrasWheelTrace
      {...props}
      channel={gripChannel}
      unit="grip"
      format={formatPercent}
      range={GRIP_RANGE}
    />
  );
}

/**
 * RaceRoom's focus panels.
 *
 * Concrete to this simulator by design — the plan is to implement RaceRoom properly first and
 * generalise once a second simulator shows which parts are genuinely shared, rather than guessing
 * at an abstraction now. What is already general is the *selection*: each panel below declares the
 * capability it needs, so none of them is chosen by asking which game this is. The damage panel is
 * the sharpest illustration: it appears only when a collector says it can produce damage, and
 * vanishes for one that cannot rather than showing four dashes.
 *
 * The three tyre panels plot a stint rather than reading out an instant. The number is still there,
 * as the line's current value — see `WheelTrace` for why the two belong in one panel.
 *
 * Push-to-pass, DRS and the flags reach the wall as *events* rather than as readouts — see
 * `EventTimeline` for why a light that blinks and goes out is the wrong shape for all three. What
 * is still absent is what the live wire genuinely does not carry: cut-track warnings, tyre subtype
 * and pit menu state.
 *
 * **The pedal trace is registered here despite needing no capability at all.** Throttle, brake and
 * steering are core channels every simulator reports, so it is not RaceRoom's in the way a
 * push-to-pass readout is. It sits here anyway because the registry is keyed by game key and has no
 * notion of a widget shared by every simulator — and inventing one now would be guessing at an
 * abstraction from a sample of one, which is exactly what the paragraph above says not to do. The
 * second simulator is what will show whether a shared catalogue is the right shape.
 *
 * Sizes come from the catalogue's vocabulary rather than from a number chosen here: `WIDGET_WIDE`
 * for the things read left to right across a lap, `WIDGET_HALF` for the readouts that gain nothing
 * from width, `WIDGET_UNIT` for everything else. Each entry picks one; none of them invents a size.
 * The minimum is derived from the default and is not stated at all — see `minSizeFor`.
 */
registerSimPanels('raceroom', [
  {
    id: 'car-metrics',
    title: 'Car',
    scope: 'driver',
    requires: [],
    component: CarMetrics,
    defaultSize: WIDGET_UNIT,
  },
  {
    id: 'pedals',
    title: 'Pedals',
    scope: 'driver',
    requires: [],
    component: PedalInputs,
    defaultSize: WIDGET_UNIT,
  },
  {
    id: 'assists',
    title: 'Assists',
    scope: 'driver',
    requires: [],
    component: AssistSettings,
    defaultSize: WIDGET_HALF,
  },
  {
    // Renamed from `pedal-trace`, which it outgrew: it now carries speed, gear, RPM and the two
    // assist markers as well. The id changes with the meaning rather than being kept as a
    // convenient lie, and a wall saved under the old one loses this tile and is told so — which is
    // the cheaper of the two confusions before there is anybody to break.
    id: 'inputs-trace',
    title: 'Inputs',
    scope: 'driver',
    requires: [],
    channels: INPUT_WIDGET_CHANNELS,
    component: InputsTrace,
    // Wider and taller than the pedal trace was. Nine channels on seven scales needs the room, and
    // this is the tile somebody arranges a wall around.
    defaultSize: WIDGET_WIDE,
  },
  {
    // No capability, for the same reason the inputs trace declares none: lap timing and normalised
    // lap progress are core channels, and a delta gated on a flag would be a flag every future
    // simulator had to remember to set for a chart that needs nothing special.
    id: 'lap-delta',
    title: 'Lap delta',
    scope: 'driver',
    requires: [],
    component: LapDelta,
    // Wide: a delta is read left to right across a lap, and its shape is the message.
    defaultSize: WIDGET_WIDE,
  },
  {
    id: 'lap-trend',
    title: 'Lap times',
    scope: 'driver',
    requires: [],
    component: LapTrend,
    defaultSize: WIDGET_UNIT,
  },
  {
    // No capability. Fuel is a core channel on the focus frame, and gating it would be gating a
    // question every simulator can answer on a flag every future connector had to remember.
    id: 'fuel',
    title: 'Fuel',
    scope: 'driver',
    requires: [],
    component: FuelPanel,
    // Three numbers and a line of prose. Wider than the car metrics because the basis line needs
    // room to be a sentence rather than four wrapped fragments.
    defaultSize: WIDGET_UNIT,
  },
  {
    // No capability. These ride in the extras document rather than the typed wire, like tyre grip
    // and brake pressure, and there is no `SimCapabilities` flag naming engine health. A car that
    // reports none of them draws six gaps and six dashes, which is the widget saying so.
    id: 'systems',
    title: 'Engine and systems',
    scope: 'driver',
    requires: [],
    channels: SYSTEM_WIDGET_CHANNELS,
    component: SystemsTrend,
    defaultSize: WIDGET_UNIT,
  },
  {
    // No capability for the same reason, and one more: flags are a property of the session rather
    // than of the car, so there is nothing a connector could declare that would make them
    // unavailable while still publishing at all.
    id: 'events',
    title: 'Events',
    scope: 'driver',
    requires: [],
    component: EventTimeline,
    // Narrow and tall. It is a list, and the useful dimension is how many rows fit.
    defaultSize: WIDGET_UNIT,
  },
  {
    // Takes a driver key despite describing the room, because it has to know which line is yours —
    // see the remarks on the component, and #70 for the room-scoped arm it did not fit.
    id: 'race-timeline',
    title: 'Race',
    scope: 'driver',
    requires: [],
    component: RaceTimeline,
    // The widest default on the wall, and the only widget that earns it: this is the one tile that
    // works for every car in the session rather than for the one with a collector.
    defaultSize: WIDGET_WIDE,
  },
  {
    id: 'tyre-pressure',
    title: 'Tyre pressure',
    scope: 'driver',
    requires: ['TyrePressure'],
    group: { id: 'tyres', title: 'Tyres', itemTitle: 'Pressure' },
    channels: WHEEL_WIDGET_CHANNELS,
    component: TyrePressurePanel,
    defaultSize: WIDGET_UNIT,
  },
  {
    id: 'tyre-wear',
    title: 'Tyre wear',
    scope: 'driver',
    requires: ['TyreWear'],
    group: { id: 'tyres', title: 'Tyres', itemTitle: 'Wear' },
    channels: WHEEL_WIDGET_CHANNELS,
    component: TyreWearPanel,
    defaultSize: WIDGET_UNIT,
  },
  {
    id: 'tyre-temperature',
    title: 'Tyre temperature',
    scope: 'driver',
    requires: ['TyreTemperature'],
    group: { id: 'tyres', title: 'Tyres', itemTitle: 'Temperature' },
    channels: WHEEL_WIDGET_CHANNELS,
    component: TyreTemperaturePanel,
    defaultSize: WIDGET_UNIT,
  },
  {
    // The one tyre widget that is not a stint chart, which is why it is not in the `tyres` group:
    // grouping it with three traces would suggest a fourth trace, and this is the car at one
    // instant rather than a quarter of an hour of it.
    id: 'tyre-tread',
    title: 'Tread temperature',
    scope: 'driver',
    requires: ['TyreTemperature'],
    channels: WHEEL_WIDGET_CHANNELS,
    component: TyreHeatmap,
    // Squarer than the traces, because it is a picture of a car rather than a window of time — and
    // width past the point where four corners are legible buys nothing.
    defaultSize: WIDGET_UNIT,
  },
  {
    id: 'tyre-grip',
    title: 'Tyre grip',
    scope: 'driver',
    // No capability of its own. Grip rides in the extras document rather than the typed wire, and
    // there is no `SimCapabilities` flag for it — gating on `TyreWear` would be claiming a
    // relationship that does not exist, so this appears wherever the connector writes the channel
    // and says so itself when it does not.
    requires: [],
    group: { id: 'tyres', title: 'Tyres', itemTitle: 'Grip' },
    channels: WHEEL_WIDGET_CHANNELS,
    component: TyreGripPanel,
    defaultSize: WIDGET_UNIT,
  },
  {
    // Brake *wear* is deliberately not registered beside this. `SimCapabilities.BrakeWear` exists
    // and nothing sets it, because RaceRoom's shared memory has no pad-wear member to set it from —
    // so the widget could be picked off the catalogue and would then do nothing but explain that no
    // collector reports it. The flag, the `brakeWear` field and `BrakeWearPanel` all stay: they are
    // waiting on a connector that reports the channel, and re-registering is one entry. Advertising
    // it before then is the part that was wrong.
    id: 'brake-temperature',
    title: 'Brake temperature',
    scope: 'driver',
    requires: ['BrakeTemperature'],
    group: { id: 'brakes', title: 'Brakes', itemTitle: 'Temperature' },
    channels: WHEEL_WIDGET_CHANNELS,
    component: BrakeTemperaturePanel,
    defaultSize: WIDGET_UNIT,
  },
  {
    // No capability, for the same reason tyre grip has none: brake pressure rides in the extras
    // document rather than the typed wire and has no `SimCapabilities` flag. Borrowing
    // `BrakeTemperature` would gate one channel on another simulator's willingness to report a
    // different one.
    id: 'brake-pressure',
    title: 'Brake pressure',
    scope: 'driver',
    requires: [],
    group: { id: 'brakes', title: 'Brakes', itemTitle: 'Pressure' },
    channels: WHEEL_WIDGET_CHANNELS,
    component: BrakePressurePanel,
    defaultSize: WIDGET_UNIT,
  },
  {
    id: 'damage',
    title: 'Damage',
    scope: 'driver',
    requires: ['Damage'],
    component: DamagePanel,
    defaultSize: WIDGET_HALF,
  },
  {
    id: 'incidents',
    title: 'Incidents',
    scope: 'driver',
    requires: ['IncidentPoints'],
    component: IncidentsPanel,
    isEmpty: incidentsPanelIsEmpty,
    defaultSize: WIDGET_HALF,
  },
]);

/**
 * What a wall holds before anyone has arranged one.
 *
 * The four that need no capability at all, which is not a coincidence: they are the channels every
 * simulator reports, so this arrangement is the one that cannot greet somebody with a tile
 * explaining why it is empty. What the car is doing, what the driver is doing, what the car is set
 * to, and and the trace that shows the last thirty seconds of all of it.
 *
 * The tyre and brake stacks are deliberately not here despite being the most useful things on the
 * wall. They are four-wheel stint charts, and four of them opened unasked would fill a 1440p wall
 * before the user had chosen anything — a default should be a starting point, not an opinion that
 * takes a minute to undo.
 */
registerDefaultWall('raceroom', ['car-metrics', 'pedals', 'assists', 'inputs-trace']);
