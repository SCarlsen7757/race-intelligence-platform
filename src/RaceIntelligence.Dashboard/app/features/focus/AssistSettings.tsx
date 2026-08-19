import { useEffect, useRef } from 'react';
import type { FocusFrameMessage } from '../../shared/live/contracts';
import type { LiveStore } from '../../shared/live/store';
import type { SimPanelProps } from '../../sims/registry';

/**
 * One assist: what it is set to, and whether it is intervening right now.
 *
 * Two facts in one indicator because they are read together. A traction control set to 4 tells you
 * how the car is configured; TC lighting up out of a slow corner tells you the driver has just
 * asked for more than the tyres had. The setting is text and the intervention is the whole tile
 * changing colour, so the second is visible without being looked at.
 *
 * Written straight to the DOM from a `requestAnimationFrame` loop, like every other readout on the
 * focus stream — an intervention lasting three frames would be invisible through React state, and
 * a `setState` at 60 Hz is the cost this whole design exists to avoid.
 */
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

  // The readers and the formatter live in a ref for the same reason `LiveChart`'s spec does: keying
  // the loop on them would restart it whenever a caller passed a fresh closure, and nothing but a
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
        rootRef.current.classList.toggle('assist--active', active === true);
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
    <div ref={rootRef} className="assist">
      <span className="assist__label">{label}</span>
      <span ref={valueRef} className="assist__value">
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

/**
 * Brake bias, said the way a race engineer says it.
 *
 * The wire carries the fraction toward the rear axle; every setup screen and every radio call names
 * the front. Converting here rather than at the wire keeps the contract describing what the
 * simulator reports and puts the translation where the number is read.
 */
const formatBrakeBias = (rearFraction: number) => `${((1 - rearFraction) * 100).toFixed(1)}% F`;

/** ABS, traction control and brake bias — what the car is set to, and what is intervening. */
export function AssistSettings({ store, driverKey }: SimPanelProps) {
  return (
    <div className="assists">
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
