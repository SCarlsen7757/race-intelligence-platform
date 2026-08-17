import { formatNumber, formatPercent } from '../../shared/format/format';
import type { FocusFrameMessage } from '../../shared/live/contracts';
import { WHEELS } from '../../shared/live/contracts';
import { LiveReadout } from '../../shared/ui/LiveReadout';
import { registerSimPanels, type SimPanelProps } from '../registry';
import { DamagePanel } from './DamagePanel';

/**
 * Per-wheel values as a car-shaped grid, which is how a race engineer reads them — a corner is
 * only interesting relative to the one across from it.
 */
function WheelGrid({
  store,
  read,
  format,
  unit,
}: SimPanelProps & {
  read: (index: number) => (frame: FocusFrameMessage) => number | null | undefined;
  format: (value: number | null | undefined) => string;
  unit: string;
}) {
  return (
    <div className="wheels">
      {WHEELS.map((wheel, index) => (
        <div key={wheel} className="wheel">
          <span className="wheel__label">{wheel}</span>
          <LiveReadout
            store={store}
            className="wheel__value"
            render={(frame) => format(read(index)(frame))}
          />
          <span className="wheel__unit">{unit}</span>
        </div>
      ))}
    </div>
  );
}

function TyrePressurePanel({ store }: SimPanelProps) {
  return (
    <WheelGrid
      store={store}
      read={(index) => (frame) => frame.tyrePressureKpa[index]}
      format={(value) => formatNumber(value, 1)}
      unit="kPa"
    />
  );
}

function TyreWearPanel({ store }: SimPanelProps) {
  return (
    <WheelGrid
      store={store}
      read={(index) => (frame) => frame.tyreWear[index]}
      format={formatPercent}
      unit="worn"
    />
  );
}

function TyreTemperaturePanel({ store }: SimPanelProps) {
  return (
    <WheelGrid
      store={store}
      read={(index) => (frame) => frame.tyreTemperatureCelsius[index]}
      format={(value) => formatNumber(value, 0)}
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
 * The remaining RaceRoom-specific extras the dashboard will eventually want — push-to-pass, DRS,
 * virtual energy, cut-track warnings, tyre subtype, pit menu state — are deliberately absent: none
 * of them is on the live wire yet.
 */
registerSimPanels('raceroom', [
  {
    id: 'tyre-pressure',
    title: 'Tyre pressure',
    requires: ['TyrePressure'],
    component: TyrePressurePanel,
  },
  {
    id: 'tyre-wear',
    title: 'Tyre wear',
    requires: ['TyreWear'],
    component: TyreWearPanel,
  },
  {
    id: 'tyre-temperature',
    title: 'Tyre temperature',
    requires: ['TyreTemperature'],
    component: TyreTemperaturePanel,
  },
  {
    id: 'damage',
    title: 'Damage',
    requires: ['Damage'],
    component: DamagePanel,
  },
]);
