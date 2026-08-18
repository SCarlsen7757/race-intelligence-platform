import { describe, expect, it, vi } from 'vitest';
import type { FocusFrameMessage, LapHistoryMessage, TowerSnapshotMessage } from './contracts';
import { LiveStore, TraceBuffer, TYRE_SAMPLE_INTERVAL_MS, TYRE_TRACE_CAPACITY } from './store';

/**
 * A store already following the drivers a test is about to push frames for.
 *
 * The store refuses frames for anyone the connection has not subscribed to — that is what keeps a
 * dropped driver's rings from being resurrected by a frame still in flight — so every test that
 * feeds it has to say who is being followed first.
 */
function following(driverKeys: string[], now?: () => number): LiveStore {
  const store = new LiveStore(now);
  store.setFollowedDrivers(driverKeys);

  return store;
}

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

  /**
   * The mechanism a paint loop actually leans on: `length` plateaus once the ring is full, so a
   * loop keyed on it would never notice a stint's five-thousandth sample arriving. `version` keeps
   * counting regardless, which is what lets a guard tell "new sample" from "same sample again".
   */
  it('keeps counting past capacity in version, unlike length which plateaus', () => {
    const buffer = new TraceBuffer(3);
    expect(buffer.version).toBe(0);

    for (const value of [1, 2, 3, 4, 5]) {
      buffer.push(value);
    }

    expect(buffer.length).toBe(3);
    expect(buffer.version).toBe(5);
  });

  it('resets version to zero on clear, along with length', () => {
    const buffer = new TraceBuffer(3);
    buffer.push(1);
    buffer.push(2);

    buffer.clear();

    expect(buffer.version).toBe(0);
  });
});

describe('LiveStore', () => {
  /**
   * The rule the whole two-rate design rests on. A subscriber firing at 60 Hz means a React render
   * per frame, which is exactly what the focus stream is kept out of React state to avoid.
   */
  it('does not notify subscribers for focus frames', () => {
    const store = following(['id:2']);
    const listener = vi.fn();
    store.subscribe(listener);

    store.apply(focusFrame());
    store.apply(focusFrame({ lapNumber: 2 }));

    expect(listener).not.toHaveBeenCalled();
    expect(store.frameFor('id:2')?.lapNumber).toBe(2);
  });

  /** Even a second driver joining the comparison mid-stream must not reach React from here. */
  it('does not notify subscribers when a second driver starts streaming', () => {
    const store = following(['id:2', 'id:9']);
    store.apply({
      type: 'extrasFrame',
      roomId: 'room',
      driverKey: 'id:2',
      capturedAtUtc: '2026-08-16T12:00:00Z',
      extras: '{}',
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
    const store = following(['id:2']);

    store.apply(focusFrame({ throttle: 0.5 }));
    store.apply(focusFrame({ throttle: 0.75 }));

    expect([...store.tracesFor('id:2').throttle.toArray()]).toEqual([0.5, 0.75]);
  });

  /**
   * A missing pedal reading is not a lifted pedal. NaN leaves a gap in the trace, which is the
   * honest rendering; zero would draw a confident line saying the driver came off the throttle.
   */
  it('plots an unreported pedal as a gap rather than as zero', () => {
    const store = following(['id:2']);

    store.apply(focusFrame({ throttle: null }));

    expect(Number.isNaN(store.tracesFor('id:2').throttle.toArray()[0]!)).toBe(true);
  });

  /** Clutch arrives only from a collector new enough to send it, and absent is not released. */
  it('plots an absent clutch as a gap rather than as zero', () => {
    const store = following(['id:2']);
    const frame = focusFrame();
    delete frame.clutch;

    store.apply(frame);

    expect(Number.isNaN(store.tracesFor('id:2').clutch.toArray()[0]!)).toBe(true);
  });

  /** Two cars compared side by side are two sets of rings, and they must never mix. */
  it('keeps a separate set of traces per followed driver', () => {
    const store = following(['id:2', 'id:9']);

    store.apply(focusFrame({ driverKey: 'id:2', throttle: 1 }));
    store.apply(focusFrame({ driverKey: 'id:9', throttle: 0.25 }));
    store.apply(focusFrame({ driverKey: 'id:2', throttle: 0.5 }));

    expect([...store.tracesFor('id:2').throttle.toArray()]).toEqual([1, 0.5]);
    expect([...store.tracesFor('id:9').throttle.toArray()]).toEqual([0.25]);
    expect(store.frameFor('id:2')?.throttle).toBe(0.5);
    expect(store.frameFor('id:9')?.throttle).toBe(0.25);
  });

  /**
   * A frame for a driver just dropped can still be in flight when the subscription changes.
   * Admitting it would leave a car on screen nobody asked for, with rings that never advance again.
   */
  it('refuses a frame for a driver nobody is following', () => {
    const store = following(['id:2']);

    store.apply(focusFrame({ driverKey: 'id:9', throttle: 1 }));

    expect(store.frameFor('id:9')).toBeNull();
    expect(store.tracesFor('id:9').throttle.length).toBe(0);
  });

  /** Dropping one half of a comparison must cost the other half nothing. */
  it('drops only the driver that went away', () => {
    const store = following(['id:2', 'id:9']);
    store.apply(focusFrame({ driverKey: 'id:2', throttle: 1 }));
    store.apply(focusFrame({ driverKey: 'id:9', throttle: 0.25 }));

    store.setFollowedDrivers(['id:9']);

    expect(store.frameFor('id:2')).toBeNull();
    expect(store.frameFor('id:9')?.throttle).toBe(0.25);
    expect([...store.tracesFor('id:9').throttle.toArray()]).toEqual([0.25]);
  });

  it('resetFocus empties the traces and the latest frame', () => {
    const store = following(['id:2']);
    store.apply(focusFrame());

    store.resetFocus();

    expect(store.frameFor('id:2')).toBeNull();
    expect(store.tracesFor('id:2').throttle.length).toBe(0);
  });

  /**
   * The bargain the hub's thirty-second room expiry exists to keep. A socket that drops and returns
   * inside that window rejoins the same room, so the minute of trace that preceded the drop is
   * still about the same car and must survive.
   */
  it('interruptFocus keeps the traces across a reconnect into the same room', () => {
    const store = following(['id:2']);
    store.apply(focusFrame({ throttle: 1 }));
    store.apply(focusFrame({ throttle: 0.5 }));

    store.interruptFocus();

    const throttle = [...store.tracesFor('id:2').throttle.toArray()];
    expect(throttle.slice(0, 2)).toEqual([1, 0.5]);
    expect(throttle).toHaveLength(3);
  });

  /** Absent is not zero, and a resumed stream must not be bridged across the hole it left. */
  it('interruptFocus writes the outage into every channel as a gap', () => {
    const store = following(['id:2']);
    store.apply(focusFrame());

    store.interruptFocus();

    const { throttle, brake, clutch, steering, speed, tyres } = store.tracesFor('id:2');
    for (const trace of [throttle, brake, clutch, steering, speed]) {
      expect(trace.last()).toBeNaN();
    }

    for (let wheel = 0; wheel < 4; wheel++) {
      expect(tyres.pressureKpa[wheel]!.last()).toBeNaN();
      expect(tyres.wear[wheel]!.last()).toBeNaN();
      expect(tyres.temperatureCelsius[wheel]!.last()).toBeNaN();
    }
  });

  /** `LiveReadout` paints the last frame forever, so a held one would sit there claiming to be now. */
  it('interruptFocus drops the held frame without dropping the history behind it', () => {
    const store = following(['id:2']);
    store.apply(focusFrame({ speedMetersPerSecond: 60 }));

    store.interruptFocus();

    expect(store.frameFor('id:2')).toBeNull();
    expect(store.tracesFor('id:2').speed.length).toBe(2);
  });

  /**
   * The followed set has to survive too. Clearing it was what made the old reset take the rings
   * with it — and a frame arriving before the replayed subscription is acknowledged would then be
   * refused as belonging to a driver nobody is following.
   */
  it('interruptFocus leaves the followed set intact, so frames resume without re-subscribing', () => {
    const store = following(['id:2']);
    store.apply(focusFrame({ throttle: 1 }));

    store.interruptFocus();
    store.apply(focusFrame({ throttle: 0.25 }));

    expect(store.frameFor('id:2')?.throttle).toBe(0.25);
    expect([...store.tracesFor('id:2').throttle.toArray()]).toEqual([1, Number.NaN, 0.25]);
  });

  /** Otherwise an interval that elapsed during the outage swallows the first frame back. */
  it('samples the first tyre reading after an interruption rather than waiting out the interval', () => {
    let now = 0;
    const store = following(['id:2'], () => now);
    store.apply(focusFrame({ tyreWear: [0.25, 0.25, 0.25, 0.25] }));

    now += TYRE_SAMPLE_INTERVAL_MS * 4;
    store.interruptFocus();

    now += 1;
    store.apply(focusFrame({ tyreWear: [0.5, 0.5, 0.5, 0.5] }));

    // The reading, the gap the outage left, and the first reading back.
    expect([...store.tracesFor('id:2').tyres.wear[0].toArray()]).toEqual([0.25, Number.NaN, 0.5]);
  });

  /**
   * The other half of an interruption: a socket down longer than the room expiry comes back to a
   * room the hub has forgotten, and says so with an error rather than with a room switch. The
   * driver keys stop meaning anything either way.
   */
  it.each(['unknownRoom', 'roomClosed'] as const)(
    'clears the traces and lap histories when the hub answers %s',
    (code) => {
      const store = following(['id:2']);
      store.apply(focusFrame());
      store.apply(lapHistory('id:2', [1, 2]));

      store.apply({ type: 'error', code, message: 'gone' });

      expect(store.frameFor('id:2')).toBeNull();
      expect(store.tracesFor('id:2').throttle.length).toBe(0);
      expect(store.getLapHistories()).toEqual({});
    },
  );

  /** An error about one driver says nothing about the room, and must not cost the other one its rings. */
  it('keeps the traces for an error that is not about the room', () => {
    const store = following(['id:2']);
    store.apply(focusFrame());

    store.apply({ type: 'error', code: 'noTelemetryForDriver', message: 'nothing to send' });

    expect(store.tracesFor('id:2').throttle.length).toBe(1);
  });

  /**
   * A tyre moves over a stint, not over a corner. Sampling the tyre rings at focus rate would fill
   * the whole window with sixty seconds of a flat line and call it information.
   */
  it('decimates the tyre traces to one sample per interval', () => {
    let now = 0;
    const store = following(['id:2'], () => now);

    // Ten frames inside one interval, then one after it.
    for (let i = 0; i < 10; i++) {
      now += TYRE_SAMPLE_INTERVAL_MS / 20;
      store.apply(focusFrame({ tyreWear: [0.25, 0.25, 0.25, 0.25] }));
    }

    now += TYRE_SAMPLE_INTERVAL_MS;
    store.apply(focusFrame({ tyreWear: [0.5, 0.5, 0.5, 0.5] }));

    // Two: the first frame of the stint, and the first one past the interval.
    expect([...store.tracesFor('id:2').tyres.wear[0].toArray()]).toEqual([0.25, 0.5]);
    expect(store.tracesFor('id:2').throttle.length).toBe(11);
  });

  it('keeps every tyre channel per wheel, in wire order', () => {
    const now = 0;
    const store = following(['id:2'], () => now);

    store.apply(
      focusFrame({
        tyrePressureKpa: [180, 181, 175, 176],
        tyreTemperatureCelsius: [85, 86, 82, 83],
      }),
    );

    const { pressureKpa, temperatureCelsius } = store.tracesFor('id:2').tyres;

    expect(pressureKpa.map((buffer) => buffer.last())).toEqual([180, 181, 175, 176]);
    expect(temperatureCelsius.map((buffer) => buffer.last())).toEqual([85, 86, 82, 83]);
  });

  /**
   * The same discipline the pedals follow. Tyre arrays are nullable on the wire, and a wheel the
   * simulator did not report must leave a hole — drawn at zero it would read as a flat tyre.
   */
  it('plots an unreported wheel as a gap rather than as zero', () => {
    const now = 0;
    const store = following(['id:2'], () => now);

    store.apply(focusFrame({ tyrePressureKpa: [180, null, 175, 176] }));

    expect(Number.isNaN(store.tracesFor('id:2').tyres.pressureKpa[1].last())).toBe(true);
    expect(store.tracesFor('id:2').tyres.pressureKpa[0].last()).toBe(180);
  });

  /**
   * Fixed-size rings, so an hour of stint costs the same memory as a minute of it. A growing array
   * here would be a slow leak over a two-hour race.
   */
  it('keeps the tyre rings flat over a long stint', () => {
    let now = 0;
    const store = following(['id:2'], () => now);

    for (let i = 0; i < TYRE_TRACE_CAPACITY + 500; i++) {
      now += TYRE_SAMPLE_INTERVAL_MS;
      store.apply(focusFrame({ tyreWear: [i / 10_000, 0, 0, 0] }));
    }

    expect(store.tracesFor('id:2').tyres.wear[0].length).toBe(TYRE_TRACE_CAPACITY);
  });

  /**
   * Two damage panels can be on screen at once, so extras are keyed like lap history rather than
   * held in a single slot both would read.
   */
  it('keeps a separate extras document per driver', () => {
    const store = following(['id:2', 'id:9']);

    store.apply({
      type: 'extrasFrame',
      roomId: 'room',
      driverKey: 'id:2',
      capturedAtUtc: '2026-08-16T12:00:00Z',
      extras: '{"damage":{"engine":0.5}}',
    });

    expect(store.getExtras()['id:2']?.extras).toContain('0.5');
    expect(store.getExtras()['id:9']).toBeUndefined();
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
