/**
 * Where the hub is, from the browser's point of view.
 *
 * The dashboard and the hub are two origins now — a Node process serving this app, an ASP.NET
 * process holding the live state — so nothing can be inferred from `window.location` any more.
 * See `hubUrlBuild.ts` for how the address gets here.
 */

/** The hub's origin, without a trailing slash. */
export function hubOrigin(): string {
  return __HUB_URL__;
}

/**
 * The hub's viewing socket, as a `ws:`/`wss:` URL.
 *
 * The scheme is derived from the hub's own, not from the page's: the two are separate deployments
 * and one may well be behind TLS while the other is not. Deriving it from the page would produce a
 * `wss:` URL for a plain-http hub and fail at the upgrade with a message about a certificate.
 */
export function hubSocketUrl(path: string): string {
  const origin = hubOrigin();
  return origin.startsWith('https:')
    ? `wss:${origin.slice('https:'.length)}${path}`
    : `ws:${origin.slice('http:'.length)}${path}`;
}
