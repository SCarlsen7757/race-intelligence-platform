import { useEffect, useState, type ReactNode } from 'react';
import { LiveConnection } from './connection';
import { LiveStore } from './store';
import { LiveContext, type LiveContextValue } from './useLive';

/**
 * Owns the store and the socket for the lifetime of the app.
 *
 * Created in a lazy initialiser rather than at module scope so a test can mount several
 * independent instances, and so the socket is not opened merely by importing this file.
 */
export function LiveProvider({ children }: { children: ReactNode }) {
  const [value] = useState<LiveContextValue>(() => {
    const store = new LiveStore();
    return { store, connection: new LiveConnection(store) };
  });

  useEffect(() => {
    value.connection.connect();
    return () => value.connection.dispose();
  }, [value]);

  return <LiveContext.Provider value={value}>{children}</LiveContext.Provider>;
}
