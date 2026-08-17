import { createContext, useCallback, useContext, useSyncExternalStore } from 'react';
import type { ExtrasFrameMessage, LapHistoryMessage } from './contracts';
import type { LiveConnection } from './connection';
import type { LiveStore } from './store';

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
export function useExtras(driverKey: string): ExtrasFrameMessage | null {
  const { store } = useLive();
  const read = useCallback(() => store.getExtras()[driverKey] ?? null, [store, driverKey]);

  return useStoreSlice(store, read);
}
