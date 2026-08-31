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
 * Read at run time: PORT, HOST, HUB_URL and READ_URL are all ordinary environment variables.
 *
 * HUB_URL and READ_URL used to be compiled into the client bundle instead, because the browser has
 * no environment of its own to read. That made a built image carry one deployment's hostnames and
 * be useless to anybody else — repointing it was a rebuild rather than a restart. It is not true
 * that the browser cannot be told at run time; it just has to be told by somebody, and this process
 * is somebody. It reads both origins here and injects them into the document's head, which is why
 * `docker build` now takes no deployment-specific arguments at all.
 *
 * See `app/shared/config/runtimeConfig.ts` for the other end of that.
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

/**
 * One of the two origins the browser is told about, required and normalised.
 *
 * There is deliberately no localhost fallback here, unlike the build-time resolvers. Falling back is
 * right for `npm run dev`, where localhost is genuinely where everything is, and wrong in a
 * container, where it silently produces a dashboard that loads and then tries to open a socket to
 * the visitor's own machine — a failure that reads as "the hub is down" and mentions configuration
 * nowhere. Refusing to start names the actual problem once, at the moment somebody can still fix it.
 */
function requireOrigin(name) {
  const raw = process.env[name]?.trim();
  if (!raw) {
    throw new Error(
      `${name} is not set. The dashboard image is deployment-agnostic and is told both origins at ` +
        `run time, so this must be set in the environment — e.g. ${name}=https://race-api.example.com`,
    );
  }

  let parsed;
  try {
    parsed = new URL(raw);
  } catch {
    throw new Error(
      `${name} must be an absolute origin including the scheme, not ${JSON.stringify(raw)}.`,
    );
  }

  if (parsed.protocol !== 'https:' && parsed.protocol !== 'http:') {
    throw new Error(
      `${name} must be http: or https:, not ${parsed.protocol} — the socket scheme is derived from it.`,
    );
  }

  // `origin` rather than the string as given: it is scheme, host and port and nothing else, so a
  // trailing slash, a stray path or a query string all normalise away here rather than corrupting
  // the first request. Every use appends a path, and `https://host/` + `/live/view` is a doubled
  // slash the hub answers with a 404 that looks nothing like a configuration mistake.
  //
  // It also makes the injection below safe by construction: an origin cannot contain `<`, so there
  // is no way for a configured value to close the script element it is written into.
  const origin = parsed.origin;

  // Not fatal, because it is legitimate: a LAN deployment with the tunnel commented out serves this
  // page over http too, and a ws:// socket from an http:// page is fine. It is only a problem when
  // the page is https, which this process cannot know — TLS is terminated upstream. So: say it
  // loudly once and let the operator judge.
  if (
    parsed.protocol === 'http:' &&
    parsed.hostname !== 'localhost' &&
    parsed.hostname !== '127.0.0.1'
  ) {
    console.warn(
      `warning: ${name} is ${origin}. The socket scheme is derived from it, so this yields a ws:// ` +
        `URL — which a browser blocks as mixed content if it loaded this dashboard over https. Use ` +
        `the public https:// origin unless the whole deployment is plain http.`,
    );
  }

  return origin;
}

const runtimeConfig = {
  hubUrl: requireOrigin('HUB_URL'),
  readUrl: requireOrigin('READ_URL'),
};

// Injected into the head so it is set before any application script runs, rather than served from a
// /config.json the app would have to fetch — that would be a round trip on the path to opening the
// live socket, and a moment of rendered UI that does not know where anything is yet.
//
// No escaping of the payload, because there is nothing left to escape: both values came from
// `URL.origin` above, which is scheme, host and port only. An origin cannot contain `<`, so it
// cannot close the script element it is written into. That is a property of requireOrigin, so if
// this ever carries a value from anywhere else, it needs escaping again.
const configScript = `<script>window.__RIP_CONFIG__=${JSON.stringify(runtimeConfig)}</script>`;

/**
 * Puts the runtime config into the document on its way out.
 *
 * A response rewrite rather than something the app renders, so that no application code has to know
 * this process exists — the app reads a global and does not care who set it. Only HTML is touched;
 * every asset falls through untouched, and so does anything without a head to inject into.
 */
async function injectRuntimeConfig(request, next) {
  const response = await next();

  if (!response.headers.get('content-type')?.includes('text/html')) {
    return response;
  }

  const html = await response.text();
  if (!html.includes('<head>')) {
    return new Response(html, response);
  }

  return new Response(html.replace('<head>', `<head>${configScript}`), response);
}

const server = serve({
  port,
  hostname,
  // Static first: the assets are hashed and immutable, and there is no reason to run a router
  // match over a request for a bundle. Anything not found on disk falls through to SSR.
  middleware: [staticMiddleware({ dir: clientDir }), injectRuntimeConfig],
  fetch: handler.fetch,
});

await server.ready();
console.log(`dashboard listening on http://${hostname}:${port}`);
console.log(`  hub:  ${runtimeConfig.hubUrl}`);
console.log(`  read: ${runtimeConfig.readUrl}`);
