import { formatPercent } from '../../shared/format/format';
import { LiveReadout } from '../../shared/ui/LiveReadout';
import type { SimPanelProps } from '../../sims/registry';
import { PedalBars } from './PedalBars';

/**
 * What the driver's feet are doing: three bars, and the same three as percentages.
 *
 * The bar answers "how hard, right now" without being read — a shape at the edge of vision — and
 * the number answers "how hard exactly" when someone actually looks. Neither replaces the other,
 * which is why both are here rather than in two tiles.
 *
 * A bar is legible at any width, which is what made this one of the three things worth keeping when
 * everything per-wheel and per-stint moved to the wall. It is a small tile on purpose.
 */
export function PedalInputs({ store, driverKey }: SimPanelProps) {
  return (
    <div className="pedal-inputs">
      <PedalBars store={store} driverKey={driverKey} />

      <div className="pedal-values">
        <div className="pedal-value">
          <span className="pedal-value__label">Clutch</span>
          <LiveReadout
            store={store}
            driverKey={driverKey}
            className="pedal-value__number"
            render={(frame) => formatPercent(frame.clutch)}
          />
        </div>
        <div className="pedal-value">
          <span className="pedal-value__label">Brake</span>
          <LiveReadout
            store={store}
            driverKey={driverKey}
            className="pedal-value__number"
            render={(frame) => formatPercent(frame.brake)}
          />
        </div>
        <div className="pedal-value">
          <span className="pedal-value__label">Throttle</span>
          <LiveReadout
            store={store}
            driverKey={driverKey}
            className="pedal-value__number"
            render={(frame) => formatPercent(frame.throttle)}
          />
        </div>
      </div>
    </div>
  );
}
