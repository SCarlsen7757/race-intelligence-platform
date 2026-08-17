import { describe, expect, it, vi } from 'vitest';
import type { FocusFrameMessage, LapHistoryMessage, TowerSnapshotMessage } from './contracts';
import { LiveStore, TraceBuffer } from './store';

function focusFrame(overrides: Partial<FocusFrameMessage> = {}): FocusFrameMessage {
  return {
    type: 'focusFrame',
    roomId: 'room',
    driverKey: 'id:2',
    capturedAtUtc: '2026-08-16T12:00:00Z',
    simulationTime: 0,
    lapNumber: 1,
    sector: 1,
    speedMetersPerSecond: 50,
    throttle: 1,
    brake: 0,
    clutch: 0,
    steering: 0,
    gear: 4,
    engineRpm: 7000,
    fuelLeftLiters: 40,
    tyrePressureKpa: [180, 180, 175, 175],
    tyreWear: [0.1, 0.1, 0.1, 0.1],
    tyreTemperatureCelsius: [85, 85, 82, 82],
    ...overrides,
  };
}

const tower: TowerSnapshotMessage = {
  type: 'towerSnapshot',
  roomId: 'room',
  capturedAtUtc: '2026-08-16T12:00:00Z',
  drivers: [],
};

function lapHistory(driverKey: string, laps: number[]): LapHistoryMessage {
  return {
    type: 'lapHistory',
    roomId: 'room',
    driverKey,
    truncated: false,
    laps: laps.map((lapNumber) => ({
      lapNumber,
      lapTimeMs: 90_000,
      sectorMs: [],
      valid: true,
    })),
  };
}

describe('TraceBuffer', () => {
  it('returns values oldest first', () => {
    const buffer = new TraceBuffer(4);
    buffer.push(1);
    buffer.push(2);
    buffer.push(3);

    expect([...buffer.toArray()]).toEqual([1, 2, 3]);
  });

  it('overwrites the oldest value once full, keeping memory flat', () => {
    const buffer = new TraceBuffer(3);
    for (const value of [1, 2, 3, 4, 5]) {
      buffer.push(value);
    }

    expect(buffer.length).toBe(3);
    expect([...buffer.toArray()]).toEqual([3, 4, 5]);
  });

  it('reuses a caller-supplied array so the paint loop allocates nothing', () => {
    const buffer = new TraceBuffer(4);
    buffer.push(1);
    buffer.push(2);

    const first = buffer.toArray();
    const second = buffer.toArray(first);

    expect(second).toBe(first);
  });

  /** What the pedal bars read: the newest sample, without copying the ring out to get it. */
  it('reports the newest value, wrapping included', () => {
    const buffer = new TraceBuffer(3);
    expect(Number.isNaN(buffer.last())).toBe(true);

    for (const value of [1, 2, 3, 4]) {
      buffer.push(value);
    }

    expect(buffer.last()).toBe(4);
  });
});

describe('LiveStore', () => {
  /**
   * The rule the whole two-rate design rests on. A subscriber firing at 60 Hz means a React render
   * per frame, which is exactly what the focus stream is kept out of React state to avoid.
   */
  it('does not notify subscribers for focus frames', () => {
    const store = new LiveStore();
    const listener = vi.fn();
    store.subscribe(listener);

    store.apply(focusFrame());
    store.apply(focusFrame({ lapNumber: 2 }));

    expect(listener).not.toHaveBeenCalled();
    expect(store.focusFrame?.lapNumber).toBe(2);
  });

  /** Even the driver switch, which resets state mid-stream, must not reach React from here. */
  it('does not notify subscribers when a focus frame switches driver', () => {
    const store = new LiveStore();
    store.apply({
      type: 'extrasFrame',
      roomId: 'room',
      driverKey: 'id:2',
      capturedAtUtc: '2026-08-16T12:00:00Z',
      extrasJson: '{}',
    });

    const listener = vi.fn();
    store.subscribe(listener);

    store.apply(focusFrame({ driverKey: 'id:2' }));
    store.apply(focusFrame({ driverKey: 'id:9' }));

    expect(listener).not.toHaveBeenCalled();
  });

  it('notifies subscribers for the slow-changing streams', () => {
    const store = new LiveStore();
    const listener = vi.fn();
    store.subscribe(listener);

    store.apply(tower);
    store.apply({ type: 'roomList', rooms: [] });
    store.apply({ type: 'error', code: 'unknownRoom', message: 'gone' });

    expect(listener).toHaveBeenCalledTimes(3);
  });

  it('appends every focus frame to the traces', () => {
    const store = new LiveStore();

    store.apply(focusFrame({ throttle: 0.5 }));
    store.apply(focusFrame({ throttle: 0.75 }));

    expect([...store.traces.throttle.toArray()]).toEqual([0.5, 0.75]);
  });

  /**
   * A missing pedal reading is not a lifted pedal. NaN leaves a gap in the trace, which is the
   * honest rendering; zero would draw a confident line saying the driver came off the throttle.
   */
  it('plots an unreported pedal as a gap rather than as zero', () => {
    const store = new LiveStore();

    store.apply(focusFrame({ throttle: null }));

    expect(Number.isNaN(store.traces.throttle.toArray()[0]!)).toBe(true);
  });

  /** Clutch arrives only from a collector new enough to send it, and absent is not released. */
  it('plots an absent clutch as a gap rather than as zero', () => {
    const store = new LiveStore();
    const frame = focusFrame();
    delete frame.clutch;

    store.apply(frame);

    expect(Number.isNaN(store.traces.clutch.toArray()[0]!)).toBe(true);
  });

  /**
   * A frame for the previous driver can still be in flight when the subscription changes.
   * Mixing it into the new driver's traces would read as a glitch rather than as the switch it is.
   */
  it('clears the traces when the focused driver changes', () => {
    const store = new LiveStore();

    store.apply(focusFrame({ driverKey: 'id:2', throttle: 1 }));
    store.apply(focusFrame({ driverKey: 'id:9', throttle: 0.25 }));

    expect([...store.traces.throttle.toArray()]).toEqual([0.25]);
    expect(store.focusFrame?.driverKey).toBe('id:9');
  });

  it('resetFocus empties the traces and the latest frame', () => {
    const store = new LiveStore();
    store.apply(focusFrame());

    store.resetFocus();

    expect(store.focusFrame).toBeNull();
    expect(store.traces.throttle.length).toBe(0);
  });

  it('reports connection changes once per transition', () => {
    const store = new LiveStore();
    const listener = vi.fn();
    store.subscribe(listener);

    store.setConnected(true);
    store.setConnected(true);

    expect(listener).toHaveBeenCalledTimes(1);
    expect(store.isConnected()).toBe(true);
  });

  it('stops notifying after unsubscribe', () => {
    const store = new LiveStore();
    const listener = vi.fn();
    const unsubscribe = store.subscribe(listener);

    unsubscribe();
    store.apply(tower);

    expect(listener).not.toHaveBeenCalled();
  });

  /** Several rows can be expanded at once, so history is kept per driver rather than per view. */
  it('keeps a separate lap history for each driver', () => {
    const store = new LiveStore();

    store.apply(lapHistory('id:1', [1, 2]));
    store.apply(lapHistory('id:9', [1]));

    expect(store.getLapHistories()['id:1']?.laps).toHaveLength(2);
    expect(store.getLapHistories()['id:9']?.laps).toHaveLength(1);
  });

  /**
   * The map has to change identity when it changes content, or `useSyncExternalStore` will
   * compare the snapshot to itself and never re-render an expanded row.
   */
  it('replaces the history map rather than mutating it', () => {
    const store = new LiveStore();
    store.apply(lapHistory('id:1', [1]));
    const first = store.getLapHistories();

    store.apply(lapHistory('id:1', [1, 2]));

    expect(store.getLapHistories()).not.toBe(first);
    expect(first['id:1']?.laps).toHaveLength(1);
  });

  it('forgets one driver on collapse and all of them on leaving the room', () => {
    const store = new LiveStore();
    store.apply(lapHistory('id:1', [1]));
    store.apply(lapHistory('id:9', [1]));

    store.dropLapHistory('id:1');
    expect(Object.keys(store.getLapHistories())).toEqual(['id:9']);

    store.resetLapHistory();
    expect(store.getLapHistories()).toEqual({});
  });
});
