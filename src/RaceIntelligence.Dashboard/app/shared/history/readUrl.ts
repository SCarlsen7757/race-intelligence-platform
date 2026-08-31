import { runtimeOrigin } from '../config/runtimeConfig';

/**
 * Where the read API is, from the browser's point of view.
 *
 * See `runtimeConfig.ts` for how the address gets here, and `readUrlBuild.ts` for why it is a
 * second address rather than a path on the hub.
 */

/** The read API's origin, without a trailing slash. */
export function readOrigin(): string {
  // Injected first, build-time fallback second — see hubOrigin() for why both exist.
  return runtimeOrigin('readUrl') ?? __READ_URL__;
}

/** An absolute URL for a read API path, which must start with a slash. */
export function readUrl(path: string): string {
  return `${readOrigin()}${path}`;
}
