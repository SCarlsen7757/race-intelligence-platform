import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useAge } from './useAge';

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date('2026-08-18T12:00:00.000Z'));
});

afterEach(() => {
  vi.useRealTimers();
});

describe('useAge', () => {
  it('advances the formatted string on the tick, with no change to the timestamp itself', () => {
    const isoUtc = '2026-08-18T11:59:00.000Z';
    const { result } = renderHook(() => useAge(isoUtc));

    expect(result.current).toBe('1m ago');

    act(() => {
      vi.advanceTimersByTime(60_000);
    });

    expect(result.current).toBe('2m ago');
  });

  it('runs a single interval for several mounted consumers', () => {
    const { unmount: unmountA } = renderHook(() => useAge('2026-08-18T11:59:00.000Z'));
    const { unmount: unmountB } = renderHook(() => useAge('2026-08-18T11:58:00.000Z'));
    const { unmount: unmountC } = renderHook(() => useAge('2026-08-18T11:57:00.000Z'));

    expect(vi.getTimerCount()).toBe(1);

    unmountA();
    unmountB();
    unmountC();
  });

  it('clears the interval once the last consumer unmounts', () => {
    const { unmount } = renderHook(() => useAge('2026-08-18T11:59:00.000Z'));

    expect(vi.getTimerCount()).toBe(1);

    unmount();

    expect(vi.getTimerCount()).toBe(0);
  });

  it('leaves the clock running for the remaining consumers when one unmounts', () => {
    const first = renderHook(() => useAge('2026-08-18T11:59:00.000Z'));
    const second = renderHook(() => useAge('2026-08-18T11:58:00.000Z'));

    first.unmount();

    expect(vi.getTimerCount()).toBe(1);

    act(() => {
      vi.advanceTimersByTime(120_000);
    });

    expect(second.result.current).toBe('4m ago');

    second.unmount();
  });
});
