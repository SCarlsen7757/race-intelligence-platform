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
 * `defaultPreload: 'intent'` costs nothing here: every route's data arrives over the live socket
 * rather than from a loader, so preloading buys a mounted component and no request at all.
 */
export function getRouter() {
  return createRouter({
    routeTree,
    defaultPreload: 'intent',
    scrollRestoration: true,
    ...ROUTER_DEFAULTS,
  });
}
