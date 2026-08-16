/**
 * jsdom implements no `matchMedia`, and uPlot calls it at module load to pick a device pixel
 * ratio. Without this stub, merely importing anything that reaches the focus panel fails before a
 * single test runs.
 *
 * A stub rather than a real implementation: nothing under test depends on the media query's
 * answer, only on the call not throwing.
 */
if (typeof window !== 'undefined' && typeof window.matchMedia !== 'function') {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  })) as typeof window.matchMedia;
}

/**
 * jsdom does not run animation frames, so the paint loops that drive the focus panel would never
 * fire and would also never stop — leaving a pending callback that keeps the test process alive.
 * Scheduling them as timers makes them observable and cancellable.
 */
if (typeof globalThis.requestAnimationFrame !== 'function') {
  globalThis.requestAnimationFrame = ((callback: FrameRequestCallback) =>
    setTimeout(() => callback(performance.now()), 16) as unknown as number);
  globalThis.cancelAnimationFrame = ((handle: number) =>
    clearTimeout(handle as unknown as NodeJS.Timeout));
}
