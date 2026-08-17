/**
 * How the focused drivers are written into, and read back out of, the URL.
 *
 * The URL is where "watch this car with me" stops being a set of instructions and becomes a link,
 * and a comparison is no different: `/rooms/abc/id:42,id:7` survives a refresh and pastes into a
 * message. A comma rather than a second path segment or a search param, because the router already
 * has to allow unencoded punctuation in this position — driver keys are `id:4242` and `slot:7` —
 * and `,` is on the same allow list as `:` (see `ROUTER_DEFAULTS`).
 */

/** Separates driver keys in the path segment. Must be in `pathParamsAllowedCharacters`. */
export const DRIVER_KEY_SEPARATOR = ',';

/**
 * How many drivers can be watched at once.
 *
 * Mirrors the hub's `LiveViewer.MaxFocusDrivers`, which is the one that actually enforces it: the
 * viewing endpoint is open, so the bound has to hold against a client that is not this dashboard.
 * Keeping the same number here means a viewer never sends a request it knows will be refused.
 */
export const MAX_FOCUSED_DRIVERS = 2;

/** Reads the focused drivers out of a path segment, in the order it names them. */
export function parseDriverKeys(segment: string | undefined): string[] {
  if (segment === undefined) {
    return [];
  }

  const seen = new Set<string>();

  return segment
    .split(DRIVER_KEY_SEPARATOR)
    .map((key) => key.trim())
    .filter((key) => {
      // Deduplicated because a hand-edited URL can name the same driver twice, and two identical
      // columns are not a comparison. Capped for the same reason the hub caps it — a link asking
      // for five cars at 60 Hz is refused, not honoured.
      if (key === '' || seen.has(key)) {
        return false;
      }

      seen.add(key);
      return seen.size <= MAX_FOCUSED_DRIVERS;
    });
}

/** Writes the focused drivers back into a path segment. */
export function formatDriverKeys(driverKeys: readonly string[]): string {
  return driverKeys.join(DRIVER_KEY_SEPARATOR);
}

/**
 * Adds or removes one driver, which is what clicking a telemetry button means.
 *
 * Clicking a driver already on screen removes them; clicking a new one appends them. Appending past
 * the cap drops the driver added longest ago rather than refusing the click — a comparison is
 * something you sweep through the field with, and a button that silently did nothing would read as
 * broken.
 */
export function toggleDriverKey(current: readonly string[], driverKey: string): string[] {
  if (current.includes(driverKey)) {
    return current.filter((key) => key !== driverKey);
  }

  return [...current, driverKey].slice(-MAX_FOCUSED_DRIVERS);
}
