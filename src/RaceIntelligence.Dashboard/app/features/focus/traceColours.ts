/**
 * CHANNELS are owned solely by this module because canvas painters need concrete colour values.
 *
 * Canvas cannot read a CSS custom property, and uPlot wants a concrete stroke, so these have to
 * exist as JavaScript values somewhere. Keeping them in one module rather than inline in each
 * painter is what stops the trace and the bar for the same channel drifting apart — a throttle
 * line and a throttle bar in two different greens would read as two different measurements.
 *
 * FLAGS and CHROME remain stylesheet concerns: coupling either to a telemetry channel would let an
 * unrelated interface restyle repaint a measurement. GROUND remains here only where canvas needs
 * it; axis and track are kept under an explicit cross-file contract because canvas cannot resolve
 * their CSS counterparts.
 */
export const TRACE_COLOURS = {
  // CHANNELS: this module is the single source of truth for every input painter.
  throttle: '#3ddc84',
  brake: '#ff5c5c',
  clutch: '#ffc35c',
  steering: '#5aa9ff',

  // CHANNELS the inputs trace adds. Speed, gear and RPM each carry their own units and so their own
  // scale, which is exactly why they need their own hues too: four lines sharing an axis are told
  // apart by position, and three that do not share one can only be told apart by colour.
  speed: '#e8eaf0',
  gear: '#b388ff',
  rpm: '#ff9f45',

  // The assist markers, drawn as a baseline near the foot of the plot rather than as a channel.
  // Deliberately quieter than any measured line: an assist intervening is context for the pedal
  // trace above it, not a reading competing with it.
  abs: '#7dd3fc',
  tractionControl: '#fbbf6e',

  // GROUND: cross-file tests stop axis and track retaining the colours CSS used to have.
  axis: '#8b93a7',
  grid: '#1f1f25',
  track: '#1e1e24',
} as const;

/**
 * One colour per wheel, in the wire's order — FL, FR, RL, RR.
 *
 * This module is their sole owner. Keeping the DOM swatches and canvas lines on this shared export
 * prevents a wheel whose label and line disagreed, which would be worse than no colour at all.
 *
 * Chosen so the two ends of an axle are far apart in hue rather than merely in lightness, because
 * the thing being read off these charts is one corner diverging from the others.
 */
export const WHEEL_COLOURS = ['#6fa8ff', '#ffb454', '#4fd1c5', '#f472b6'] as const;
