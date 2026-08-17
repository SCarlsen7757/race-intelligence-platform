import { createRouter } from '@tanstack/react-router';
import { routeTree } from './routeTree.gen';

/**
 * Router options the tests share with the real router.
 *
 * `pathParamsAllowedCharacters` is the one that matters: driver keys are `id:4242`, `slot:7` and
 * the like, and percent-encoding the colon turns a shareable URL into `/rooms/abc/id%3A4242`. It
 * still works, it just stops being something anyone would paste into a message.
 */
export const ROUTER_DEFAULTS = {
  pathParamsAllowedCharacters: [':'] as Array<':'>,
};

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
