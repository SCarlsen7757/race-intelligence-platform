import { useMemo } from 'react';
import type { RaceEvent } from '../../shared/live/store';
import { useRaceEvents } from '../../shared/live/useLive';
import type { SimPanelProps } from '../../sims/registry';

/** How the clock reads, given the first event as the zero point. */
function since(event: RaceEvent, firstAtMs: number): string {
  const seconds = Math.max(0, Math.round((Date.parse(event.atUtc) - firstAtMs) / 1000));
  const minutes = Math.floor(seconds / 60);

  return `${minutes}:${String(seconds % 60).padStart(2, '0')}`;
}

/**
 * Flags, activations and incidents, in the order they happened.
 *
 * **The value is entirely in the ordering, which is what a light cannot show.** A yellow flag
 * indicator that is on tells you there is a yellow; it does not tell you the yellow came out two
 * laps ago, one lap after the driver reported the car going away, which is the sentence a race
 * engineer is actually trying to finish. Every channel here is already on the wire and has only
 * ever been rendered as a state that blinks and is gone.
 *
 * The store records transitions rather than states — see `recordEvents` — so a standing yellow is
 * one entry rather than one a second for the length of the caution. Joining a session mid-race
 * announces nothing that was already true when the first document arrived, because a viewer opening
 * the dashboard under a yellow has not just seen it come out.
 *
 * Newest first, because on a tile that will usually be four rows tall the interesting end is the
 * recent one. The elapsed clock counts from the first event seen rather than from the session
 * start, which the live wire does not carry — it is a relative ordering, and it is honest about
 * being one.
 *
 * Plain React, no `requestAnimationFrame` and no canvas. A race produces a few dozen of these,
 * minutes apart; the machinery the traces use exists to keep sixty updates a second off the render
 * path, and there is nothing here to keep off it.
 */
export function EventTimeline({ driverKey }: SimPanelProps) {
  const events = useRaceEvents(driverKey);

  const rows = useMemo(() => {
    if (events.length === 0) {
      return [];
    }

    const firstAtMs = Date.parse(events[0]!.atUtc);
    return [...events].reverse().map((event) => ({ event, at: since(event, firstAtMs) }));
  }, [events]);

  if (rows.length === 0) {
    return <p className="wall__unavailable">No flags, activations or incidents yet.</p>;
  }

  return (
    <ol className="event-timeline">
      {rows.map(({ event, at }) => (
        <li key={event.id} className={`event-timeline__row event-timeline__row--${event.kind}`}>
          <span className="event-timeline__at">{at}</span>
          <span className="event-timeline__label">{event.label}</span>
        </li>
      ))}
    </ol>
  );
}
