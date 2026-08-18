import { useEffect, useMemo, useRef } from 'react';
import { formatNumber, formatPercent, NOT_REPORTED } from '../../shared/format/format';
import type { RaceRoomExtras } from '../../shared/live/contracts';
import { WHEELS } from '../../shared/live/contracts';
import { TraceBuffer, TYRE_TRACE_CAPACITY, type WheelTraces } from '../../shared/live/store';
import { useExtras } from '../../shared/live/useLive';
import { LiveChart, type LiveChartSpec } from '../../features/focus/LiveChart';
import { WHEEL_COLOURS } from '../../features/focus/traceColours';
import type { SimPanelProps } from '../registry';

type BrakeChannel = (extras: RaceRoomExtras) => number[] | undefined;

function parseExtras(extrasJson: string | null): RaceRoomExtras | null {
  if (extrasJson === null) return null;

  try {
    return JSON.parse(extrasJson) as RaceRoomExtras;
  } catch {
    return null;
  }
}

function newWheelTraces(): WheelTraces {
  return [
    new TraceBuffer(TYRE_TRACE_CAPACITY),
    new TraceBuffer(TYRE_TRACE_CAPACITY),
    new TraceBuffer(TYRE_TRACE_CAPACITY),
    new TraceBuffer(TYRE_TRACE_CAPACITY),
  ];
}

function BrakeTrace({
  store,
  driverKey,
  read,
  unit,
  format,
  range,
}: SimPanelProps & {
  read: BrakeChannel;
  unit: string;
  format: (value: number | null | undefined) => string;
  range?: readonly [number, number];
}) {
  const frame = useExtras(driverKey);

  /*
   * Held here rather than in the store, which is the wrong place for them: every other channel on
   * screen is a ring the store owns, and these are the one exception only because the extras
   * document is still parsed per panel. Both halves of that move together in #57 — when the store
   * pushes extras channels into its own rings, this ref and the effect below go, and the spec's
   * buffers resolve out of the store like every other chart's.
   *
   * Rings rather than component state for the usual reason: a stint's worth of samples re-rendering
   * a panel once a second is a cost with nothing to show for it.
   */
  const tracesRef = useRef<WheelTraces>(newWheelTraces());
  const lastSampleRef = useRef<string | null>(null);

  const values = useMemo(() => {
    const extras = parseExtras(frame?.extras ?? null);
    return extras === null ? undefined : read(extras);
  }, [frame, read]);

  useEffect(() => {
    if (frame === null || frame.capturedAtUtc === lastSampleRef.current) return;
    lastSampleRef.current = frame.capturedAtUtc;

    for (let wheel = 0; wheel < 4; wheel++) {
      const value = values?.[wheel];
      // NaN for anything the simulator did not report, including its -1 sentinel, so the chart
      // draws a hole rather than a reading. A brake at -1 °C is not a cold brake.
      tracesRef.current[wheel]!.push(
        value === undefined || !Number.isFinite(value) || value < 0 ? Number.NaN : value,
      );
    }
  }, [frame, values]);

  const spec: LiveChartSpec = {
    capacity: TYRE_TRACE_CAPACITY,
    scales: { y: range === undefined ? {} : { range: [...range] } },
    series: WHEELS.map((wheel, index) => ({
      label: wheel,
      stroke: WHEEL_COLOURS[index]!,
      buffer: () => tracesRef.current[index]!,
    })),
  };

  return (
    <div className="wheel-chart">
      <LiveChart
        store={store}
        driverKey={driverKey}
        spec={spec}
        height={112}
        className="wheel-chart__plot"
      />

      <div className="wheel-chart__values">
        {WHEELS.map((wheel, index) => {
          const value = values?.[index];
          const shown =
            value === undefined || !Number.isFinite(value) || value < 0
              ? NOT_REPORTED
              : format(value);
          return (
            <div key={wheel} className="wheel-chart__value">
              <span className="wheel-chart__key" style={{ background: WHEEL_COLOURS[index]! }} />
              <span className="wheel-chart__wheel">{wheel}</span>
              <span className="wheel-chart__number">{shown}</span>
            </div>
          );
        })}
        <span className="wheel-chart__unit">{unit}</span>
      </div>
    </div>
  );
}

const readTemperature = (extras: RaceRoomExtras) => extras.brakeTemperatureCelsius;
const readWear = (extras: RaceRoomExtras) => extras.brakeWear;
const formatTemperature = (value: number | null | undefined) => formatNumber(value, 0);
const WEAR_RANGE = [0, 1] as const;

export function BrakeTemperaturePanel(props: SimPanelProps) {
  return <BrakeTrace {...props} read={readTemperature} unit="°C" format={formatTemperature} />;
}

export function BrakeWearPanel(props: SimPanelProps) {
  return (
    <BrakeTrace {...props} read={readWear} unit="worn" format={formatPercent} range={WEAR_RANGE} />
  );
}
