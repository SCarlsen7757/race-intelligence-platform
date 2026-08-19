import { formatNumber, formatPercent } from '../../shared/format/format';
import type { FocusFrameMessage } from '../../shared/live/contracts';
import type { TyreTraces } from '../../shared/live/store';
import { AssistSettings } from '../../features/focus/AssistSettings';
import { CarMetrics } from '../../features/focus/CarMetrics';
import { PedalInputs } from '../../features/focus/PedalInputs';
import { INPUT_CHANNELS, InputsTrace } from '../../features/focus/InputsTrace';
import { WHEEL_CHANNELS, WheelTrace } from '../../features/focus/WheelTrace';
import {
  registerDefaultWall,
  registerSimPanels,
  type ChannelPanelProps,
  type WidgetChannel,
} from '../registry';
import { DamagePanel } from './DamagePanel';
import { IncidentsPanel, incidentsPanelIsEmpty } from './IncidentsPanel';
import { BrakeTemperaturePanel } from './BrakePanel';

/*
 * Every function below is declared at module scope rather than inline in the JSX, and that is load
 * bearing rather than tidiness: `WheelTrace` builds its uPlot chart in an effect keyed on these, so
 * a fresh closure per render would tear the chart down and rebuild it every time the focus panel
 * re-rendered.
 */

const pressureChannel = (tyres: TyreTraces) => tyres.pressureKpa;
const wearChannel = (tyres: TyreTraces) => tyres.wear;
const temperatureChannel = (tyres: TyreTraces) => tyres.temperatureCelsius;

const readPressure = (frame: FocusFrameMessage, wheel: number) => frame.tyrePressureKpa[wheel];
const readWear = (frame: FocusFrameMessage, wheel: number) => frame.tyreWear[wheel];
// The middle of the tread, matching the line this chart draws. The shoulders and the simulator's
// window arrive on the same frame and are what the tread heatmap and the window band will read;
// this readout stays the one number that belongs beside a single line.
const readTemperature = (frame: FocusFrameMessage, wheel: number) =>
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
 * The remaining RaceRoom-specific extras the dashboard will eventually want — push-to-pass, DRS,
 * virtual energy, cut-track warnings, tyre subtype, pit menu state — are deliberately absent: none
 * of them is on the live wire yet.
 *
 * **The pedal trace is registered here despite needing no capability at all.** Throttle, brake and
 * steering are core channels every simulator reports, so it is not RaceRoom's in the way a
 * push-to-pass readout is. It sits here anyway because the registry is keyed by game key and has no
 * notion of a widget shared by every simulator — and inventing one now would be guessing at an
 * abstraction from a sample of one, which is exactly what the paragraph above says not to do. The
 * second simulator is what will show whether a shared catalogue is the right shape.
 *
 * Sizes are in grid cells. The two channel stacks are the widest because four wheels over a stint
 * is a shape, not a number, and a stack squeezed to a quarter of the wall is a smear; damage and
 * incidents are the narrowest because they are readouts and gain nothing from width.
 */
registerSimPanels('raceroom', [
  {
    id: 'car-metrics',
    title: 'Car',
    scope: 'driver',
    requires: [],
    component: CarMetrics,
    defaultSize: { w: 3, h: 4 },
    minSize: { w: 2, h: 3 },
  },
  {
    id: 'pedals',
    title: 'Pedals',
    scope: 'driver',
    requires: [],
    component: PedalInputs,
    defaultSize: { w: 3, h: 5 },
    minSize: { w: 2, h: 4 },
  },
  {
    id: 'assists',
    title: 'Assists',
    scope: 'driver',
    requires: [],
    component: AssistSettings,
    defaultSize: { w: 2, h: 3 },
    minSize: { w: 2, h: 2 },
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
    defaultSize: { w: 8, h: 6 },
    minSize: { w: 4, h: 4 },
  },
  {
    id: 'tyre-pressure',
    title: 'Tyre pressure',
    scope: 'driver',
    requires: ['TyrePressure'],
    group: { id: 'tyres', title: 'Tyres', itemTitle: 'Pressure' },
    channels: WHEEL_WIDGET_CHANNELS,
    component: TyrePressurePanel,
    defaultSize: { w: 4, h: 6 },
    minSize: { w: 3, h: 4 },
  },
  {
    id: 'tyre-wear',
    title: 'Tyre wear',
    scope: 'driver',
    requires: ['TyreWear'],
    group: { id: 'tyres', title: 'Tyres', itemTitle: 'Wear' },
    channels: WHEEL_WIDGET_CHANNELS,
    component: TyreWearPanel,
    defaultSize: { w: 4, h: 6 },
    minSize: { w: 3, h: 4 },
  },
  {
    id: 'tyre-temperature',
    title: 'Tyre temperature',
    scope: 'driver',
    requires: ['TyreTemperature'],
    group: { id: 'tyres', title: 'Tyres', itemTitle: 'Temperature' },
    channels: WHEEL_WIDGET_CHANNELS,
    component: TyreTemperaturePanel,
    defaultSize: { w: 4, h: 6 },
    minSize: { w: 3, h: 4 },
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
    defaultSize: { w: 4, h: 6 },
    minSize: { w: 3, h: 4 },
  },
  {
    id: 'damage',
    title: 'Damage',
    scope: 'driver',
    requires: ['Damage'],
    component: DamagePanel,
    defaultSize: { w: 2, h: 5 },
    minSize: { w: 2, h: 4 },
  },
  {
    id: 'incidents',
    title: 'Incidents',
    scope: 'driver',
    requires: ['IncidentPoints'],
    component: IncidentsPanel,
    isEmpty: incidentsPanelIsEmpty,
    defaultSize: { w: 2, h: 3 },
    minSize: { w: 2, h: 2 },
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
