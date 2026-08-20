import { useCallback } from 'react';
import { formatGear, formatNumber, formatSpeed } from '../../shared/format/format';
import type { FocusFrameMessage } from '../../shared/live/contracts';
import { LiveReadout } from '../../shared/ui/LiveReadout';
import type { SimPanelProps } from '../../sims/registry';

/**
 * What the car is doing this instant: speed, gear, revs, fuel, lap.
 *
 * Five numbers that were the top of the compare column until the column stopped existing. They are
 * a widget now for the same reason everything else is — the wall is the only place things go, and
 * a fixed region carrying five numbers was a region the user could not move, resize or remove.
 *
 * **Painted outside React.** Every value here is a `LiveReadout` writing `textContent` from its own
 * `requestAnimationFrame` loop against the store's latest frame, so this component renders once and
 * then never again for telemetry. That is the rule the whole frontend is built on and it survives
 * being on a draggable tile unchanged.
 *
 * Three columns with speed across two of them, so the grid fills exactly. The auto-fit grid this
 * replaced wrapped four and one, and left three-quarters of a row empty under the lap number.
 */
export function CarMetrics({ store, driverKey }: SimPanelProps) {
  const renderSpeed = useCallback(
    (frame: FocusFrameMessage) => formatSpeed(frame.speedMetersPerSecond),
    [],
  );

  return (
    <div className="car-metrics">
      <div className="metric metric--large metric--wide">
        <LiveReadout
          store={store}
          driverKey={driverKey}
          className="metric__value"
          render={renderSpeed}
        />
        <span className="metric__unit">km/h</span>
      </div>

      <div className="metric metric--large">
        <LiveReadout
          store={store}
          driverKey={driverKey}
          className="metric__value"
          render={(frame) => formatGear(frame.gear)}
        />
        <span className="metric__unit">gear</span>
      </div>

      <div className="metric">
        <LiveReadout
          store={store}
          driverKey={driverKey}
          className="metric__value"
          render={(frame) => formatNumber(frame.engineRpm)}
        />
        <span className="metric__unit">rpm</span>
      </div>

      <div className="metric">
        <LiveReadout
          store={store}
          driverKey={driverKey}
          className="metric__value"
          render={(frame) => formatNumber(frame.fuelLeftLiters, 1)}
        />
        <span className="metric__unit">L fuel</span>
      </div>

      <div className="metric">
        <LiveReadout
          store={store}
          driverKey={driverKey}
          className="metric__value"
          render={(frame) => String(frame.lapNumber)}
        />
        <span className="metric__unit">lap</span>
      </div>
    </div>
  );
}
