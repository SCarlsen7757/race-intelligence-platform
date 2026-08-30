import type { LiveChartSource } from '../../features/focus/LiveChart';

/**
 * A {@link LiveChartSource} over numbers that are already all there.
 *
 * **This is the seam issue #69 was betting on.** `LiveChart` asks a source for three things — a
 * version, a length, and a way to read the values into a reused array — and names that interface
 * structural on purpose, so the lap-delta chart (whose numbers are computed rather than pushed)
 * could drive an identical chart. A stored lap is the same situation one step further out: the
 * numbers were fixed before the page loaded.
 *
 * So a history chart is not a second charting stack. It is this class and a spec.
 *
 * The version is a constant. The paint loop repaints only when the version changes, so a chart over
 * a static source draws once and then costs a comparison per frame — which is the correct amount of
 * work for a picture that cannot change.
 */
export class StaticChartSource implements LiveChartSource {
  /**
   * Never changes, which is the whole point: the paint loop compares this and skips the repaint.
   *
   * Zero rather than a counter because there is no second value it could ever take. A source whose
   * numbers can change is a ring, and rings already exist.
   */
  readonly version = 0;

  private readonly values: readonly (number | null)[];

  constructor(values: readonly (number | null)[]) {
    this.values = values;
  }

  /**
   * Builds one source per channel from a list of samples, in a single pass.
   *
   * One pass rather than one per channel because the sample list is the expensive thing to walk —
   * a lap is tens of thousands of objects — and every chart wants several channels off the same
   * list.
   */
  static fromSamples<T, K extends string>(
    samples: readonly T[],
    channels: Readonly<Record<K, (sample: T) => number | null | undefined>>,
  ): Record<K, StaticChartSource> {
    const keys = Object.keys(channels) as K[];
    const columns = new Map<K, (number | null)[]>(
      keys.map((key) => [key, new Array<number | null>(samples.length)]),
    );

    for (let i = 0; i < samples.length; i++) {
      const sample = samples[i]!;
      for (const key of keys) {
        // `undefined` becomes `null`, deliberately and not as a formality. The read API omits a
        // channel the simulator did not report, and uPlot draws `null` as a gap — which is the
        // truth. Substituting zero would draw a car that lifted off the throttle.
        columns.get(key)![i] = channels[key](sample) ?? null;
      }
    }

    return Object.fromEntries(
      keys.map((key) => [key, new StaticChartSource(columns.get(key)!)]),
    ) as Record<K, StaticChartSource>;
  }

  get length(): number {
    return this.values.length;
  }

  /**
   * Reads the values out, into the caller's array where it is the right size.
   *
   * The same contract `TraceBuffer` honours, including reusing the caller's array — the paint loop
   * hands back the array it was given last frame, and allocating a fresh one per frame per series
   * is the allocation this interface exists to avoid.
   */
  toNullableArray(into?: (number | null)[]): (number | null)[] {
    const target =
      into !== undefined && into.length === this.values.length
        ? into
        : new Array<number | null>(this.values.length);

    for (let i = 0; i < this.values.length; i++) {
      target[i] = this.values[i]!;
    }

    return target;
  }
}
