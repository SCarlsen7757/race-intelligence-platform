/**
 * Where the read API is, from the browser's point of view.
 *
 * See `readUrlBuild.ts` for how the address gets here, and why it is a second address rather than
 * a path on the hub.
 */

/** The read API's origin, without a trailing slash. */
export function readOrigin(): string {
  return __READ_URL__;
}

/** An absolute URL for a read API path, which must start with a slash. */
export function readUrl(path: string): string {
  return `${readOrigin()}${path}`;
}
