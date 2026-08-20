import { useEffect, useRef } from 'react';
import type { TreadTemperatures } from '../../shared/live/contracts';
import { NOT_REPORTED, formatNumber } from '../../shared/format/format';
import type { ChannelPanelProps } from '../../sims/registry';
import { ChannelLegend } from './ChannelLegend';
import { firstReportedWindow } from './operatingWindow';
import { TREAD_HEAT_COLOURS } from './traceColours';
import { WHEEL_CHANNELS } from './WheelTrace';

/**
 * The three readings across one tyre's tread, outer edge first.
 *
 * Ordered outer → middle → inner rather than the wire's inner-first order, because this is a
 * picture of a car and the cells are laid out left to right across the tread. Which end of that row
 * is the car's centre depends on which side the tyre is on — see {@link CORNERS}.
 */
const TREAD = ['outer', 'middle', 'inner'] as const;

type TreadPosition = (typeof TREAD)[number];

/**
 * Where each corner sits, and which way its tread runs.
 *
 * `mirrored` is the detail that makes this a diagram of a car rather than a table with rounded
 * corners: **inner means "toward the centre of the car"**, so on the left-hand tyres the inner
 * shoulder is on the right of the cell row and on the right-hand tyres it is on the left. Drawn
 * without the mirror, a car with both inner shoulders cooking would show its two hot edges on
 * opposite sides of the figure, which is the one reading this widget exists to make obvious.
 */
const CORNERS = [
  { index: 0, id: 'fl', label: 'FL', mirrored: true },
  { index: 1, id: 'fr', label: 'FR', mirrored: false },
  { index: 2, id: 'rl', label: 'RL', mirrored: true },
  { index: 3, id: 'rr', label: 'RR', mirrored: false },
] as const;

/** Identifies one cell across renders, so the paint loop can find the node the last render made. */
function cellKey(wheel: number, tread: TreadPosition): string {
  return `${wheel}:${tread}`;
}

/** Every cell the figure holds, in a fixed order the paint loop can walk without allocating. */
const CELLS: readonly { key: string; wheel: number; tread: TreadPosition }[] = CORNERS.flatMap(
  (corner) =>
    TREAD.map((tread) => ({ key: cellKey(corner.index, tread), wheel: corner.index, tread })),
);

/** One cell's nodes, plus what was last written to them. */
interface CellNodes {
  node: HTMLDivElement | null;
  value: HTMLSpanElement | null;
  /** What was last written, so a frame that changed nothing touches no DOM at all. */
  painted: string | null;
  paintedColour: string | null;
}

/**
 * Where a reading sits between cold and hot, as 0..1, or null when it cannot be placed.
 *
 * Null rather than a guess is the important half. Without a window there is no answer to "is 84 °C
 * hot" that the dashboard is entitled to invent — a nominal range picked here would be a number
 * this code made up, rendered in the same colours as one the simulator reported.
 */
function position(value: number | null | undefined, cold: number | null, hot: number | null) {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return null;
  }

  if (cold === null || hot === null || hot <= cold) {
    return null;
  }

  return Math.min(1, Math.max(0, (value - cold) / (hot - cold)));
}

/** The heat colour for a 0..1 position. Five bands picked by index, not a continuous ramp. */
function heatColour(fraction: number): string {
  const index = Math.min(
    TREAD_HEAT_COLOURS.length - 1,
    Math.floor(fraction * TREAD_HEAT_COLOURS.length),
  );

  return TREAD_HEAT_COLOURS[index]!;
}

/**
 * Every tyre's tread temperature at once, laid out as the car.
 *
 * **Twelve numbers arranged so an imbalance is something you see rather than something you read.**
 * The middle of the tread — the one reading the stint charts draw — answers "is this tyre warm".
 * The spread across it answers why: a front left twenty degrees hotter on its inner shoulder than
 * its outer is too much negative camber or too much pressure, stated in a way no single number can.
 * That is handover item 4, and it is the reason the wire was widened to carry all six readings.
 *
 * ### Why this paints itself rather than rendering
 *
 * Tread temperatures arrive on the focus frame, sixty times a second. Rendering them through React
 * would be sixty renders a second for twelve numbers, which is exactly what the store's plain-field
 * design exists to prevent — so the structure below is rendered once and the *contents* are written
 * from a `requestAnimationFrame` loop, the same arrangement `LiveReadout` and `AssistIndicator` use.
 *
 * It is not a `LiveChart` because it is not a time series. There is no x axis here: this is the
 * car at one instant, and a uPlot lifecycle would be machinery for a shape it does not have.
 * `LapTrend` reached the opposite conclusion for the opposite reason — it *is* a series, but one
 * that changes once a lap, so plain React was the honest answer there.
 *
 * ### Without a window, no colours
 *
 * The scale comes from the simulator's own cold and hot thresholds. When it reports none, the cells
 * stay neutral and show their numbers: a heatmap needs a definition of hot, and inventing one would
 * dress a guess up as a measurement.
 */
export function TyreHeatmap({
  store,
  driverKey,
  hiddenChannels,
  onToggleChannel,
}: ChannelPanelProps) {
  // Written only from ref callbacks and read only from the paint loop, never during a render —
  // which is both what `react-hooks/refs` requires and what keeps the frame rate off React.
  const cellsRef = useRef<Map<string, CellNodes>>(new Map());

  // Held in a ref for the same reason the readouts hold their formatters: the loop must not restart
  // because somebody switched a corner off.
  const hiddenRef = useRef<ReadonlySet<string>>(new Set(hiddenChannels));
  useEffect(() => {
    hiddenRef.current = new Set(hiddenChannels);
  });

  useEffect(() => {
    let frame = 0;

    const paint = () => {
      // The stint frame, not the focus frame: tread temperatures moved to their own roughly 1 Hz
      // channel. Still painted from the loop rather than through React — the per-cell guard below
      // means an unchanged reading writes nothing, so a slower source costs a comparison and no
      // DOM work, and the loop stays the one place this figure is drawn.
      const latest = store.stintFor(driverKey);
      const corners: (TreadTemperatures | undefined)[] = latest?.tyreTemperatureCelsius ?? [];
      const window = firstReportedWindow(corners);

      for (const cell of CELLS) {
        const nodes = cellsRef.current.get(cell.key);
        if (nodes === undefined) {
          continue;
        }

        const reading = corners[cell.wheel]?.[cell.tread];
        const text =
          reading === null || reading === undefined ? NOT_REPORTED : formatNumber(reading, 0);

        if (text !== nodes.painted && nodes.value !== null) {
          nodes.value.textContent = text;
          nodes.painted = text;
        }

        const fraction = position(reading, window?.cold ?? null, window?.hot ?? null);
        // Transparent rather than a neutral fill, so the cell falls back to the stylesheet's own
        // surface instead of this module owning a second opinion about the panel background.
        const colour = fraction === null ? 'transparent' : heatColour(fraction);

        if (colour !== nodes.paintedColour && nodes.node !== null) {
          nodes.node.style.background = colour;
          nodes.paintedColour = colour;
        }
      }

      frame = requestAnimationFrame(paint);
    };

    frame = requestAnimationFrame(paint);

    return () => cancelAnimationFrame(frame);
  }, [store, driverKey]);

  /** Registers one cell's nodes. Runs after render, which is why writing the ref here is allowed. */
  const register = (key: string, part: 'node' | 'value', element: HTMLElement | null) => {
    const existing = cellsRef.current.get(key) ?? {
      node: null,
      value: null,
      painted: null,
      paintedColour: null,
    };

    if (part === 'node') {
      existing.node = element as HTMLDivElement | null;
      // The node is new, so whatever it was last painted is no longer on screen. Without this a
      // remount would keep the old bookkeeping and skip the write that fills the cell back in.
      existing.paintedColour = null;
    } else {
      existing.value = element;
      existing.painted = null;
    }

    cellsRef.current.set(key, existing);
  };

  const hiddenIds = new Set(hiddenChannels);

  return (
    <div className="tyre-heatmap">
      <div className="tyre-heatmap__car">
        {CORNERS.map((corner) => {
          const off = hiddenIds.has(corner.id);
          const treads = corner.mirrored ? [...TREAD].reverse() : TREAD;

          return (
            <div
              key={corner.id}
              className={`tyre-heatmap__corner${off ? ' tyre-heatmap__corner--off' : ''}`}
            >
              <span className="tyre-heatmap__corner-label">{corner.label}</span>

              <div className="tyre-heatmap__tread">
                {treads.map((tread) => {
                  const key = cellKey(corner.index, tread);

                  return (
                    <div
                      key={tread}
                      className="tyre-heatmap__cell"
                      title={`${corner.label} ${tread}`}
                      ref={(node) => {
                        register(key, 'node', node);
                      }}
                    >
                      <span
                        className="tyre-heatmap__value"
                        ref={(node) => {
                          register(key, 'value', node);
                        }}
                      >
                        {NOT_REPORTED}
                      </span>
                    </div>
                  );
                })}
              </div>
            </div>
          );
        })}
      </div>

      <ChannelLegend
        channels={WHEEL_CHANNELS}
        hidden={hiddenChannels}
        onToggle={onToggleChannel}
        unit="°C"
      />
    </div>
  );
}
