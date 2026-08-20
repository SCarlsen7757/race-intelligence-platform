import { createContext, useCallback, useContext, useSyncExternalStore } from 'react';
import type { LapHistoryMessage, StintFrameMessage } from './contracts';
import type { LiveConnection } from './connection';
import type { ExtrasSnapshot, LapSummary, LiveStore, RaceEvent } from './store';

// A stable empty reference: useSyncExternalStore compares snapshots by identity, and a fresh [] per
// read would look like a change on every emit and loop.
const EMPTY_LAP_SUMMARIES: readonly LapSummary[] = [];
const EMPTY_EVENTS: readonly RaceEvent[] = [];

export interface LiveContextValue {
  store: LiveStore;
  connection: LiveConnection;
}

export const LiveContext = createContext<LiveContextValue | null>(null);

export function useLive(): LiveContextValue {
  const value = useContext(LiveContext);
  if (value === null) {
    throw new Error('useLive must be used inside a LiveProvider.');
  }

  return value;
}

/**
 * Binds a slow-changing slice of the store to React.
 *
 * `useSyncExternalStore` rather than state mirrored with an effect: the store is written from a
 * socket callback, outside React's knowledge, and this is the API built for exactly that. It also
 * makes the tearing question moot under concurrent rendering.
 *
 * **Nothing here observes focus frames**, and that omission is the design. Those arrive at 60 Hz
 * and are read by the focus panel's paint loop directly; routing them through React would mean a
 * render cycle per frame, which is the cost the whole two-rate transport exists to avoid.
 */
function useStoreSlice<T>(store: LiveStore, read: () => T): T {
  return useSyncExternalStore(store.subscribe, read, read);
}

export function useRooms() {
  const { store } = useLive();
  return useStoreSlice(store, store.getRooms);
}

export function useTower() {
  const { store } = useLive();
  return useStoreSlice(store, store.getTower);
}

/**
 * What is true of the session itself — its lap length, and its pit window.
 *
 * Null before the hub has answered, and stale for exactly as long as it takes a room switch to be
 * acknowledged, which is why every consumer checks `roomId` against the room it is rendering rather
 * than trusting whatever arrived last.
 */
export function useSessionState() {
  const { store } = useLive();
  return useStoreSlice(store, store.getSessionState);
}

export function useLastError() {
  const { store } = useLive();
  return useStoreSlice(store, store.getLastError);
}

export function useConnected() {
  const { store } = useLive();
  return useStoreSlice(store, store.isConnected);
}

/**
 * Which focused drivers are actually streaming, as opposed to merely subscribed.
 *
 * The one exception to "nothing here observes focus frames", and it is a narrow one: the store
 * emits when a driver gains or loses its first frame, never on the frames in between. A subscribed
 * driver with nothing arriving yet is a click that has not visibly registered, and telling those
 * apart is the whole reason this exists.
 */
export function useFocusReady() {
  const { store } = useLive();
  return useStoreSlice(store, store.getFocusReadyKeys);
}

/**
 * One driver's completed laps, or null until the hub has answered.
 *
 * Reads out of the per-driver map rather than subscribing per driver, because the messages
 * themselves are stable objects: the snapshot this returns only changes identity when a new
 * history for *this* driver arrives, so an expanded row does not re-render when another row's
 * history does.
 */
export function useLapHistory(driverKey: string): LapHistoryMessage | null {
  const { store } = useLive();
  const read = useCallback(() => store.getLapHistories()[driverKey] ?? null, [store, driverKey]);

  return useStoreSlice(store, read);
}

/**
 * The latest extras frame for one focused driver, at roughly 1 Hz.
 *
 * Per driver for the same reason lap history is: two cars can be compared at once, and a single
 * slot would have both damage panels showing whichever frame arrived last.
 */
export function useExtras(driverKey: string): ExtrasSnapshot | null {
  const { store } = useLive();
  const read = useCallback(() => store.getExtras()[driverKey] ?? null, [store, driverKey]);

  return useStoreSlice(store, read);
}

/**
 * One driver's latest tyre readings — pressure, wear, and the tread with its operating window.
 *
 * A hook rather than a paint-loop read, unlike the focus frame: these arrive at about 1 Hz on their
 * own frame, so the render cost is irrelevant and the ergonomics are worth a great deal. The rolling
 * traces behind them are still rings, read from `LiveChart` as always.
 */
export function useStint(driverKey: string): StintFrameMessage | null {
  const { store } = useLive();
  const read = useCallback(() => store.stintFor(driverKey), [store, driverKey]);

  return useStoreSlice(store, read);
}

/**
 * One driver's per-lap derived numbers — fuel used, lap time, validity.
 *
 * React state rather than a ring, and that is the line this whole store draws: these change once a
 * lap, which is exactly what React is cheap for.
 */
export function useLapSummaries(driverKey: string): readonly LapSummary[] {
  const { store } = useLive();
  const read = useCallback(
    () => store.getLapSummaries()[driverKey] ?? EMPTY_LAP_SUMMARIES,
    [store, driverKey],
  );

  return useStoreSlice(store, read);
}

/**
 * One driver's flags, activations and incidents, oldest first.
 *
 * React state for the same reason the lap summaries are: a race produces a few dozen of these,
 * minutes apart.
 */
export function useRaceEvents(driverKey: string): readonly RaceEvent[] {
  const { store } = useLive();
  const read = useCallback(() => store.getEvents()[driverKey] ?? EMPTY_EVENTS, [store, driverKey]);

  return useStoreSlice(store, read);
}

/**
 * Every focused driver's latest extras frame as one stable snapshot.
 *
 * FocusPanel decides whether a comparison row exists across all drivers at once. Reading the whole
 * record keeps that decision in one hook call; a per-driver hook in its section loop would make the
 * number of hooks depend on how many cars are compared.
 */
export function useAllExtras(): Readonly<Record<string, ExtrasSnapshot>> {
  const { store } = useLive();
  return useStoreSlice(store, store.getExtras);
}
