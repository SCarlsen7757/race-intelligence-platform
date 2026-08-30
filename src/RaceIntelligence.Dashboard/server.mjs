/**
 * The production entry point for the built dashboard.
 *
 * `vite build` emits `dist/server/server.js`, but that file is not a server: it default-exports
 * `{ fetch }` — a request handler, in the standard TanStack Start server-entry shape — and running
 * it directly does nothing at all. Node loads the module, evaluates it, finds no listener holding
 * the event loop open, and exits 0. That is a silent success, which is the worst possible failure
 * mode: `npm start` appeared to work and the container simply vanished.
 *
 * So something has to host that handler, and this is it. Two things are needed and neither is
 * provided by the handler alone:
 *
 *   1. A listener. `srvx` serves a fetch handler on Node and is already in the tree — it is what
 *      h3 uses underneath — so this adds no new dependency surface. It is a RUNTIME dependency, not
 *      a dev one: this file imports it in the container, where `npm ci --omit=dev` has run.
 *   2. Static files. The SSR handler renders HTML that references hashed bundles under
 *      `dist/client/assets/`, but it does not serve them; without the middleware below every page
 *      loads, then dies on its own script tags.
 *
 * Read at run time, not baked in: PORT and HOST are ordinary environment variables. Note that
 * HUB_URL is emphatically NOT — it is compiled into the client bundle by `vite.config.ts`, because
 * the browser has no environment to read. See `app/shared/live/hubUrlBuild.ts`.
 */
import { fileURLToPath } from 'node:url';

import { serve } from 'srvx';
import { staticMiddleware } from 'srvx/static';

import handler from './dist/server/server.js';

// Resolved against this file rather than the process's working directory, so the server behaves
// the same whether it is started from the project directory, from `/app` in the container, or by a
// supervisor that sets cwd somewhere else entirely. A relative `dir` here silently serves nothing
// and every request falls through to SSR, which renders a 404 page for a JavaScript bundle.
const clientDir = fileURLToPath(new URL('./dist/client', import.meta.url));

// 3000 matches vite.config.ts's dev default, so the hub's development `Live:AllowedOrigins` and
// docs/development.md stay true of the built server too.
const port = Number(process.env.PORT ?? 3000);

// 0.0.0.0 rather than srvx's localhost default. In a container, binding the loopback interface
// makes the published port accept a connection and then hang — the service looks up and is
// unreachable, which is a considerably more confusing symptom than a refused connection.
const hostname = process.env.HOST ?? '0.0.0.0';

const server = serve({
  port,
  hostname,
  // Static first: the assets are hashed and immutable, and there is no reason to run a router
  // match over a request for a bundle. Anything not found on disk falls through to SSR.
  middleware: [staticMiddleware({ dir: clientDir })],
  fetch: handler.fetch,
});

await server.ready();
console.log(`dashboard listening on http://${hostname}:${port}`);
