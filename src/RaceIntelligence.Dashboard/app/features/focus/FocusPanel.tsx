import { useCallback, useEffect, useMemo, useRef, type ReactNode } from 'react';
import { formatGear, formatNumber, formatPercent, formatSpeed } from '../../shared/format/format';
import type { FocusFrameMessage } from '../../shared/live/contracts';
import type { LiveStore } from '../../shared/live/store';
import { useAllExtras, useConnected, useLive } from '../../shared/live/useLive';
import { LiveReadout } from '../../shared/ui/LiveReadout';
import { panelsFor } from '../../sims/registry';
import '../../sims/raceroom';
import { PedalBars } from './PedalBars';
import { PedalTrace } from './PedalTrace';

interface FocusPanelProps {
  /** The drivers on screen, in the order the URL names them. One, or two being compared. */
  driverKeys: readonly string[];
  gameKey: string;
  capabilities: string[];
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
  /**
   * Which of the strip's shapes this section wants.
   *
   * `.focus__body` used to give every panel the same flex basis, which is why the strip was mostly
   * air: a trace wants width, a tyre corner wants about ninety pixels, and a damage meter wants a
   * short bar. One rule cannot serve all three, so each section says which it is and the stylesheet
   * sizes it.
   */
  shape: 'motec' | 'chart' | 'stack' | 'compact';
  render: (driverKey: string) => ReactNode;
  /**
   * Whether this section has nothing to show for one driver, where it can tell.
   *
   * Absent on the built-in sections, which always have something to say. A sim panel that can come
   * up empty declares it on its registration — never keyed on the panel's id here, because that is
   * the shape `COMPACT_PANELS` already had to be rescued from once.
   */
  isEmpty?: (driverKey: string) => boolean;
}

/**
 * Sim panels that are a readout rather than a chart, and so want a narrow column instead of the
 * width a trace needs.
 */
const COMPACT_PANELS = new Set(['damage', 'incidents']);

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

  useEffect(() => {
    let animationFrame = 0;
    let previousSetting: number | null | undefined;
    let previousActive: boolean | null | undefined;

    const paint = () => {
      const frame = store.frameFor(driverKey);
      const setting = frame === null ? undefined : readSetting(frame);
      const active = frame === null || readActive === undefined ? undefined : readActive(frame);
      const settingChanged = setting !== previousSetting;
      const activeChanged = active !== previousActive;

      if (settingChanged && valueRef.current !== null) {
        valueRef.current.textContent =
          setting === undefined || setting === null ? '—' : formatSetting(setting);
        previousSetting = setting;
      }
      if ((settingChanged || activeChanged) && rootRef.current !== null) {
        rootRef.current.classList.toggle('motec-assist--active', active === true);
        rootRef.current.setAttribute(
          'aria-label',
          `${label} setting ${setting === undefined || setting === null ? 'unavailable' : formatSetting(setting)}${active === true ? ', active' : ''}`,
        );
        previousActive = active;
      }

      animationFrame = requestAnimationFrame(paint);
    };

    animationFrame = requestAnimationFrame(paint);
    return () => cancelAnimationFrame(animationFrame);
  }, [store, driverKey, label, readSetting, readActive, formatSetting]);

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
      <div className="motec__trace">
        <h4 className="focus__stack-title">Pedal trace · 30 seconds</h4>
        <PedalTrace store={store} driverKey={driverKey} />
      </div>
    </div>
  );
}

/**
 * One or two drivers at the collector's full poll rate.
 *
 * Everything below the header is painted outside React — see `PedalTrace`, `PedalBars`,
 * `WheelTrace` and `LiveReadout`. This component re-renders only when the drivers, the room, or the
 * available panels change, and once a second when an extras frame arrives for a damage panel. That
 * holds for two streams exactly as it did for one: nothing about a second driver adds a render.
 *
 * Laid out as a strip beneath the tower rather than a column beside it. A sidebar squeezes the
 * timing table — the thing with twenty rows and ten columns — to make room for panels that are each
 * two or three numbers wide. Below the tower they sit side by side at a glanceable size and the
 * tower keeps its full width.
 *
 * **Two drivers are laid out as a grid, not as two strips.** Each section is a row spanning both
 * columns, so the same channel is at the same height on both sides. A comparison where the tyre
 * temperatures sit at different heights on each side is not a comparison — and two independently
 * wrapping strips would produce exactly that as soon as one driver's collector reported a channel
 * the other's did not.
 */
export function FocusPanel({
  driverKeys,
  gameKey,
  capabilities,
  displayName,
  onClose,
  note,
}: FocusPanelProps) {
  const { store } = useLive();
  const connected = useConnected();
  const extras = useAllExtras();

  // Sorted and deduplicated so the panel set is stable: with two collectors in a room the same
  // capability arrives twice, and an unstable list would remount the panels on every room-list
  // message.
  const stableCapabilities = useMemo(() => [...new Set(capabilities)].sort(), [capabilities]);

  const panels = useMemo(
    () => panelsFor(gameKey, stableCapabilities),
    [gameKey, stableCapabilities],
  );

  const panelSections = useMemo<FocusSection[]>(() => {
    const result: FocusSection[] = [];
    const groups = new Map<string, typeof panels>();

    for (const panel of panels) {
      if (panel.group === undefined) {
        result.push({
          id: panel.id,
          title: panel.title,
          shape: COMPACT_PANELS.has(panel.id) ? 'compact' : 'chart',
          render: (driverKey) => <panel.component store={store} driverKey={driverKey} />,
          ...(panel.isEmpty === undefined
            ? {}
            : {
                isEmpty: (driverKey: string) =>
                  panel.isEmpty!(extras[driverKey] ?? null),
              }),
        });
        continue;
      }

      const members = groups.get(panel.group.id);
      if (members !== undefined) {
        members.push(panel);
        continue;
      }

      const groupMembers = [panel];
      groups.set(panel.group.id, groupMembers);
      result.push({
        id: panel.group.id,
        title: panel.group.title,
        shape: 'stack',
        render: (driverKey) => (
          <div className="focus__stack">
            {groupMembers.map((member) => (
              <div key={member.id} className="focus__stack-item">
                <h4 className="focus__stack-title">{member.group!.itemTitle}</h4>
                <member.component store={store} driverKey={driverKey} />
              </div>
            ))}
          </div>
        ),
        isEmpty: (driverKey) =>
          groupMembers.every(
            (member) =>
              member.isEmpty !== undefined && member.isEmpty(extras[driverKey] ?? null),
          ),
      });
    }

    return result;
  }, [extras, panels, store]);

  const sections = useMemo<FocusSection[]>(
    () => [
      {
        id: 'motec',
        title: 'MoTeC',
        shape: 'motec',
        render: (driverKey) => <MotecPanel store={store} driverKey={driverKey} />,
      },
      ...panelSections,
    ],
    [panelSections, store],
  );

  /**
   * Sections dropped only when they are empty for *every* driver on screen.
   *
   * Not per column, and that is the whole point. `.focus__compare` lays each section out as one row
   * spanning both sides so the same channel sits at the same height in both — dropping a section
   * from one column and not the other would slide every row beneath it out of step, which is
   * precisely the comparison failure that layout exists to prevent.
   */
  const visibleSections = useMemo(
    () => sections.filter(({ isEmpty }) => !isEmpty || !driverKeys.every((key) => isEmpty(key))),
    [driverKeys, sections],
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
        One flow of panels rather than a stack. Each is sized by its content and wraps onto the next
        line when the window narrows, so the same markup reads as a strip on a wide monitor and as a
        stack on a narrow one without a breakpoint deciding which. The comparison is the same list
        again, this time as grid rows — see the component remarks.
      */}
      <div className={comparing ? 'focus__compare' : 'focus__body'}>
        {comparing &&
          driverKeys.map((driverKey) => (
            <h3 key={driverKey} className="focus__column-name">
              {displayName(driverKey)}
            </h3>
          ))}

        {visibleSections.map((section) =>
          driverKeys.map((driverKey) => (
            <section
              key={`${section.id}-${driverKey}`}
              className={`focus__section focus__section--${section.shape}`}
            >
              <h3 className="focus__section-title">{section.title}</h3>
              {section.render(driverKey)}
            </section>
          )),
        )}
      </div>

      {panels.length === 0 && (
        <p className="focus__note">This collector reports no channels beyond the basics above.</p>
      )}
    </aside>
  );
}
