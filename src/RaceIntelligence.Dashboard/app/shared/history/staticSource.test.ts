import { describe, expect, it } from 'vitest';
import { StaticChartSource } from './staticSource';

describe('StaticChartSource', () => {
  it('reports the length of what it was given', () => {
    const source = new StaticChartSource([1, 2, 3]);

    expect(source.length).toBe(3);
  });

  it('never changes version, so the paint loop draws it once', () => {
    const source = new StaticChartSource([1, 2, 3]);
    const before = source.version;

    source.toNullableArray();

    // The whole reason a static source is cheap: reading it is not a change.
    expect(source.version).toBe(before);
  });

  it('reuses the array it is handed when the size matches', () => {
    const source = new StaticChartSource([1, 2, 3]);
    const target: (number | null)[] = [0, 0, 0];

    const result = source.toNullableArray(target);

    // The contract TraceBuffer honours, and the reason the paint loop does not allocate per frame.
    expect(result).toBe(target);
    expect(result).toEqual([1, 2, 3]);
  });

  it('allocates when the array it is handed is the wrong size', () => {
    const source = new StaticChartSource([1, 2, 3]);
    const target: (number | null)[] = [0];

    const result = source.toNullableArray(target);

    expect(result).not.toBe(target);
    expect(result).toEqual([1, 2, 3]);
  });

  describe('fromSamples', () => {
    const samples = [
      { speed: 40, throttle: 1 },
      { speed: 45, throttle: 0.5 },
    ];

    it('builds one source per channel', () => {
      const sources = StaticChartSource.fromSamples(samples, {
        speed: (s) => s.speed,
        throttle: (s) => s.throttle,
      });

      expect(sources.speed.toNullableArray()).toEqual([40, 45]);
      expect(sources.throttle.toNullableArray()).toEqual([1, 0.5]);
    });

    it('turns an unreported channel into a gap rather than a zero', () => {
      const withGaps = [{ brake: 0.5 }, { brake: undefined }, { brake: 0.25 }];

      const sources = StaticChartSource.fromSamples(withGaps, { brake: (s) => s.brake });

      // null draws as a gap; zero would draw a car that came off the brakes.
      expect(sources.brake.toNullableArray()).toEqual([0.5, null, 0.25]);
    });

    it('preserves a real zero', () => {
      const sources = StaticChartSource.fromSamples([{ brake: 0 }], { brake: (s) => s.brake });

      expect(sources.brake.toNullableArray()).toEqual([0]);
    });

    it('handles an empty sample list', () => {
      const sources = StaticChartSource.fromSamples([] as { speed: number }[], {
        speed: (s) => s.speed,
      });

      expect(sources.speed.length).toBe(0);
      expect(sources.speed.toNullableArray()).toEqual([]);
    });
  });
});
