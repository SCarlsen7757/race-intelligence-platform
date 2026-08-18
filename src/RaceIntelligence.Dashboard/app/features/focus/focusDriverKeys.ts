/**
 * Which cars are being watched at full rate.
 *
 * This used to be about the URL. `/rooms/abc/id:42,id:7` named the room and the pair of drivers,
 * and that was the promise: a comparison you could paste into a message. The pit wall retires it.
 * A wall is a layout the user arranged and saved per simulator, not a path — so the cars being
 * watched are runtime state belonging to the room, the arrangement is a document, and what used to
 * be shareable by link is now shareable by exporting the view.
 *
 * What survives is the part that was never about the URL: a cap that mirrors the hub's. How a wall
 * tile names one of these cars lives in `shared/view/driverBinding.ts`, because the saved document
 * depends on it and must not reach into a feature to find it.
 */

/**
 * How many drivers can be watched at once.
 *
 * Mirrors the hub's `LiveViewer.MaxFocusDrivers`, which is the one that actually enforces it: the
 * viewing endpoint is open, so the bound has to hold against a client that is not this dashboard.
 * Keeping the same number here means a viewer never sends a request it knows will be refused.
 */
export const MAX_FOCUSED_DRIVERS = 2;

/**
 * Adds or removes one driver, which is what clicking a telemetry button means.
 *
 * Clicking a driver already on screen removes them; clicking a new one appends them. Appending past
 * the cap drops the driver added longest ago rather than refusing the click — a comparison is
 * something you sweep through the field with, and a button that silently did nothing would read as
 * broken.
 *
 * Because this is now the only way a car enters the watched set, it is also what keeps that set
 * inside the hub's cap: the follow set is exactly this list, so there is no second path by which a
 * request the hub would refuse could be sent.
 */
export function toggleDriverKey(current: readonly string[], driverKey: string): string[] {
  if (current.includes(driverKey)) {
    return current.filter((key) => key !== driverKey);
  }

  return [...current, driverKey].slice(-MAX_FOCUSED_DRIVERS);
}
