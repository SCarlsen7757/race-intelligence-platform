import type { ReactNode } from 'react';
import { useConnected } from '../live/useLive';
import { ErrorBanner } from './ErrorBanner';

/**
 * The frame every route renders inside: the title, the connection light, and the error banner.
 *
 * The connection light is here rather than per route because a dropped socket is a fact about the
 * whole page. A tower that has simply stopped being updated looks exactly like a tower where
 * nobody is improving, and this is the only thing on screen that tells the two apart.
 */
export function AppShell({ children }: { children: ReactNode }) {
  const connected = useConnected();

  return (
    <div className="app">
      <header className="app__header">
        <h1 className="app__title">Race Intelligence</h1>

        <span className={`status status--${connected ? 'live' : 'offline'}`}>
          {connected ? 'Connected' : 'Reconnecting…'}
        </span>
      </header>

      <ErrorBanner />

      <main className="app__main">{children}</main>
    </div>
  );
}
