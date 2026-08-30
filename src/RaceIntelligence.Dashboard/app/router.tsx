import { createRouter } from '@tanstack/react-router';
import { routeTree } from './routeTree.gen';

/**
 * Router options the tests share with the real router.
 *
 * Empty, and worth keeping as the seam rather than deleting: it held
 * `pathParamsAllowedCharacters` for the colon and comma in `/rooms/abc/id:4242,id:7`, back when a
 * path segment named the drivers. Nothing in a path needs unencoded punctuation now — a room id is
 * a bare hex guid — and which cars are being watched is state belonging to the room rather than to
 * the URL.
 */
export const ROUTER_DEFAULTS = {};

/**
 * The router TanStack Start boots, on the server and in the browser.
 *
 * `defaultPreload: 'intent'` used to cost nothing: every route's data arrived over the live socket
 * rather than from a loader, so preloading bought a mounted component and no request at all.
 *
 * The `/sessions` routes changed that. They read history over HTTP and have real loaders, so
 * preloading them does issue a request — which is why it stays on. Those are the routes where
 * starting the fetch on hover is worth something, and the live routes still pay nothing for it.
 */
export function getRouter() {
  return createRouter({
    routeTree,
    defaultPreload: 'intent',
    scrollRestoration: true,
    ...ROUTER_DEFAULTS,
  });
}
