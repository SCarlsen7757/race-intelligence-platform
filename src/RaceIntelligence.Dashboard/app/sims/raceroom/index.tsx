import { formatNumber, formatPercent } from '../../shared/format/format';
import type { FocusFrameMessage } from '../../shared/live/contracts';
import type { TyreTraces } from '../../shared/live/store';
import { PedalTrace } from '../../features/focus/PedalTrace';
import { WheelTrace } from '../../features/focus/WheelTrace';
import { registerSimPanels, type SimPanelProps } from '../registry';
import { DamagePanel } from './DamagePanel';
import { IncidentsPanel, incidentsPanelIsEmpty } from './IncidentsPanel';
import { BrakeTemperaturePanel, BrakeWearPanel } from './BrakePanel';

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
const readTemperature = (frame: FocusFrameMessage, wheel: number) =>
  frame.tyreTemperatureCelsius[wheel];

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

function TyrePressurePanel({ store, driverKey }: SimPanelProps) {
  return (
    <WheelTrace
      store={store}
      driverKey={driverKey}
      channel={pressureChannel}
      read={readPressure}
      format={formatPressure}
      unit="kPa"
    />
  );
}

function TyreWearPanel({ store, driverKey }: SimPanelProps) {
  return (
    <WheelTrace
      store={store}
      driverKey={driverKey}
      channel={wearChannel}
      read={readWear}
      format={formatPercent}
      unit="worn"
      range={WEAR_RANGE}
    />
  );
}

function TyreTemperaturePanel({ store, driverKey }: SimPanelProps) {
  return (
    <WheelTrace
      store={store}
      driverKey={driverKey}
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
    id: 'pedal-trace',
    title: 'Pedal trace',
    scope: 'driver',
    requires: [],
    component: PedalTrace,
    defaultSize: { w: 6, h: 5 },
    minSize: { w: 4, h: 4 },
  },
  {
    id: 'tyre-pressure',
    title: 'Tyre pressure',
    scope: 'driver',
    requires: ['TyrePressure'],
    group: { id: 'tyres', title: 'Tyres', itemTitle: 'Pressure' },
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
    component: TyreTemperaturePanel,
    defaultSize: { w: 4, h: 6 },
    minSize: { w: 3, h: 4 },
  },
  {
    id: 'brake-temperature',
    title: 'Brake temperature',
    scope: 'driver',
    requires: ['BrakeTemperature'],
    group: { id: 'brakes', title: 'Brakes', itemTitle: 'Temperature' },
    component: BrakeTemperaturePanel,
    defaultSize: { w: 4, h: 6 },
    minSize: { w: 3, h: 4 },
  },
  {
    id: 'brake-wear',
    title: 'Brake wear',
    scope: 'driver',
    requires: ['BrakeWear'],
    group: { id: 'brakes', title: 'Brakes', itemTitle: 'Wear' },
    component: BrakeWearPanel,
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
