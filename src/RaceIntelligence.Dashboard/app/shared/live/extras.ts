import type { RaceRoomExtras } from './contracts';

/**
 * Reads one extras document, or gives up quietly.
 *
 * Extras are an opaque, connector-defined blob rather than a typed message, so parsing them is the
 * one place in the live path that can meet text it cannot use. One malformed payload is not a
 * reason to take the wall down with it — every reader treats "no document" and "a document that
 * will not parse" the same way, as nothing reported.
 *
 * **Called once per frame, by the store.** It used to be called once per panel per frame, which for
 * a wall of extras-fed tiles meant decoding the same bytes into the same object six or eight times
 * a second — and the whole reason extras have their own channel is that parsing them is not
 * supposed to happen more often than they arrive.
 *
 * Lives beside the type rather than under `sims/raceroom/`, because the store parses it and
 * `shared/` reaching into `sims/` would invert the layering.
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

/**
 * One raw extras number as a reading, or null when the simulator did not report one.
 *
 * **`-1` is "not available", not a value.** Extras cross the wire exactly as the connector wrote
 * them and nothing upstream translates the sentinel, so a reader that trusts the number tells a
 * race engineer the opposite of the truth twice over: once by claiming there is a reading, and once
 * by claiming the worst possible one. A brake at −1 °C is not a cold brake, a pressure at −1 is not
 * a flat tyre, and a damage value at −1 is not a destroyed engine.
 *
 * Negative rather than exactly `-1`, because the sentinel is a convention rather than a guarantee
 * and every channel this guards is non-negative by nature. `NaN` and `undefined` are folded in for
 * the same reason they are elsewhere: absent is absent, however it arrived.
 *
 * This is the one place that judgement is made. It began as `toCondition` inside the damage panel,
 * which was correct and had no reason to be private.
 */
export function reportedNumber(value: number | null | undefined): number | null {
  if (value === undefined || value === null || !Number.isFinite(value) || value < 0) {
    return null;
  }

  return value;
}

/**
 * The same reading as a number a ring can hold, with absence as NaN.
 *
 * `TraceBuffer` is a `Float32Array`, so it cannot hold null — NaN is how a hole is written, and
 * `toNullableArray` turns it back into the null uPlot needs to break a line. Every push of an
 * extras channel goes through here so that "absent is not zero" holds in the rings as well as in
 * the readouts.
 */
export function reportedOrNaN(value: number | null | undefined): number {
  return reportedNumber(value) ?? Number.NaN;
}
