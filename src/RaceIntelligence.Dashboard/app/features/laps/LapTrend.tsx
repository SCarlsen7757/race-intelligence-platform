import { useMemo } from 'react';
import { formatLapTime } from '../../shared/format/format';
import type { LapSummary } from '../../shared/live/store';
import { useLapSummaries } from '../../shared/live/useLive';
import { TRACE_COLOURS } from '../focus/traceColours';

/**
 * How many laps the rolling average looks back over.
 *
 * Five, because the question it answers is "what is this driver doing now" and a longer window
 * carries the first stint's traffic into the last one's clear air. Short enough to turn when the
 * pace does, long enough that one lap behind a slower car does not become the story.
 */
const ROLLING_WINDOW = 5;

/** Room around the plot for the axis labels, in the SVG's own units. */
const PADDING = { top: 8, right: 8, bottom: 8, left: 8 };
const VIEW_WIDTH = 300;
const VIEW_HEIGHT = 120;

/** A lap as this chart reads it: a time, whether it counted, and where it sits. */
interface Point {
  lapNumber: number;
  ms: number;
  /** Explicitly refused by the simulator. Undefined validity is unknown, and counts. */
  invalid: boolean;
  x: number;
  y: number;
}

/**
 * Whether a lap may set a best or move an average.
 *
 * **`valid === undefined` is unknown, not invalid.** `contracts.ts` says as much about personal
 * bests and the same holds here: a simulator that declines to comment on validity is not accusing
 * the driver of anything, and treating silence as a refusal would empty the chart for every
 * simulator that does not report the flag.
 */
function counts(lap: LapSummary): boolean {
  return lap.valid !== false && lap.lapTimeMs !== null && lap.lapTimeMs !== undefined;
}

/** The mean of the last {@link ROLLING_WINDOW} counting laps up to and including this one. */
function rollingAverage(counting: number[], index: number): number {
  const from = Math.max(0, index - ROLLING_WINDOW + 1);
  const window = counting.slice(from, index + 1);

  return window.reduce((total, ms) => total + ms, 0) / window.length;
}

interface LapTrendProps {
  driverKey: string;
}

/**
 * Every completed lap, the pace through them, and how tightly they cluster.
 *
 * The distinction this exists to draw is the one a single fastest lap hides: **a driver a second
 * slower and half a second more consistent wins the stint.** One purple lap says what a car can do
 * on its best tenth of an hour; the spread says what it will do for the other nine.
 *
 * Three things are drawn. Each counting lap as a point, so an outlier stays visible as an event
 * rather than being averaged into invisibility. A rolling mean through them, which is the pace.
 * And a band at one standard deviation around that mean, which is the consistency — a band that
 * narrows over a stint is a driver settling in, and one that widens is usually traffic or a tyre
 * going away.
 *
 * **Invalid laps are plotted and then ignored.** They happened, and a gap where a lap was deleted
 * would leave a stint that reads as shorter than it was — but letting one set the best or drag the
 * average would report a time that officially never existed.
 *
 * Rendered as plain SVG through React, and that is deliberate rather than an oversight: this
 * changes once a lap. The canvas-and-`requestAnimationFrame` machinery the traces use exists to
 * keep sixty updates a second off the render path, and there is nothing here to keep off it. A
 * chart of thirty points that repaints twice a minute is exactly what React is cheap for.
 */
export function LapTrend({ driverKey }: LapTrendProps) {
  const laps = useLapSummaries(driverKey);

  const chart = useMemo(() => {
    const timed = laps.filter(
      (lap) => lap.lapTimeMs !== null && lap.lapTimeMs !== undefined,
    ) as (LapSummary & { lapTimeMs: number })[];

    if (timed.length === 0) {
      return null;
    }

    const times = timed.map((lap) => lap.lapTimeMs);
    const fastest = Math.min(...times);
    const slowest = Math.max(...times);
    // A flat stint would divide by zero and put every point at the same undefined height. One
    // millisecond of span keeps the arithmetic honest and draws the flat line it actually is.
    const span = Math.max(1, slowest - fastest);

    const plotWidth = VIEW_WIDTH - PADDING.left - PADDING.right;
    const plotHeight = VIEW_HEIGHT - PADDING.top - PADDING.bottom;

    // Faster laps sit higher, which is the way every timing screen already reads.
    const toY = (ms: number) => PADDING.top + ((ms - fastest) / span) * plotHeight;
    const toX = (index: number) =>
      PADDING.left +
      (timed.length === 1 ? plotWidth / 2 : (index / (timed.length - 1)) * plotWidth);

    const points: Point[] = timed.map((lap, index) => ({
      lapNumber: lap.lapNumber,
      ms: lap.lapTimeMs,
      invalid: lap.valid === false,
      x: toX(index),
      y: toY(lap.lapTimeMs),
    }));

    // The average walks only the laps that count, but is drawn at the x of the lap it belongs to —
    // so a deleted lap leaves the mean line unmoved rather than absent.
    const counting: number[] = [];
    const meanPath: string[] = [];
    const upper: string[] = [];
    const lower: string[] = [];

    timed.forEach((lap, index) => {
      if (!counts(lap)) {
        return;
      }

      counting.push(lap.lapTimeMs);
      const mean = rollingAverage(counting, counting.length - 1);
      const from = Math.max(0, counting.length - ROLLING_WINDOW);
      const window = counting.slice(from);
      const variance =
        window.reduce((total, ms) => total + (ms - mean) ** 2, 0) / Math.max(1, window.length);
      const deviation = Math.sqrt(variance);

      const x = toX(index);
      meanPath.push(`${meanPath.length === 0 ? 'M' : 'L'}${x} ${toY(mean)}`);
      upper.push(`${upper.length === 0 ? 'M' : 'L'}${x} ${toY(mean - deviation)}`);
      lower.unshift(`L${x} ${toY(mean + deviation)}`);
    });

    return {
      points,
      fastest,
      slowest,
      meanPath: meanPath.join(' '),
      // Closed by running the upper edge forwards and the lower edge back, so the band is one shape
      // rather than two lines the eye has to pair up.
      bandPath: upper.length === 0 ? '' : `${upper.join(' ')} ${lower.join(' ')} Z`,
    };
  }, [laps]);

  if (chart === null) {
    return <p className="wall__unavailable">No completed laps yet.</p>;
  }

  return (
    <div className="lap-trend">
      <svg
        className="lap-trend__plot"
        viewBox={`0 0 ${VIEW_WIDTH} ${VIEW_HEIGHT}`}
        preserveAspectRatio="none"
        role="img"
        aria-label={`Lap times from ${formatLapTime(chart.fastest)} to ${formatLapTime(chart.slowest)}`}
      >
        {chart.bandPath !== '' && (
          <path className="lap-trend__band" d={chart.bandPath} fill={TRACE_COLOURS.steering} />
        )}

        <path
          className="lap-trend__mean"
          d={chart.meanPath}
          fill="none"
          stroke={TRACE_COLOURS.steering}
        />

        {chart.points.map((point) => (
          <circle
            key={point.lapNumber}
            cx={point.x}
            cy={point.y}
            r={2.5}
            // An invalid lap is hollow rather than merely a different colour, for the reason the
            // struck-through lap time in the tower is struck through: a state this consequential
            // should not be carried by hue alone.
            className={point.invalid ? 'lap-trend__lap lap-trend__lap--invalid' : 'lap-trend__lap'}
          >
            <title>
              Lap {point.lapNumber} · {formatLapTime(point.ms)}
              {point.invalid ? ' · invalid' : ''}
            </title>
          </circle>
        ))}
      </svg>

      <div className="lap-trend__scale">
        <span>{formatLapTime(chart.fastest)}</span>
        <span className="lap-trend__scale-label">
          {ROLLING_WINDOW}-lap average, ±1σ · {chart.points.length} laps
        </span>
        <span>{formatLapTime(chart.slowest)}</span>
      </div>
    </div>
  );
}
