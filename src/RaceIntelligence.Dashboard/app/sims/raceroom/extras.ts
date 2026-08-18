import type { RaceRoomExtras } from '../../shared/live/contracts';

/**
 * Reads one RaceRoom extras document, or gives up quietly.
 *
 * Extras are an opaque, connector-defined blob rather than a typed message, so parsing them is the
 * one place in the live path that can meet text it cannot use. One malformed payload is not a
 * reason to take a focus panel down with it — every panel here treats "no document" and "a document
 * that will not parse" the same way, as nothing reported.
 *
 * Shared by the RaceRoom panels so there is one answer to that question rather than one per panel.
 */
export function parseExtras(extrasJson: string | null): RaceRoomExtras | null {
  if (extrasJson === null) {
    return null;
  }

  try {
    return JSON.parse(extrasJson) as RaceRoomExtras;
  } catch {
    return null;
  }
}
