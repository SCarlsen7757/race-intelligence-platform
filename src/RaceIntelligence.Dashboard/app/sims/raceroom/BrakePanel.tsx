import { useEffect, useMemo, useRef } from 'react';
import { formatNumber, formatPercent, NOT_REPORTED } from '../../shared/format/format';
import type { RaceRoomExtras } from '../../shared/live/contracts';
import { TraceBuffer, TYRE_TRACE_CAPACITY, type WheelTraces } from '../../shared/live/store';
import { useExtras } from '../../shared/live/useLive';
import { ChannelLegend } from '../../features/focus/ChannelLegend';
import { LiveChart, type LiveChartSpec } from '../../features/focus/LiveChart';
import { WHEEL_CHANNELS } from '../../features/focus/WheelTrace';
import type { ChannelPanelProps } from '../registry';

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
  hiddenChannels,
  onToggleChannel,
  read,
  unit,
  format,
  range,
}: ChannelPanelProps & {
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
    series: WHEEL_CHANNELS.map((wheel, index) => ({
      id: wheel.id,
      label: wheel.label,
      stroke: wheel.stroke,
      buffer: () => tracesRef.current[index]!,
    })),
  };

  return (
    <div className="wheel-chart">
      <LiveChart
        store={store}
        driverKey={driverKey}
        spec={spec}
        hidden={hiddenChannels}
        height={112}
        className="wheel-chart__plot"
      />

      <ChannelLegend
        channels={WHEEL_CHANNELS}
        hidden={hiddenChannels}
        onToggle={onToggleChannel}
        unit={unit}
        renderValue={(_, index) => {
          const value = values?.[index];
          const shown =
            value === undefined || !Number.isFinite(value) || value < 0
              ? NOT_REPORTED
              : format(value);

          return <span className="wheel-chart__number">{shown}</span>;
        }}
      />
    </div>
  );
}

const readTemperature = (extras: RaceRoomExtras) => extras.brakeTemperatureCelsius;
const readWear = (extras: RaceRoomExtras) => extras.brakeWear;
const formatTemperature = (value: number | null | undefined) => formatNumber(value, 0);
const WEAR_RANGE = [0, 1] as const;

export function BrakeTemperaturePanel(props: ChannelPanelProps) {
  return <BrakeTrace {...props} read={readTemperature} unit="°C" format={formatTemperature} />;
}

export function BrakeWearPanel(props: ChannelPanelProps) {
  return (
    <BrakeTrace {...props} read={readWear} unit="worn" format={formatPercent} range={WEAR_RANGE} />
  );
}
