import { useCallback, useMemo } from 'react';
import { formatGear, formatNumber, formatPercent, formatSpeed } from '../../shared/format/format';
import type { FocusFrameMessage } from '../../shared/live/contracts';
import { useLive } from '../../shared/live/useLive';
import { LiveReadout } from '../../shared/ui/LiveReadout';
import { panelsFor } from '../../sims/registry';
import '../../sims/raceroom';
import { PedalBars } from './PedalBars';
import { PedalTrace } from './PedalTrace';

interface FocusPanelProps {
  driverKey: string;
  gameKey: string;
  capabilities: string[];
  onClose: () => void;
}

/**
 * One driver at the collector's full poll rate.
 *
 * Everything below the header is painted outside React — see `PedalTrace`, `PedalBars` and
 * `LiveReadout`. This component re-renders only when the driver, the room, or the available panels
 * change, and once a second when an extras frame arrives for the damage panel.
 */
export function FocusPanel({ driverKey, gameKey, capabilities, onClose }: FocusPanelProps) {
  const { store } = useLive();

  // Sorted and deduplicated so the panel set is stable: with two collectors in a room the same
  // capability arrives twice, and an unstable list would remount the panels on every room-list
  // message.
  const stableCapabilities = useMemo(() => [...new Set(capabilities)].sort(), [capabilities]);

  const panels = useMemo(
    () => panelsFor(gameKey, stableCapabilities),
    [gameKey, stableCapabilities],
  );

  const renderSpeed = useCallback(
    (frame: FocusFrameMessage) => formatSpeed(frame.speedMetersPerSecond),
    [],
  );

  return (
    <aside className="focus">
      <header className="focus__header">
        <h2 className="focus__driver">{driverKey.replace(/^(id|slot|name):/, '')}</h2>
        <button type="button" className="link-button" onClick={onClose}>
          Close
        </button>
      </header>

      <div className="focus__primary">
        <div className="metric metric--large">
          <LiveReadout store={store} className="metric__value" render={renderSpeed} />
          <span className="metric__unit">km/h</span>
        </div>

        <div className="metric metric--large">
          <LiveReadout
            store={store}
            className="metric__value"
            render={(frame) => formatGear(frame.gear)}
          />
          <span className="metric__unit">gear</span>
        </div>

        <div className="metric">
          <LiveReadout
            store={store}
            className="metric__value"
            render={(frame) => formatNumber(frame.engineRpm)}
          />
          <span className="metric__unit">rpm</span>
        </div>

        <div className="metric">
          <LiveReadout
            store={store}
            className="metric__value"
            render={(frame) => formatNumber(frame.fuelLeftLiters, 1)}
          />
          <span className="metric__unit">L fuel</span>
        </div>

        <div className="metric">
          <LiveReadout
            store={store}
            className="metric__value"
            render={(frame) => String(frame.lapNumber)}
          />
          <span className="metric__unit">lap</span>
        </div>
      </div>

      <section className="focus__section">
        <h3 className="focus__section-title">Inputs</h3>
        <PedalBars store={store} />

        <div className="pedal-values">
          <div className="pedal-value">
            <span className="pedal-value__label">Clutch</span>
            <LiveReadout
              store={store}
              className="pedal-value__number"
              render={(frame) => formatPercent(frame.clutch)}
            />
          </div>
          <div className="pedal-value">
            <span className="pedal-value__label">Brake</span>
            <LiveReadout
              store={store}
              className="pedal-value__number"
              render={(frame) => formatPercent(frame.brake)}
            />
          </div>
          <div className="pedal-value">
            <span className="pedal-value__label">Throttle</span>
            <LiveReadout
              store={store}
              className="pedal-value__number"
              render={(frame) => formatPercent(frame.throttle)}
            />
          </div>
        </div>

        <PedalTrace store={store} />
      </section>

      {panels.map((panel) => (
        <section key={panel.id} className="focus__section">
          <h3 className="focus__section-title">{panel.title}</h3>
          <panel.component store={store} />
        </section>
      ))}

      {panels.length === 0 && (
        <p className="focus__note">This collector reports no channels beyond the basics above.</p>
      )}
    </aside>
  );
}
