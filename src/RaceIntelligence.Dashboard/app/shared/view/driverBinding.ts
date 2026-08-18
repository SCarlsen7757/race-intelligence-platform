/**
 * How a saved wall says whose car a tile is about.
 *
 * In `shared` rather than beside the comparison column that supplies the cars, because the wall
 * document depends on this and `shared` must not reach back into `features` to find it. What lives
 * here is the vocabulary and the lookup; who is actually in slot two is the session's business.
 */

/**
 * A tile's subject, by position rather than by key.
 *
 * **Never a driver key.** A key like `id:4242` names one car in one session and nobody in the next,
 * and the wall it is written into is opened against every session of that simulator — so a
 * persisted key would resolve to a stranger or to nothing. Position survives that, and it is what
 * lets a wall exported from one race open correctly in someone else's.
 *
 * The two forms answer different questions. `'selected'` is "whichever car I am looking at", and it
 * is what makes a compact wall work across a whole field: one set of tyre, brake and input tiles
 * that follows the car you click in the tower. A slot is "the second car", and it is what makes a
 * comparison — two tiles of the same channel, pinned to two positions, side by side.
 */
export type WallDriverBinding = 'selected' | { slot: number };

/** Slots are numbered as they read: the first car being watched is slot 1. */
export const FIRST_SLOT = 1;

/**
 * Whether a persisted binding is one this build understands.
 *
 * Strict about the slot number, because a slot indexes the watched cars and a fractional or
 * negative one would resolve to nothing in a way that looks like an empty slot rather than like a
 * corrupt document.
 */
export function isDriverBinding(value: unknown): value is WallDriverBinding {
  if (value === 'selected') {
    return true;
  }

  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const slot = (value as { slot?: unknown }).slot;
  return typeof slot === 'number' && Number.isInteger(slot) && slot >= FIRST_SLOT;
}

/**
 * The car a binding refers to, or undefined when there is not one.
 *
 * Undefined is a routine answer rather than an error. A wall saved with three slots, opened against
 * a session where two cars are being watched, has a tile with nobody in it — and the tile says so.
 * Sliding it onto the nearest car instead would put one driver's numbers under another driver's
 * heading, which is the one failure a race engineer has no way to catch.
 */
export function resolveBinding(
  binding: WallDriverBinding | undefined,
  comparedDriverKeys: readonly string[],
  selectedDriverKey: string | null,
): string | undefined {
  if (binding === undefined) {
    return undefined;
  }

  if (binding === 'selected') {
    return selectedDriverKey ?? undefined;
  }

  return comparedDriverKeys[binding.slot - FIRST_SLOT];
}
