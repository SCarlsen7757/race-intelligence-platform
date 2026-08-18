import { useEffect, useMemo, useRef } from 'react';
import uPlot from 'uplot';
import 'uplot/dist/uPlot.min.css';
import { formatNumber, formatPercent, NOT_REPORTED } from '../../shared/format/format';
import type { RaceRoomExtras } from '../../shared/live/contracts';
import { WHEELS } from '../../shared/live/contracts';
import { TraceBuffer, TYRE_TRACE_CAPACITY, type WheelTraces } from '../../shared/live/store';
import { useExtras } from '../../shared/live/useLive';
import { TRACE_COLOURS, WHEEL_COLOURS } from '../../features/focus/traceColours';
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
  const containerRef = useRef<HTMLDivElement>(null);
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
      tracesRef.current[wheel]!.push(
        value === undefined || !Number.isFinite(value) || value < 0 ? Number.NaN : value,
      );
    }
  }, [frame, values]);

  useEffect(() => {
    const container = containerRef.current;
    if (container === null) return;

    const chart = new uPlot(
      {
        width: container.clientWidth,
        height: 112,
        scales: {
          x: { time: false, range: [0, TYRE_TRACE_CAPACITY - 1] },
          y: range === undefined ? {} : { range: [...range] },
        },
        axes: [
          { show: false },
          { stroke: TRACE_COLOURS.axis, grid: { stroke: TRACE_COLOURS.grid } },
        ],
        legend: { show: false },
        cursor: { show: false },
        series: [
          {},
          ...WHEELS.map((wheel, index) => ({
            label: wheel,
            stroke: WHEEL_COLOURS[index]!,
            width: 1.5,
            spanGaps: false,
          })),
        ],
      },
      [
        new Float64Array(0),
        new Float64Array(0),
        new Float64Array(0),
        new Float64Array(0),
        new Float64Array(0),
      ],
      container,
    );

    let xs = new Float64Array(0);
    const series = WHEELS.map(() => new Float64Array(0));
    let animationFrame = 0;

    const paint = () => {
      const count = tracesRef.current[0].length;
      if (count !== xs.length) {
        xs = new Float64Array(count);
        for (let index = 0; index < count; index++) {
          xs[index] = TYRE_TRACE_CAPACITY - count + index;
        }
      }
      for (let wheel = 0; wheel < 4; wheel++) {
        series[wheel] = tracesRef.current[wheel]!.toArray(series[wheel]);
      }
      chart.setData([xs, series[0]!, series[1]!, series[2]!, series[3]!], true);
      animationFrame = requestAnimationFrame(paint);
    };

    animationFrame = requestAnimationFrame(paint);
    const resize = () => chart.setSize({ width: container.clientWidth, height: 112 });
    window.addEventListener('resize', resize);

    return () => {
      cancelAnimationFrame(animationFrame);
      window.removeEventListener('resize', resize);
      chart.destroy();
    };
  }, [range]);

  return (
    <div className="wheel-chart">
      <div ref={containerRef} className="wheel-chart__plot" />
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
