import { useEffect, useRef } from 'react';
import type { FocusTraces, LiveStore } from '../../shared/live/store';
import { TRACE_COLOURS } from './traceColours';

interface PedalBarsProps {
  store: LiveStore;
  /** Which driver's stream to paint. Two can be on screen at once. */
  driverKey: string;
  /**
   * The shortest the bars may be drawn, in pixels.
   *
   * A floor, not a height — the height is the tile's. Only reached for the frame before layout has
   * run, and for a tile the grid has briefly collapsed; a canvas painted at zero pixels shows
   * nothing.
   */
  minHeight?: number;
}

/** The three pedals, left to right in the order they sit in a footwell. */
const PEDALS = [
  { label: 'CLU', colour: TRACE_COLOURS.clutch, read: (t: FocusTraces) => t.clutch.last() },
  { label: 'BRK', colour: TRACE_COLOURS.brake, read: (t: FocusTraces) => t.brake.last() },
  { label: 'THR', colour: TRACE_COLOURS.throttle, read: (t: FocusTraces) => t.throttle.last() },
] as const;

/**
 * How wide one bar is relative to the gap beside it, and the size that pairing is drawn at when
 * there is room for it.
 *
 * Was a hard 26 and 14 pixels. Kept as the *preferred* widths and scaled down when three bars plus
 * their gaps will not fit — a tile dragged narrow drew three bars off the side of its own canvas,
 * because the centring arithmetic below cannot produce a negative left edge.
 */
const BAR_WIDTH = 26;
const BAR_GAP = 14;
const LABEL_HEIGHT = 14;
const STEER_HEIGHT = 10;
const STEER_GAP = 10;

/**
 * Instantaneous pedal positions and steering angle, beside the rolling trace.
 *
 * The trace answers "what did the driver do through that corner"; the bars answer "what is the
 * driver doing right now", which is the question being asked while watching a car brake into
 * turn one. Both are the same numbers — the bars read the newest sample out of the same ring
 * buffers the trace plots — so they can never disagree.
 *
 * **Painted from a `requestAnimationFrame` loop, like everything else on the focus stream.** This
 * component renders once. A bar bound to React state would be sixty renders a second for a
 * rectangle.
 *
 * **A pedal the simulator does not report is drawn as a hatched track, not as an empty one.** The
 * store pushes NaN for exactly that case, and an empty bar would claim the driver's foot is off
 * the pedal — the same lie the trace avoids with `spanGaps: false`.
 */
export function PedalBars({ store, driverKey, minHeight = 64 }: PedalBarsProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (canvas === null) {
      return;
    }

    const context = canvas.getContext('2d');
    if (context === null) {
      return;
    }

    // Resolved once: the rings for a driver are created on demand and then live as long as the
    // subscription does, so the paint loop never has to look them up again.
    const traces = store.tracesFor(driverKey);

    let frame = 0;
    let cssWidth = 0;
    let cssHeight = 0;

    const resize = () => {
      const ratio = window.devicePixelRatio || 1;
      cssWidth = canvas.clientWidth;
      // The tile's height, not a number chosen here. A canvas has no intrinsic height, so the
      // stylesheet gives it the slack in the widget's flex column and this reads it back.
      cssHeight = Math.max(minHeight, canvas.clientHeight);
      canvas.width = Math.max(1, Math.round(cssWidth * ratio));
      canvas.height = Math.max(1, Math.round(cssHeight * ratio));
      // Reset rather than accumulate: setTransform replaces, scale would compound on every resize.
      context.setTransform(ratio, 0, 0, ratio, 0, 0);
    };

    const paintHatch = (x: number, y: number, width: number, barHeight: number) => {
      context.save();
      context.beginPath();
      context.rect(x, y, width, barHeight);
      context.clip();
      context.strokeStyle = TRACE_COLOURS.axis;
      context.globalAlpha = 0.35;
      context.lineWidth = 1;

      for (let offset = -barHeight; offset < width + barHeight; offset += 6) {
        context.beginPath();
        context.moveTo(x + offset, y + barHeight);
        context.lineTo(x + offset + barHeight, y);
        context.stroke();
      }

      context.restore();
    };

    const paint = () => {
      context.clearRect(0, 0, cssWidth, cssHeight);

      // The three fixed strips below the bars keep their pixels — a label is a label at any tile
      // size — and the bars take everything above them, which is what makes a taller tile a taller
      // bar rather than a bar with space over it.
      const barHeight = Math.max(1, cssHeight - LABEL_HEIGHT - STEER_HEIGHT - STEER_GAP);

      // Scaled down when three bars and two gaps will not fit the width. Without this the centring
      // below clamps at zero and the right-hand bar is drawn past the edge of the canvas.
      const preferred = PEDALS.length * BAR_WIDTH + (PEDALS.length - 1) * BAR_GAP;
      const scale = Math.min(1, cssWidth / preferred);
      const barWidth = BAR_WIDTH * scale;
      const barGap = BAR_GAP * scale;
      const totalWidth = preferred * scale;
      const left = Math.max(0, (cssWidth - totalWidth) / 2);

      context.font = '10px "Segoe UI", system-ui, sans-serif';
      context.textAlign = 'center';
      context.textBaseline = 'top';

      PEDALS.forEach((pedal, index) => {
        const x = left + index * (barWidth + barGap);
        const value = pedal.read(traces);

        context.fillStyle = TRACE_COLOURS.track;
        context.fillRect(x, 0, barWidth, barHeight);

        if (Number.isNaN(value)) {
          paintHatch(x, 0, barWidth, barHeight);
        } else {
          const clamped = Math.min(1, Math.max(0, value));
          const filled = Math.round(barHeight * clamped);
          context.fillStyle = pedal.colour;
          context.fillRect(x, barHeight - filled, barWidth, filled);
        }

        context.fillStyle = TRACE_COLOURS.axis;
        context.fillText(pedal.label, x + barWidth / 2, barHeight + 3);
      });

      // Steering as a bar growing out of centre, because what a race engineer reads off it is
      // which way and how much, not an absolute position on a scale.
      const steerY = barHeight + LABEL_HEIGHT + STEER_GAP;
      const steerLeft = left;
      const steerWidth = totalWidth;
      const centre = steerLeft + steerWidth / 2;

      context.fillStyle = TRACE_COLOURS.track;
      context.fillRect(steerLeft, steerY, steerWidth, STEER_HEIGHT);

      const steering = traces.steering.last();
      if (Number.isNaN(steering)) {
        paintHatch(steerLeft, steerY, steerWidth, STEER_HEIGHT);
      } else {
        const clamped = Math.min(1, Math.max(-1, steering));
        const extent = (steerWidth / 2) * clamped;
        context.fillStyle = TRACE_COLOURS.steering;
        context.fillRect(
          extent >= 0 ? centre : centre + extent,
          steerY,
          Math.abs(extent),
          STEER_HEIGHT,
        );
      }

      context.fillStyle = TRACE_COLOURS.axis;
      context.fillRect(centre - 0.5, steerY - 2, 1, STEER_HEIGHT + 4);

      frame = requestAnimationFrame(paint);
    };

    resize();
    frame = requestAnimationFrame(paint);

    // A `ResizeObserver` rather than the `window.resize` listener this had, and rather than the
    // `clientWidth` check the paint loop used to open with. Both were wrong in their own way: the
    // window listener never fires when a widget is dragged wider on the pit wall, and reading
    // `clientWidth` every frame forces the browser to flush layout sixty times a second to answer a
    // question whose answer almost never changes. The observer is told.
    const observer = new ResizeObserver(resize);
    observer.observe(canvas);

    return () => {
      cancelAnimationFrame(frame);
      observer.disconnect();
    };
  }, [store, driverKey, minHeight]);

  return (
    <canvas
      ref={canvasRef}
      className="pedals"
      role="img"
      aria-label="Clutch, brake and throttle position, and steering angle"
    />
  );
}
