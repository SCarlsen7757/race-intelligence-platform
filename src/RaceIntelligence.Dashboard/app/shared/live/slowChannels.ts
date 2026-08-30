import type { OperatingWindow, OperatingWindowRow } from './contracts';

/**
 * A slow-channel reading as a number a ring can hold, with absence as NaN.
 *
 * `TraceBuffer` is a `Float32Array`, so it cannot hold `undefined` — NaN is how a hole is written,
 * and `toNullableArray` turns it back into the `null` uPlot needs to break a line.
 *
 * **This is all that is left of the sentinel handling this module used to do.** Every channel on the
 * slow wire was a raw simulator number, so a reader had to know that RaceRoom writes `-1` for "not
 * available" and that a brake at −1 °C is not a cold brake, a pressure at −1 is not a flat tyre and
 * a damage value at −1 is not a destroyed engine. The connector translates all of it now, once, at
 * the only place that knows what a negative means — so a negative reaching here is a real reading
 * and passing it through is correct.
 */
export function orNaN(value: number | null | undefined): number {
  if (value === undefined || value === null || !Number.isFinite(value)) {
    return Number.NaN;
  }

  return value;
}

/**
 * The tyre band for one corner, in the shape a chart draws it in.
 *
 * The wire sends one row per corner and compound; a widget has a corner and wants the bounds. A
 * missing row is an empty window rather than an invented one — a band built from a nominal value
 * tells an engineer their tyres are cold with the same confidence the simulator would have used to
 * tell them the truth.
 */
export function tyreWindow(
  windows: readonly OperatingWindowRow[] | undefined,
  corner: number,
): OperatingWindow {
  const row = windows?.find((candidate) => candidate.corner === corner);
  return {
    optimal: row?.tyreOptimalCelsius ?? null,
    cold: row?.tyreColdCelsius ?? null,
    hot: row?.tyreHotCelsius ?? null,
  };
}

/** The brake band for one corner. See {@link tyreWindow}; a brake has one reading, not three. */
export function brakeWindow(
  windows: readonly OperatingWindowRow[] | undefined,
  corner: number,
): OperatingWindow {
  const row = windows?.find((candidate) => candidate.corner === corner);
  return {
    optimal: row?.brakeOptimalCelsius ?? null,
    cold: row?.brakeColdCelsius ?? null,
    hot: row?.brakeHotCelsius ?? null,
  };
}
