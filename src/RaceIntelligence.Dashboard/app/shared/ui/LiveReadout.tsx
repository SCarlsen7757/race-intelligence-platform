import { useEffect, useRef } from 'react';
import type { FocusFrameMessage } from '../live/contracts';
import type { LiveStore } from '../live/store';

interface LiveReadoutProps {
  store: LiveStore;
  /** Which driver's stream to read. Two can be on screen at once, so this is never implicit. */
  driverKey: string;
  /** Reads the display text out of a frame. Called once per animation frame, so keep it cheap. */
  render: (frame: FocusFrameMessage) => string;
  placeholder?: string;
  className?: string;
}

/**
 * One number from the focus stream, written straight to the DOM.
 *
 * The escape hatch that keeps the 60 Hz rule practical. A readout bound to React state would drag
 * the whole panel through a render on every frame; this writes `textContent` on a single node from
 * the same `requestAnimationFrame` loop the traces use, which is the cheapest possible update and
 * involves React exactly once, at mount.
 *
 * Each instance runs its own loop. At the handful of readouts a focus panel shows, that is simpler
 * than a shared scheduler and costs nothing measurable — the browser coalesces them into one frame
 * regardless.
 */
export function LiveReadout({
  store,
  driverKey,
  render,
  placeholder = '—',
  className,
}: LiveReadoutProps) {
  const ref = useRef<HTMLSpanElement>(null);

  useEffect(() => {
    let frame = 0;
    let previous: string | null = null;

    const paint = () => {
      const node = ref.current;
      if (node !== null) {
        const latest = store.frameFor(driverKey);
        const text = latest === null ? placeholder : render(latest);

        // Only touched when it changed. Assigning the same string still invalidates layout in some
        // engines, and most of these values are unchanged between frames.
        if (text !== previous) {
          node.textContent = text;
          previous = text;
        }
      }

      frame = requestAnimationFrame(paint);
    };

    frame = requestAnimationFrame(paint);
    return () => cancelAnimationFrame(frame);
  }, [store, driverKey, render, placeholder]);

  return (
    <span ref={ref} className={className}>
      {placeholder}
    </span>
  );
}
