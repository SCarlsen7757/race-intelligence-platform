/**
 * jsdom implements no `matchMedia`, and uPlot calls it at module load to pick a device pixel
 * ratio. Without this stub, merely importing anything that reaches the focus panel fails before a
 * single test runs.
 *
 * A stub rather than a real implementation: nothing under test depends on the media query's
 * answer, only on the call not throwing.
 */
if (typeof window !== 'undefined' && typeof window.matchMedia !== 'function') {
  window.matchMedia = (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  });
}

/**
 * jsdom ships no canvas implementation, so `getContext` logs a "Not implemented" error and returns
 * null. Both painters that matter here — the uPlot trace and the pedal bars — are canvas, so
 * without a stub every focus-panel test drowns its real output in that message.
 *
 * A recording no-op rather than a real rasteriser: nothing under test asserts on pixels, only that
 * the paint loops run and stop. `measureText` is the one call that must return something, because
 * uPlot lays its axis out from the result.
 */
if (typeof HTMLCanvasElement !== 'undefined') {
  const noop = () => undefined;
  const stub = function getContext(this: HTMLCanvasElement) {
    const context: Record<string, unknown> = { canvas: this };
    return new Proxy(context, {
      get: (target, property) =>
        property in target
          ? target[property as string]
          : property === 'measureText'
            ? () => ({ width: 0 })
            : noop,
    });
  };

  HTMLCanvasElement.prototype.getContext =
    stub as unknown as typeof HTMLCanvasElement.prototype.getContext;
}

/**
 * jsdom implements no `scrollTo` either, and the router's scroll restoration calls it on every
 * navigation. Left alone it logs a "Not implemented" error per test, which buries real ones.
 */
window.scrollTo = () => {};

/**
 * jsdom does not run animation frames, so the paint loops that drive the focus panel would never
 * fire and would also never stop — leaving a pending callback that keeps the test process alive.
 * Scheduling them as timers makes them observable and cancellable.
 */
if (typeof globalThis.requestAnimationFrame !== 'function') {
  globalThis.requestAnimationFrame = (callback: FrameRequestCallback) =>
    setTimeout(() => callback(performance.now()), 16) as unknown as number;
  globalThis.cancelAnimationFrame = (handle: number) =>
    clearTimeout(handle as unknown as NodeJS.Timeout);
}
