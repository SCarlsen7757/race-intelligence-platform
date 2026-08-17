/**
 * The colours the input traces and bars are painted in.
 *
 * Canvas cannot read a CSS custom property, and uPlot wants a concrete stroke, so these have to
 * exist as JavaScript values somewhere. Keeping them in one module rather than inline in each
 * painter is what stops the trace and the bar for the same channel drifting apart — a throttle
 * line and a throttle bar in two different greens would read as two different measurements.
 *
 * They mirror the `--trace-*` tokens in `styles.css`; the stylesheet is the one a designer would
 * edit, and these must be changed with it.
 */
export const TRACE_COLOURS = {
  throttle: '#3ddc84',
  brake: '#ff5c5c',
  clutch: '#ffc35c',
  steering: '#5aa9ff',
  axis: '#8b93a7',
  grid: '#1e2433',
  track: '#1d2432',
} as const;
