import type { FocusFrameMessage } from '../../shared/live/contracts';
import { WHEELS } from '../../shared/live/contracts';
import {
  TYRE_TRACE_CAPACITY,
  type LiveStore,
  type TyreTraces,
  type WheelTraces,
} from '../../shared/live/store';
import { LiveReadout } from '../../shared/ui/LiveReadout';
import { LiveChart, type LiveChartSpec } from './LiveChart';
import { WHEEL_COLOURS } from './traceColours';

interface WheelTraceProps {
  store: LiveStore;
  driverKey: string;
  /** Which of the tyre rings to plot. */
  channel: (tyres: TyreTraces) => WheelTraces;
  /** The same channel read off a frame, for the current-value labels. */
  read: (frame: FocusFrameMessage, wheel: number) => number | null | undefined;
  format: (value: number | null | undefined) => string;
  unit: string;
  /**
   * A fixed y range, where the channel has one.
   *
   * Wear is a fraction and belongs on 0..1, so a stint that has barely worn the tyres draws as the
   * flat line it is rather than being auto-scaled into a dramatic-looking slope. Pressure and
   * temperature have no natural bounds and are left to scale to what arrived.
   */
  range?: readonly [number, number];
  height?: number;
}

/**
 * One tyre channel over a stint, four wheels on one axis, with the current value beside each.
 *
 * **A number says where a tyre is; the line says where it is going**, and mid-stint the second is
 * the question actually being asked. Both are here rather than in two panels because they answer it
 * together — the label is the line's newest point, read out of the same store, so they cannot
 * disagree.
 *
 * Four wheels share one axis on purpose. Divergence is the signal for every one of these channels:
 * a left front climbing away from the right front is a car that is about to understeer, and that is
 * visible as a gap between two lines and invisible in four separate charts.
 *
 * The rings behind it are the slow ones — see `TYRE_SAMPLE_INTERVAL_MS`. Plotting tyres on the
 * pedals' thirty-second window would show a flat line and call it information.
 *
 * The painting is `LiveChart`'s and the labels are `LiveReadout`s, so no React render happens per
 * frame here either.
 */
export function WheelTrace({
  store,
  driverKey,
  channel,
  read,
  format,
  unit,
  range,
  height = 112,
}: WheelTraceProps) {
  const spec: LiveChartSpec = {
    capacity: TYRE_TRACE_CAPACITY,
    scales: { y: range === undefined ? {} : { range: [...range] } },
    // Resolved inside the closures rather than here, so the on-demand ring creation in `tracesFor`
    // stays out of a render pass. See the same note in `PedalTrace`.
    series: WHEELS.map((wheel, index) => ({
      label: wheel,
      stroke: WHEEL_COLOURS[index]!,
      buffer: () => channel(store.tracesFor(driverKey).tyres)[index]!,
    })),
  };

  return (
    <div className="wheel-chart">
      <LiveChart
        store={store}
        driverKey={driverKey}
        spec={spec}
        height={height}
        className="wheel-chart__plot"
      />

      <div className="wheel-chart__values">
        {WHEELS.map((wheel, index) => (
          <div key={wheel} className="wheel-chart__value">
            <span className="wheel-chart__key" style={{ background: WHEEL_COLOURS[index]! }} />
            <span className="wheel-chart__wheel">{wheel}</span>
            <LiveReadout
              store={store}
              driverKey={driverKey}
              className="wheel-chart__number"
              render={(liveFrame) => format(read(liveFrame, index))}
            />
          </div>
        ))}
        <span className="wheel-chart__unit">{unit}</span>
      </div>
    </div>
  );
}
