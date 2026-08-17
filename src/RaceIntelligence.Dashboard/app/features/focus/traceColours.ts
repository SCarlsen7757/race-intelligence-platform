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

/**
 * One colour per wheel, in the wire's order — FL, FR, RL, RR.
 *
 * They live here under the same constraint the input colours do, and mirror the `--wheel-*` tokens
 * in `styles.css` for the same reason: the numbers beside a tyre chart are DOM and take their
 * colour from the stylesheet, while the lines are canvas and cannot read a custom property. A wheel
 * whose label and line disagreed would be worse than no colour at all.
 *
 * Chosen so the two ends of an axle are far apart in hue rather than merely in lightness, because
 * the thing being read off these charts is one corner diverging from the others.
 */
export const WHEEL_COLOURS = ['#6fa8ff', '#ffb454', '#4fd1c5', '#f472b6'] as const;
