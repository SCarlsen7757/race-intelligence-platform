import { useSyncExternalStore } from 'react';
import { formatAge } from './format';

/**
 * One tick, shared by every subscriber, rather than a `setInterval` per row.
 *
 * A room list can hold dozens of sessions, and every one of them wants its age refreshed on the
 * same one-second cadence. Dozens of independent timers buy nothing over a single shared one — they
 * only mean dozens of callbacks firing in the same tick and dozens of timers to leak if a row's
 * cleanup is ever missed. So the interval is a module-level singleton: it starts when the first
 * consumer subscribes and stops when the last one unsubscribes, and every consumer just listens.
 */
class AgeClock {
  private listeners = new Set<() => void>();
  private timer: ReturnType<typeof setInterval> | null = null;

  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    if (this.timer === null) {
      this.timer = setInterval(() => {
        for (const l of this.listeners) {
          l();
        }
      }, 1000);
    }

    return () => {
      this.listeners.delete(listener);
      if (this.listeners.size === 0 && this.timer !== null) {
        clearInterval(this.timer);
        this.timer = null;
      }
    };
  };
}

const clock = new AgeClock();

/**
 * Renders a timestamp as "3m ago" and keeps it current for as long as it stays on screen.
 *
 * The clock ticks every subscriber every second, but the string returned here only changes once a
 * second boundary `formatAge` actually cares about is crossed — most ticks reformat the same input
 * and get back the same text. That matters because `useSyncExternalStore` bails out of the
 * re-render when `Object.is(previous, next)` holds: a row still reading "3m ago" is notified but
 * never re-renders, so a room list of quiet sessions costs nothing per second beyond formatting a
 * string nobody sees change. Only the rows whose text actually moved repaint. This is the same
 * reasoning `store.ts`'s snapshot getters already rely on.
 */
export function useAge(isoUtc: string): string {
  const getSnapshot = () => formatAge(isoUtc, Date.now());
  return useSyncExternalStore(clock.subscribe, getSnapshot, getSnapshot);
}
