import { useCallback, useEffect, useMemo, useRef, type CSSProperties, type ReactNode } from 'react';
import { formatGear, formatNumber, formatPercent, formatSpeed } from '../../shared/format/format';
import type { FocusFrameMessage } from '../../shared/live/contracts';
import type { LiveStore } from '../../shared/live/store';
import { useConnected, useLive } from '../../shared/live/useLive';
import { LiveReadout } from '../../shared/ui/LiveReadout';
import { PedalBars } from './PedalBars';

interface FocusPanelProps {
  /** The drivers on screen, in the order the URL names them. */
  driverKeys: readonly string[];
  /** How each driver's name is shown. Falls back to the key for a driver not in the tower yet. */
  displayName: (driverKey: string) => string;
  /** Drops one driver — the last one closes the panel. */
  onClose: (driverKey: string) => void;
  /** Shown under the header: why there is no second column, when there cannot be one. */
  note?: ReactNode;
}

/**
 * One section of the panel, rendered once per driver on screen.
 *
 * A list rather than fixed markup because the same sections have to appear twice, in the same
 * order, when two drivers are compared — and building the comparison out of the same list is what
 * makes "the same row means the same channel in both" true by construction rather than by care.
 */
interface FocusSection {
  id: string;
  title: string;
  render: (driverKey: string) => ReactNode;
}

function CarMetrics({ store, driverKey }: { store: LiveStore; driverKey: string }) {
  const renderSpeed = useCallback(
    (frame: FocusFrameMessage) => formatSpeed(frame.speedMetersPerSecond),
    [],
  );

  return (
    // Five values in three columns with speed across two of them, so the grid fills exactly. The
    // auto-fit grid this replaced wrapped 4 + 1 and left three-quarters of a row empty under the
    // lap number.
    <div className="focus__primary">
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

/** The three pedals as bars, with their percentages under them. */
function Inputs({ store, driverKey }: { store: LiveStore; driverKey: string }) {
  return (
    <>
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
    </>
  );
}

function AssistIndicator({
  store,
  driverKey,
  label,
  readSetting,
  readActive,
  formatSetting = String,
}: {
  store: LiveStore;
  driverKey: string;
  label: string;
  readSetting: (frame: FocusFrameMessage) => number | null | undefined;
  readActive?: (frame: FocusFrameMessage) => boolean | null | undefined;
  formatSetting?: (setting: number) => string;
}) {
  const rootRef = useRef<HTMLDivElement>(null);
  const valueRef = useRef<HTMLSpanElement>(null);

  // The readers and the formatter live in a ref for the same reason `LiveReadout`'s do: keying the
  // loop on them would restart it whenever a caller passed a fresh closure, and nothing but a
  // hoisting convention stops a caller from doing that. Read from the loop, they are always
  // whatever the latest render supplied.
  const readersRef = useRef({ readSetting, readActive, formatSetting });
  useEffect(() => {
    readersRef.current = { readSetting, readActive, formatSetting };
  });

  useEffect(() => {
    let animationFrame = 0;
    let previousSetting: number | null | undefined;
    let previousActive: boolean | null | undefined;

    const paint = () => {
      const readers = readersRef.current;
      const frame = store.frameFor(driverKey);
      const setting = frame === null ? undefined : readers.readSetting(frame);
      const active =
        frame === null || readers.readActive === undefined ? undefined : readers.readActive(frame);
      const settingChanged = setting !== previousSetting;
      const activeChanged = active !== previousActive;

      if (settingChanged && valueRef.current !== null) {
        valueRef.current.textContent =
          setting === undefined || setting === null ? '—' : readers.formatSetting(setting);
        previousSetting = setting;
      }
      if ((settingChanged || activeChanged) && rootRef.current !== null) {
        rootRef.current.classList.toggle('motec-assist--active', active === true);
        rootRef.current.setAttribute(
          'aria-label',
          `${label} setting ${setting === undefined || setting === null ? 'unavailable' : readers.formatSetting(setting)}${active === true ? ', active' : ''}`,
        );
        previousActive = active;
      }

      animationFrame = requestAnimationFrame(paint);
    };

    animationFrame = requestAnimationFrame(paint);
    return () => cancelAnimationFrame(animationFrame);
  }, [store, driverKey, label]);

  return (
    <div ref={rootRef} className="motec-assist">
      <span className="motec-assist__label">{label}</span>
      <span ref={valueRef} className="motec-assist__value">
        —
      </span>
    </div>
  );
}

const readAbsSetting = (frame: FocusFrameMessage) => frame.absSetting;
const readAbsActive = (frame: FocusFrameMessage) => frame.absActive;
const readTcSetting = (frame: FocusFrameMessage) => frame.tractionControlSetting;
const readTcActive = (frame: FocusFrameMessage) => frame.tractionControlActive;
const readBrakeBias = (frame: FocusFrameMessage) => frame.brakeBias;
const formatBrakeBias = (rearFraction: number) => `${((1 - rearFraction) * 100).toFixed(1)}% F`;

function AssistSettings({ store, driverKey }: { store: LiveStore; driverKey: string }) {
  return (
    <div className="motec-assists">
      <AssistIndicator
        store={store}
        driverKey={driverKey}
        label="ABS"
        readSetting={readAbsSetting}
        readActive={readAbsActive}
      />
      <AssistIndicator
        store={store}
        driverKey={driverKey}
        label="TC"
        readSetting={readTcSetting}
        readActive={readTcActive}
      />
      <AssistIndicator
        store={store}
        driverKey={driverKey}
        label="BB"
        readSetting={readBrakeBias}
        formatSetting={formatBrakeBias}
      />
    </div>
  );
}

function MotecPanel({ store, driverKey }: { store: LiveStore; driverKey: string }) {
  return (
    <div className="motec">
      <div className="motec__car">
        <h4 className="focus__stack-title">Car</h4>
        <CarMetrics store={store} driverKey={driverKey} />
      </div>
      <div className="motec__inputs">
        <h4 className="focus__stack-title">Inputs</h4>
        <Inputs store={store} driverKey={driverKey} />
        <AssistSettings store={store} driverKey={driverKey} />
      </div>
    </div>
  );
}

/**
 * Every driver on screen, at the collector's full poll rate, in as little width as that can be said
 * in.
 *
 * **What is here is what is instantaneous and narrow**: what the car is doing this moment, what the
 * driver's feet are doing, and what the assists are set to. Everything per-wheel or per-stint — the
 * tyre channels, the brakes, damage, the pedal trace — moved to the pit wall, where a tile can be
 * given the width a stint needs.
 *
 * That split is arithmetic rather than taste. This column repeats once per car, so every pixel it
 * spends is spent again for every driver being compared, and the wall exists so that more than two
 * cars can be watched at once. A trace needs width to be a trace; a bar is legible at any width, so
 * the bars stay and the trace goes.
 *
 * Everything below the header is painted outside React — see `PedalBars` and `LiveReadout`. This
 * component re-renders only when the drivers or the room change. That holds for five streams
 * exactly as it did for one: nothing about another driver adds a render.
 *
 * **Drivers are laid out as a grid, not as independent strips.** Each section is a row spanning
 * every column, so the same channel is at the same height for every car. A comparison where the
 * readouts sit at different heights on each side is not a comparison.
 */
export function FocusPanel({ driverKeys, displayName, onClose, note }: FocusPanelProps) {
  const { store } = useLive();
  const connected = useConnected();

  const sections = useMemo<FocusSection[]>(
    () => [
      {
        id: 'motec',
        title: 'MoTeC',
        render: (driverKey) => <MotecPanel store={store} driverKey={driverKey} />,
      },
    ],
    [store],
  );

  const comparing = driverKeys.length > 1;

  return (
    <aside
      className={[
        'focus',
        comparing ? 'focus--compare' : '',
        // Every readout below falls back to its em dash the moment the socket drops — the store
        // stops holding a frame, so nothing here can present a stale speed as current. What that
        // does not say is *why* the panel emptied, which is what this is for.
        connected ? '' : 'focus--stale',
      ]
        .filter(Boolean)
        .join(' ')}
    >
      <header className="focus__header">
        {!connected && (
          <p className="focus__stale" role="status">
            Not updating — reconnecting to the hub
          </p>
        )}
        <div className="focus__drivers">
          {driverKeys.map((driverKey) => (
            <span key={driverKey} className="focus__driver">
              <h2 className="focus__driver-name">{displayName(driverKey)}</h2>
              <button
                type="button"
                className="link-button"
                aria-label={`Stop watching ${displayName(driverKey)}`}
                onClick={() => onClose(driverKey)}
              >
                Close
              </button>
            </span>
          ))}
        </div>
      </header>

      {note !== undefined && <p className="focus__note">{note}</p>}

      {/*
        One column per car, each section a row spanning all of them — see the component remarks for
        why the alignment is the point. The single-driver case is the same list with one column, so
        there is no second layout to keep in step with this one.

        `--compare-columns` rather than a fixed `repeat(2, ...)`: the cap on how many cars can be
        watched at once belongs to the hub, not to a stylesheet, and this column is now narrow
        enough that several fit.
      */}
      <div
        className={comparing ? 'focus__compare' : 'focus__body'}
        style={
          comparing ? ({ '--compare-columns': driverKeys.length } as CSSProperties) : undefined
        }
      >
        {comparing &&
          driverKeys.map((driverKey) => (
            <h3 key={driverKey} className="focus__column-name">
              {displayName(driverKey)}
            </h3>
          ))}

        {sections.map((section) =>
          driverKeys.map((driverKey) => (
            <section key={`${section.id}-${driverKey}`} className="focus__section">
              <h3 className="focus__section-title">{section.title}</h3>
              {section.render(driverKey)}
            </section>
          )),
        )}
      </div>
    </aside>
  );
}
