import { createFileRoute } from '@tanstack/react-router';
import { fetchSessions } from '../shared/history/client';
import { SessionList } from '../features/history/SessionList';

/**
 * Sessions that have already happened.
 *
 * **The first route in this app with a loader.** Every other route's data arrives over the live
 * socket, which is why `defaultPreload: 'intent'` was free — see the note in `router.tsx`, which
 * this route makes only half true. Here preloading does issue a request, and that is the point:
 * hovering the link starts the fetch that the click was going to need.
 *
 * `ssr: false` like the rest, for a different reason than they have. The live routes are
 * client-only because their data is stale the moment it is rendered; this one is client-only
 * because the read API is a separate origin the browser reaches directly, and rendering it on the
 * server would mean the Node process fetching it too. Revisit if a first paint ever needs to be
 * faster than one request.
 */
export const Route = createFileRoute('/sessions/')({
  ssr: false,
  loader: () => fetchSessions(),
  component: StoredSessionsView,
});

function StoredSessionsView() {
  const page = Route.useLoaderData();
  return <SessionList sessions={page.sessions} />;
}
