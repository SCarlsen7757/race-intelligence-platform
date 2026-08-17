import { createRootRoute, HeadContent, Outlet, Scripts } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import { LiveProvider } from '../shared/live/LiveProvider';
import { AppShell } from '../shared/ui/AppShell';
import appCss from '../shared/ui/styles.css?url';

/**
 * The document, and the one socket every route shares.
 *
 * `LiveProvider` lives here rather than per route on purpose: the hub keeps no per-viewer memory
 * across connections, so a socket torn down and reopened on every navigation would re-request the
 * room list, the tower and the focus stream each time a viewer opened a driver. One connection for
 * the lifetime of the page, re-stating its subscriptions as the URL changes, is both cheaper and
 * the only way a room survives a reconnect.
 */
export const Route = createRootRoute({
  head: () => ({
    meta: [
      { charSet: 'utf-8' },
      { name: 'viewport', content: 'width=device-width, initial-scale=1' },
      { title: 'Race Intelligence' },
    ],
    links: [{ rel: 'stylesheet', href: appCss }],
  }),
  shellComponent: RootDocument,
  component: RootComponent,
});

function RootDocument({ children }: { children: ReactNode }) {
  return (
    <html lang="en">
      <head>
        <HeadContent />
      </head>
      <body>
        {children}
        <Scripts />
      </body>
    </html>
  );
}

function RootComponent() {
  return (
    <LiveProvider>
      <AppShell>
        <Outlet />
      </AppShell>
    </LiveProvider>
  );
}
