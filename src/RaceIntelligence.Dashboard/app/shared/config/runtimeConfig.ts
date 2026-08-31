/**
 * The origins the browser needs, delivered with the document rather than compiled into the bundle.
 *
 * The dashboard talks to two other services — the hub and the read API — on two other origins, and
 * a browser has no environment to read them from. For a long time the answer was to bake them in at
 * build time, which worked and made the image useless to anybody else: an image built on one server
 * carried that deployment's hostnames, so handing it to another person meant handing them a
 * rebuild.
 *
 * So the server ships them down instead. `server.mjs` reads `HUB_URL` and `READ_URL` from its own
 * environment at startup and injects them into the document's head as `window.__RIP_CONFIG__`,
 * before any application script runs. Injected rather than fetched from a `/config.json`: a fetch
 * would be a round trip on the path to opening the live socket, and a moment of rendered UI that
 * does not yet know where anything is.
 *
 * **The build-time values are still there, and are now the fallback rather than the mechanism.**
 * `npm run dev` runs Vite's dev server, not `server.mjs`, so nothing injects anything — the value
 * Vite substituted at config time is what dev uses, which is why AppHost's injected `HUB_URL` still
 * reaches a `npm run dev` session with no configuration. In a container the fallback is never
 * reached, because `server.mjs` refuses to start without both values.
 */

/** The origins a document carries down to the browser. */
export interface RuntimeConfig {
  /** The hub's origin, without a trailing slash. */
  readonly hubUrl: string;
  /** The read API's origin, without a trailing slash. */
  readonly readUrl: string;
}

/**
 * The injected object as it actually arrives, rather than as it ought to.
 *
 * Every field is optional and may be explicitly undefined: this is JSON written by another process
 * into a script tag, so the type says what could be there rather than what should be. The accessor
 * below is what turns that into an answer.
 */
export type InjectedConfig = { [K in keyof RuntimeConfig]?: string | undefined };

declare global {
  var __RIP_CONFIG__: InjectedConfig | undefined;
}

/**
 * One field of the injected config, or <c>undefined</c> when nothing was injected.
 *
 * Reads through `globalThis` rather than `window` so this is safe during a server render, where
 * there is no `window` and the answer is legitimately "nothing was injected here".
 */
export function runtimeOrigin(key: keyof RuntimeConfig): string | undefined {
  const value = globalThis.__RIP_CONFIG__?.[key];
  return typeof value === 'string' && value !== '' ? value : undefined;
}
