import { useMemo } from 'react';
import { LiveChart } from '../focus/LiveChart';
import { TRACE_COLOURS } from '../focus/traceColours';
import type { StoredSample } from '../../shared/history/contracts';
import { StaticChartSource } from '../../shared/history/staticSource';

interface StoredLapChartProps {
  samples: readonly StoredSample[];
}

/**
 * Throttle, brake and speed across one stored lap.
 *
 * **The point of this component is how little of it there is.** It is the first chart in the app
 * drawn from something other than the live socket, and it reuses `LiveChart` unchanged: the same
 * uPlot lifecycle, the same paint loop, the same colours. What made that possible is that
 * `LiveChart` asks its sources for a structural interface rather than for a `TraceBuffer` — see
 * `shared/history/staticSource.ts`, which is the whole of the new machinery.
 *
 * Throttle and brake share a 0–1 scale so their crossover — the overlap where a driver is on both
 * pedals — is visible as a shape rather than as two numbers. Speed gets its own scale because it
 * shares no units with either, and lines that do not share an axis can only be told apart by
 * colour.
 */
export function StoredLapChart({ samples }: StoredLapChartProps) {
  // Built once per lap rather than per render: a lap is tens of thousands of samples, and the spec
  // is read when the chart is built.
  const sources = useMemo(
    () =>
      StaticChartSource.fromSamples(samples, {
        throttle: (s) => s.throttle,
        brake: (s) => s.brake,
        speed: (s) => s.speed,
      }),
    [samples],
  );

  return (
    <LiveChart
      // A floor well above the 96px default, because this chart is the page rather than one tile on
      // a wall: a lap of telemetry drawn in a strip cannot be read. The real height still comes from
      // the container.
      minHeight={280}
      // No `store` and no `driverKey`: these numbers were fixed before the page loaded, so there is
      // no later moment at which they become different numbers and the chart is built exactly once.
      spec={{
        // The x range is the lap's own length, not a rolling window. A stored lap is not a ring —
        // it has a first sample and a last one, and showing all of it is the entire difference
        // between this and a live trace.
        capacity: samples.length,
        scales: {
          // Pinned rather than auto-ranged: a pedal trace read against an axis that rescales itself
          // to whatever this lap happened to reach cannot be compared with the lap beside it.
          pedals: { range: [0, 1] },
          speed: { auto: true },
        },
        axis: { scale: 'pedals' },
        series: [
          {
            id: 'throttle',
            label: 'Throttle',
            stroke: TRACE_COLOURS.throttle,
            scale: 'pedals',
            buffer: () => sources.throttle,
          },
          {
            id: 'brake',
            label: 'Brake',
            stroke: TRACE_COLOURS.brake,
            scale: 'pedals',
            buffer: () => sources.brake,
          },
          {
            id: 'speed',
            label: 'Speed',
            stroke: TRACE_COLOURS.speed,
            scale: 'speed',
            buffer: () => sources.speed,
          },
        ],
      }}
    />
  );
}
