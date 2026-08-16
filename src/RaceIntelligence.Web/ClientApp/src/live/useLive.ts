import { createContext, useContext, useSyncExternalStore } from 'react';
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

export function useLastError() {
  const { store } = useLive();
  return useStoreSlice(store, store.getLastError);
}

export function useConnected() {
  const { store } = useLive();
  return useStoreSlice(store, store.isConnected);
}
