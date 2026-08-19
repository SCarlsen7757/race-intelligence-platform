import { LAP_BINS, type LapTrace, type LiveStore } from '../../shared/live/store';
import { useLapSummaries } from '../../shared/live/useLive';
import { LiveChart, type LiveChartSource, type LiveChartSpec } from './LiveChart';
import { TRACE_COLOURS } from './traceColours';

/**
 * The delta between the lap being driven and the reference, as a chart source.
 *
 * Not a ring, which is the whole reason {@link LiveChartSource} is structural. A ring is pushed
 * sample by sample and read back in order; this is a thousand bins of *lap progress* that get
 * rewritten as the car comes round, and the value at each is a subtraction between two laps rather
 * than anything anybody recorded.
 *
 * Both laps are resolved per read rather than captured once. The current lap is replaced every time
 * the car crosses the line, and the reference is replaced the moment a quicker clean lap lands —
 * holding either would leave the chart quietly measuring against a lap that is no longer the best
 * one, which is the kind of wrong that looks right.
 */
class LapDeltaSource implements LiveChartSource {
  private scratch: Float64Array<ArrayBuffer> | undefined;

  constructor(
    private readonly current: () => LapTrace | null,
    private readonly reference: () => LapTrace | null,
  ) {}

  readonly length = LAP_BINS;

  /**
   * Changes whenever the picture would.
   *
   * Two things move it: the lap filling in as the car goes round, and the pair of laps being
   * compared changing underneath. Counting only the first would freeze the chart at the moment a
   * new reference landed — the numbers would all be different and nothing would repaint.
   */
  get version(): number {
    const current = this.current();
    const reference = this.reference();

    if (current === null || reference === null) {
      return -1;
    }

    return (current.lapNumber * LAP_BINS + current.sampleCount) * LAP_BINS + reference.lapNumber;
  }

  toNullableArray(into?: (number | null)[]): (number | null)[] {
    const out = into !== undefined && into.length === LAP_BINS ? into : new Array<null>(LAP_BINS);
    const current = this.current();
    const reference = this.reference();

    if (current === null || reference === null) {
      out.fill(null);
      return out;
    }

    // Reused across reads for the usual reason: this is called from a paint loop, and a fresh
    // thousand-element array sixty times a second is garbage the loop exists to avoid.
    this.scratch = current.deltaTo(reference, this.scratch);

    for (let bin = 0; bin < LAP_BINS; bin++) {
      const value = this.scratch[bin]!;
      // NaN wherever either lap has no reading there. Null is what draws a hole, and a hole is the
      // honest rendering of a stretch one of the two laps was never observed over — a zero would
      // claim the two laps were level.
      out[bin] = Number.isNaN(value) ? null : value;
    }

    return out;
  }
}

interface LapDeltaProps {
  store: LiveStore;
  driverKey: string;
  height?: number;
}

/**
 * Where this lap is gaining or losing against the driver's best clean one.
 *
 * The chart a driver actually asks for, and the one this milestone's whole indexing scheme exists to
 * make possible. **There is no track map behind it, no corner list, and nothing to maintain per
 * circuit** — the wire carries normalised lap progress, so two laps line up by where the car is, and
 * the delta is a subtraction. A section losing four tenths is a hump you can see the size of rather
 * than a number to hold in your head.
 *
 * Below the zero line is time gained, above it is time lost, which is the sign convention every
 * timing screen already uses: a positive delta is what a driver sees when they are behind.
 *
 * **A missing reference is explained rather than drawn as an empty plot.** For the first laps of a
 * session there is genuinely nothing to compare against, and a blank chart in a grid of working ones
 * reads as broken — the one impression it must not give while it is, in fact, working exactly as
 * intended.
 */
export function LapDelta({ store, driverKey, height = 140 }: LapDeltaProps) {
  // Subscribed for the re-render, not for the value. Lap summaries change once a lap, which is
  // precisely when a reference lap can appear or be beaten, so this is the cheapest correct signal
  // for "look again at whether there is something to compare against".
  useLapSummaries(driverKey);

  const reference = store.referenceLapFor(driverKey);

  if (reference === null) {
    return (
      // The same voice a tile uses when this session cannot feed it: the widget is placed and
      // working, and is saying what it is waiting for.
      <p className="wall__unavailable">
        No clean lap to compare against yet. The delta appears once a full lap has been completed.
      </p>
    );
  }

  const spec: LiveChartSpec = {
    capacity: LAP_BINS,
    // Left to fit what arrived. A delta has no natural bounds — a driver two seconds off their best
    // through a corner is as real as one a tenth off — and a pinned range would either clip the
    // interesting case or flatten the ordinary one.
    scales: { delta: {} },
    axis: { scale: 'delta' },
    series: [
      {
        id: 'delta',
        label: 'Delta',
        scale: 'delta',
        stroke: TRACE_COLOURS.steering,
        width: 2,
        buffer: () =>
          new LapDeltaSource(
            () => store.currentLapFor(driverKey),
            () => store.referenceLapFor(driverKey),
          ),
      },
    ],
  };

  return (
    <div className="wheel-chart">
      <LiveChart
        store={store}
        driverKey={driverKey}
        spec={spec}
        height={height}
        className="trace"
      />
      <p className="chart-caption">
        Against lap {reference.lapNumber} · above the line is time lost
      </p>
    </div>
  );
}
