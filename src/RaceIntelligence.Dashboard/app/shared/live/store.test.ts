import { describe, expect, it, vi } from 'vitest';
import type {
  ExtrasFrameMessage,
  FocusFrameMessage,
  LapHistoryMessage,
  StintFrameMessage,
  TowerRow,
  TowerSnapshotMessage,
  TreadTemperatures,
} from './contracts';
import { LAP_BINS, LiveStore, TraceBuffer, TYRE_TRACE_CAPACITY } from './store';

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

/**
 * One tyre's temperatures, with a plausible window around the given middle-of-tread reading.
 *
 * Shoulders either side of the middle rather than three identical numbers, so a test that started
 * reading the wrong one would fail rather than pass by coincidence.
 */
function tread(middle: number): TreadTemperatures {
  return { inner: middle + 2, middle, outer: middle - 2, optimal: 90, cold: 70, hot: 110 };
}

/**
 * A stint frame — the tyre channels, on the roughly 1 Hz message they travel on.
 *
 * Separate from {@link focusFrame} because the wire separates them: tyres are read over a stint and
 * used to ride the 60 Hz frame, where fifty-nine of every sixty samples were sent and then dropped.
 */
function stintFrame(overrides: Partial<StintFrameMessage> = {}): StintFrameMessage {
  return {
    type: 'stintFrame',
    roomId: 'room',
    driverKey: 'id:2',
    capturedAtUtc: '2026-08-16T12:00:00Z',
    tyrePressureKpa: [180, 180, 175, 175],
    tyreWear: [0.1, 0.1, 0.1, 0.1],
    tyreTemperatureCelsius: [tread(85), tread(85), tread(82), tread(82)],
    ...overrides,
  };
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
    brakePressureKiloNewtons: [3.1, 3.2, 1.4, 1.5],
    ...overrides,
  };
}

function extrasFrame(overrides: Partial<ExtrasFrameMessage> = {}): ExtrasFrameMessage {
  return {
    type: 'extrasFrame',
    roomId: 'room',
    driverKey: 'id:2',
    capturedAtUtc: '2026-08-16T12:00:00Z',
    extras: '{}',
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

  /**
   * The boundary where "absent is not zero" was being lost.
   *
   * uPlot decides where a line breaks by testing against `null`, and a NaN passes that test as a
   * number — so every gap the store carefully recorded was drawn as a confident line straight
   * across it. The rings were always right; the shape they were copied out in was not.
   */
  it('copies a gap out as null, so a chart can draw it as a break', () => {
    const buffer = new TraceBuffer(4);
    buffer.push(1);
    buffer.push(Number.NaN);
    buffer.push(0.5);

    expect(buffer.toNullableArray()).toEqual([1, null, 0.5]);
  });

  it('reuses a caller-supplied array for the nullable copy too, so the paint loop still allocates nothing', () => {
    const buffer = new TraceBuffer(4);
    buffer.push(1);
    buffer.push(2);

    const first = buffer.toNullableArray();
    const second = buffer.toNullableArray(first);

    expect(second).toBe(first);
  });

  it('keeps the gap in the right place after the ring has wrapped', () => {
    const buffer = new TraceBuffer(3);
    for (const value of [1, 2, Number.NaN, 4, 5]) {
      buffer.push(value);
    }

    expect(buffer.toNullableArray()).toEqual([null, 4, 5]);
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
   *
   * Not zero notifications, but a fixed number: a driver announces itself once when it starts
   * streaming, so the panel can stop showing a click as unacknowledged. What must never happen is
   * a count that grows with the frames, which is what this asserts by sending two hundred of them.
   */
  it('notifies once when a driver starts streaming, and never again per frame', () => {
    const store = following(['id:2']);
    const listener = vi.fn();
    store.subscribe(listener);

    store.apply(focusFrame());
    expect(listener).toHaveBeenCalledTimes(1);

    for (let lapNumber = 2; lapNumber <= 200; lapNumber++) {
      store.apply(focusFrame({ lapNumber }));
    }

    expect(listener).toHaveBeenCalledTimes(1);
    expect(store.frameFor('id:2')?.lapNumber).toBe(200);
  });

  /** Two streams, two announcements — still one each, not one per frame of either. */
  it('notifies once per driver when a second joins the comparison mid-stream', () => {
    const store = following(['id:2', 'id:9']);
    const listener = vi.fn();
    store.subscribe(listener);

    for (let i = 0; i < 50; i++) {
      store.apply(focusFrame({ driverKey: 'id:2' }));
      store.apply(focusFrame({ driverKey: 'id:9' }));
    }

    expect(listener).toHaveBeenCalledTimes(2);
    expect(store.getFocusReadyKeys()).toEqual(new Set(['id:2', 'id:9']));
  });

  /** The click acknowledgement: subscribed is not the same as streaming, and the panel must know. */
  it('reports a driver as ready only once a frame has actually arrived', () => {
    const store = following(['id:2']);

    expect(store.getFocusReadyKeys().has('id:2')).toBe(false);

    store.apply(focusFrame());

    expect(store.getFocusReadyKeys().has('id:2')).toBe(true);
  });

  /** A stream that has stopped is not one whose panel should still read as live. */
  it('stops reporting a driver as ready when the stream is interrupted, and again when it resumes', () => {
    const store = following(['id:2']);
    store.apply(focusFrame());

    store.interruptFocus();
    expect(store.getFocusReadyKeys().has('id:2')).toBe(false);

    store.apply(focusFrame());
    expect(store.getFocusReadyKeys().has('id:2')).toBe(true);
  });

  it('drops readiness for a driver that went away, and keeps it for the one that stayed', () => {
    const store = following(['id:2', 'id:9']);
    store.apply(focusFrame({ driverKey: 'id:2' }));
    store.apply(focusFrame({ driverKey: 'id:9' }));

    store.setFollowedDrivers(['id:9']);

    expect(store.getFocusReadyKeys()).toEqual(new Set(['id:9']));
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

  /** An outage leaves a hole in the tyre rings too, and the first frame back lands straight after it. */
  it('resumes the tyre rings after an interruption', () => {
    const store = following(['id:2']);
    store.apply(stintFrame({ tyreWear: [0.25, 0.25, 0.25, 0.25] }));

    store.interruptFocus();
    store.apply(stintFrame({ tyreWear: [0.5, 0.5, 0.5, 0.5] }));

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
   * The decimation moved to the publisher, and this is what proves the store no longer does it: one
   * arrival, one sample. It used to drop fifty-nine of every sixty *after* the hub had serialised
   * them and the socket had carried them, which was the right thinning happening three processes
   * too late.
   *
   * The two rates are still separate — a hundred focus frames advance the pedal rings and leave the
   * tyre rings alone, because tyres arrive on their own message.
   */
  it('takes one tyre sample per stint frame, and none from a focus frame', () => {
    const store = following(['id:2']);

    for (let i = 0; i < 100; i++) {
      store.apply(focusFrame());
    }

    store.apply(stintFrame({ tyreWear: [0.25, 0.25, 0.25, 0.25] }));
    store.apply(stintFrame({ tyreWear: [0.5, 0.5, 0.5, 0.5] }));

    expect([...store.tracesFor('id:2').tyres.wear[0].toArray()]).toEqual([0.25, 0.5]);
    expect(store.tracesFor('id:2').throttle.length).toBe(100);
  });

  /**
   * Brake pressure went the other way: it used to ride the once-a-second extras document, where a
   * one-second braking event was one or two samples. On the focus frame it shares an index with the
   * pedal, which is what makes pedal-against-pressure a comparison rather than two unrelated traces.
   */
  it('samples brake pressure at focus rate, on the same index as the pedal', () => {
    const store = following(['id:2']);

    // Quarters, because the rings are Float32Array and 3.1 comes back as 3.0999999046325684. The
    // reading under test is which index a sample lands on, not how many bits it survives, so the
    // values are chosen to round-trip exactly and leave the assertion about the thing it is for.
    store.apply(focusFrame({ brake: 0.5, brakePressureKiloNewtons: [3.5, 3.25, 1.5, 1.25] }));
    store.apply(focusFrame({ brake: 0.9, brakePressureKiloNewtons: [9.5, 9.25, 4.5, 4.25] }));

    const traces = store.tracesFor('id:2');

    expect([...traces.brakePressureKiloNewtons[0].toArray()]).toEqual([3.5, 9.5]);
    expect(traces.brakePressureKiloNewtons[0].length).toBe(traces.brake.length);
  });

  /** An unreported corner is a hole: at zero it would read as a brake that did nothing. */
  it('plots an unreported brake corner as a gap rather than as zero', () => {
    const store = following(['id:2']);

    store.apply(focusFrame({ brakePressureKiloNewtons: [3.5, null, 1.5, 1.25] }));

    expect(store.tracesFor('id:2').brakePressureKiloNewtons[1].last()).toBeNaN();
    expect(store.tracesFor('id:2').brakePressureKiloNewtons[0].last()).toBe(3.5);
  });

  it('keeps every tyre channel per wheel, in wire order', () => {
    const store = following(['id:2']);

    store.apply(
      stintFrame({
        tyrePressureKpa: [180, 181, 175, 176],
        tyreTemperatureCelsius: [tread(85), tread(86), tread(82), tread(83)],
      }),
    );

    const { pressureKpa, temperatureCelsius } = store.tracesFor('id:2').tyres;

    expect(pressureKpa.map((buffer) => buffer.last())).toEqual([180, 181, 175, 176]);

    // The stint trace plots the middle of the tread — one line per tyre. `tread()` puts the
    // shoulders either side of it, so reading the wrong one would show here.
    expect(temperatureCelsius.map((buffer) => buffer.last())).toEqual([85, 86, 82, 83]);
  });

  /**
   * The live path used to carry the middle reading alone, which made two whole charts unbuildable:
   * the shoulder spread that shows a camber problem, and the band the simulator is willing to name.
   * Both arrive on the stint frame now, and neither belongs in a ring — a spread is read across the
   * car at one instant, and a window does not move — so the frame is where a widget reaches for them.
   */
  it('keeps the tread shoulders and the operating window on the stint frame', () => {
    const store = following(['id:2']);

    store.apply(
      stintFrame({
        tyreTemperatureCelsius: [tread(85), tread(86), tread(82), tread(83)],
      }),
    );

    const frontLeft = store.stintFor('id:2')!.tyreTemperatureCelsius[0]!;

    expect(frontLeft.inner).toBe(87);
    expect(frontLeft.outer).toBe(83);
    expect(frontLeft.optimal).toBe(90);
    expect(frontLeft.cold).toBe(70);
    expect(frontLeft.hot).toBe(110);
  });

  /**
   * A simulator that names no band must leave the dashboard drawing none. An absent bound read as
   * zero would put every tyre on the car permanently over its hot threshold.
   */
  it('leaves an unreported window absent rather than zero', () => {
    const store = following(['id:2']);

    store.apply(stintFrame({ tyreTemperatureCelsius: [{ middle: 85 }, {}, {}, {}] }));

    const frontLeft = store.stintFor('id:2')!.tyreTemperatureCelsius[0]!;

    expect(frontLeft.optimal).toBeUndefined();
    expect(frontLeft.hot).toBeUndefined();
    expect(store.tracesFor('id:2').tyres.temperatureCelsius[0].last()).toBe(85);

    // And a tyre reporting nothing at all leaves a hole, not a reading at zero.
    expect(store.tracesFor('id:2').tyres.temperatureCelsius[1].last()).toBeNaN();
  });

  /**
   * The same discipline the pedals follow. Tyre arrays are nullable on the wire, and a wheel the
   * simulator did not report must leave a hole — drawn at zero it would read as a flat tyre.
   */
  it('plots an unreported wheel as a gap rather than as zero', () => {
    const store = following(['id:2']);

    store.apply(stintFrame({ tyrePressureKpa: [180, null, 175, 176] }));

    expect(Number.isNaN(store.tracesFor('id:2').tyres.pressureKpa[1].last())).toBe(true);
    expect(store.tracesFor('id:2').tyres.pressureKpa[0].last()).toBe(180);
  });

  /**
   * Fixed-size rings, so an hour of stint costs the same memory as a minute of it. A growing array
   * here would be a slow leak over a two-hour race.
   */
  it('keeps the tyre rings flat over a long stint', () => {
    const store = following(['id:2']);

    for (let i = 0; i < TYRE_TRACE_CAPACITY + 500; i++) {
      store.apply(stintFrame({ tyreWear: [i / 10_000, 0, 0, 0] }));
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

    // Decoded on arrival, so a reader gets the document rather than the string it came in.
    expect(store.getExtras()['id:2']?.document?.damage?.engine).toBe(0.5);
    expect(store.getExtras()['id:9']).toBeUndefined();
  });

  it('decodes one extras document once, and hands every reader the same object', () => {
    const store = following(['id:2']);

    store.apply(extrasFrame({ extras: '{"tyreGrip":[0.9,0.9,0.88,0.88]}' }));

    // Identity, not equality. Two tiles reading the same frame must not each get their own decode —
    // that is the cost this whole change exists to remove.
    expect(store.getExtras()['id:2']?.document).toBe(store.getExtras()['id:2']?.document);
  });

  it('survives an extras payload that will not parse', () => {
    const store = following(['id:2']);

    expect(() => store.apply(extrasFrame({ extras: 'not json at all' }))).not.toThrow();
    expect(store.getExtras()['id:2']?.document).toBeNull();

    // And the rings still advance, so the hole is drawn rather than closed over.
    expect(store.tracesFor('id:2').extras.tyreGrip[0].length).toBe(1);
    expect(store.tracesFor('id:2').extras.tyreGrip[0].last()).toBeNaN();
  });

  it('pushes extras channels into rings, treating the -1 sentinel as absent', () => {
    const store = following(['id:2']);

    store.apply(
      extrasFrame({
        extras: JSON.stringify({
          brakeTemperatureCelsius: [
            { current: 320, optimal: 400, cold: 200, hot: 800 },
            { current: -1, optimal: 400, cold: 200, hot: 800 },
            { current: 300, optimal: 400, cold: 200, hot: 800 },
            { current: 300, optimal: 400, cold: 200, hot: 800 },
          ],
          turboPressureBar: -1,
          engineOilTempCelsius: 104,
        }),
      }),
    );

    const { extras } = store.tracesFor('id:2');

    // The ring holds the reading. A brake temperature is an object now, carrying its window
    // alongside, and the trace plots the one member that moves.
    expect(extras.brakeTemperatureCelsius[0].last()).toBe(320);
    // A brake at -1 °C is not a cold brake. It is a reading that was never taken.
    expect(extras.brakeTemperatureCelsius[1].last()).toBeNaN();
    expect(extras.turboPressureBar.last()).toBeNaN();
    expect(extras.engineOilTempCelsius.last()).toBe(104);
  });

  /**
   * The window the simulator names for these pads. It stays on the document rather than going into
   * a ring, for the reason a tyre's does: a band does not move, so a rolling history of it would be
   * fifteen minutes of the same four numbers.
   */
  it('keeps the brake operating window on the parsed document', () => {
    const store = following(['id:2']);

    store.apply(
      extrasFrame({
        extras: JSON.stringify({
          brakeTemperatureCelsius: [{ current: 320, optimal: 400, cold: 200, hot: 800 }],
        }),
      }),
    );

    const frontLeft = store.getExtras()['id:2']?.document?.brakeTemperatureCelsius?.[0];

    expect(frontLeft?.optimal).toBe(400);
    expect(frontLeft?.cold).toBe(200);
    expect(frontLeft?.hot).toBe(800);
  });

  it('advances the extras rings once per document, not once per delivery', () => {
    const store = following(['id:2']);
    const frame = extrasFrame({ extras: '{"turboPressureBar":1.4}' });

    store.apply(frame);
    store.apply(frame);

    // The same capture time twice is one sample. Otherwise a re-delivery would stretch the window
    // and the width of a stint would depend on how the socket happened to behave.
    expect(store.tracesFor('id:2').extras.turboPressureBar.length).toBe(1);

    store.apply(
      extrasFrame({ capturedAtUtc: '2026-08-16T12:00:01Z', extras: '{"turboPressureBar":1.5}' }),
    );
    expect(store.tracesFor('id:2').extras.turboPressureBar.length).toBe(2);
  });

  it('refuses an extras frame for a driver nobody is following', () => {
    const store = following(['id:2']);

    store.apply(extrasFrame({ driverKey: 'id:9' }));

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

/** One driver's lap history, as the hub sends it: always a full snapshot, never a delta. */
function lapHistoryFor(laps: { lapNumber: number; lapTimeMs: number; valid?: boolean }[]) {
  return {
    type: 'lapHistory' as const,
    roomId: 'room',
    driverKey: 'id:2',
    truncated: false,
    laps: laps.map(({ lapNumber, lapTimeMs, valid }) => ({
      lapNumber,
      lapTimeMs,
      sectorMs: [],
      ...(valid === undefined ? {} : { valid }),
    })),
  };
}

function towerRow(driverKey: string, overrides: Partial<TowerRow> = {}): TowerRow {
  return {
    driverKey,
    displayName: driverKey,
    currentSectorMs: [],
    previousSectorMs: [],
    bestSectorMs: [],
    pitLaneState: -1,
    pitStopStatus: -1,
    finishStatus: 0,
    tier: 'Self',
    ...overrides,
  };
}

/**
 * Lap-indexed traces, the reference lap, and the derived series.
 *
 * These are what make a delta possible without a track map — the constraint the whole chart backlog
 * was written under — so most of what is pinned here is the ways a lap can be disqualified from
 * being a reference. That is where a wrong answer would be invisible rather than obvious: a delta
 * measured against a bad lap still draws a confident line.
 */
describe('LiveStore lap traces', () => {
  /** Drives one whole lap through the store, a hundred samples of it, at the given lap time. */
  function driveLap(store: LiveStore, lapNumber: number, lapSeconds: number, startedAt: number) {
    for (let step = 0; step <= 100; step++) {
      store.apply(
        focusFrame({
          lapNumber,
          trackPositionFraction: step / 100,
          simulationTime: startedAt + (step / 100) * lapSeconds,
        }),
      );
    }

    return startedAt + lapSeconds;
  }

  it('bins a lap by track position and closes it when the lap counter moves', () => {
    const store = following(['id:2']);

    const ended = driveLap(store, 1, 90, 0);
    expect(store.currentLapFor('id:2')?.lapNumber).toBe(1);

    store.apply(focusFrame({ lapNumber: 2, trackPositionFraction: 0, simulationTime: ended }));

    expect(store.currentLapFor('id:2')?.lapNumber).toBe(2);
  });

  it('ignores a fraction that has gone backwards inside a lap', () => {
    const store = following(['id:2']);

    store.apply(focusFrame({ lapNumber: 1, trackPositionFraction: 0.99, simulationTime: 89 }));
    // The wrap arriving before the lap counter does. Writing it would leave a lap that is a blend
    // of two — the last metres of one and the first of the next.
    store.apply(focusFrame({ lapNumber: 1, trackPositionFraction: 0.01, simulationTime: 90 }));

    expect(store.currentLapFor('id:2')?.elapsedAt(10)).toBeNaN();
  });

  it('measures against the fastest lap the simulator did not refuse', () => {
    const store = following(['id:2']);

    let at = driveLap(store, 1, 92, 0);
    at = driveLap(store, 2, 90, at);
    driveLap(store, 3, 91, at);

    store.apply(
      lapHistoryFor([
        { lapNumber: 1, lapTimeMs: 92_000, valid: true },
        { lapNumber: 2, lapTimeMs: 90_000, valid: true },
      ]),
    );

    expect(store.referenceLapFor('id:2')?.lapNumber).toBe(2);
  });

  it('refuses a lap the simulator invalidated, but not one it said nothing about', () => {
    const store = following(['id:2']);

    let at = driveLap(store, 1, 92, 0);
    at = driveLap(store, 2, 88, at);
    // A third lap, only so the second one closes. A lap still being driven is not a reference no
    // matter how fast it is, which is what the completeness rule is for.
    driveLap(store, 3, 95, at);

    store.apply(
      lapHistoryFor([
        { lapNumber: 1, lapTimeMs: 92_000, valid: true },
        { lapNumber: 2, lapTimeMs: 88_000, valid: false },
      ]),
    );
    expect(store.referenceLapFor('id:2')?.lapNumber).toBe(1);

    // Unknown is not invalid. Refusing every lap a simulator declined to comment on would leave
    // most sessions with no reference at all, which is worse than the risk it avoids.
    store.apply(
      lapHistoryFor([
        { lapNumber: 1, lapTimeMs: 92_000, valid: true },
        { lapNumber: 2, lapTimeMs: 88_000 },
      ]),
    );
    expect(store.referenceLapFor('id:2')?.lapNumber).toBe(2);
  });

  it('never uses a lap that was joined halfway through', () => {
    const store = following(['id:2']);

    // Watched from a third of the way round — a lap with a hole where its first sector should be.
    for (let step = 33; step <= 100; step++) {
      store.apply(
        focusFrame({ lapNumber: 1, trackPositionFraction: step / 100, simulationTime: step }),
      );
    }
    store.apply(focusFrame({ lapNumber: 2, trackPositionFraction: 0, simulationTime: 101 }));

    store.apply(lapHistoryFor([{ lapNumber: 1, lapTimeMs: 70_000, valid: true }]));

    // Fastest on the timing sheet, and still not a reference: there is nothing to compare the first
    // third of a lap against.
    expect(store.referenceLapFor('id:2')).toBeNull();
  });

  it('reports a delta in seconds gained and lost', () => {
    const store = following(['id:2']);

    const at = driveLap(store, 1, 90, 0);
    driveLap(store, 2, 92, at);

    store.apply(lapHistoryFor([{ lapNumber: 1, lapTimeMs: 90_000, valid: true }]));

    const reference = store.referenceLapFor('id:2');
    const delta = store.currentLapFor('id:2')!.deltaTo(reference!);

    // Two seconds slower over a lap driven evenly slower everywhere, and level at the line.
    expect(delta[LAP_BINS - 1]).toBeCloseTo(2, 1);
    expect(delta[0]).toBeCloseTo(0, 5);
  });

  it('keeps gear and rpm as traces, with an unreported gear as a hole', () => {
    const store = following(['id:2']);

    store.apply(focusFrame({ gear: 4, engineRpm: 7200 }));
    store.apply(focusFrame({ gear: null, engineRpm: 7300 }));

    const traces = store.tracesFor('id:2');
    expect(traces.engineRpm.last()).toBe(7300);
    // Neutral is a real gear, so an unreported gearbox cannot be drawn as one.
    expect(traces.gear.last()).toBeNaN();
  });
});

describe('LiveStore race timeline and per-lap series', () => {
  it('derives gap to leader by summing the gaps ahead down the order', () => {
    const store = new LiveStore(() => 0);

    store.apply({
      ...tower,
      drivers: [
        towerRow('id:1', { position: 1, gapToCarAheadMs: null }),
        towerRow('id:2', { position: 2, gapToCarAheadMs: 1_500 }),
        towerRow('id:3', { position: 3, gapToCarAheadMs: 2_500 }),
      ],
    });

    expect(store.raceTracesFor('id:1').gapToLeaderSeconds.last()).toBe(0);
    expect(store.raceTracesFor('id:2').gapToLeaderSeconds.last()).toBeCloseTo(1.5, 5);
    // Four seconds back, not two and a half: the sum walks down the order.
    expect(store.raceTracesFor('id:3').gapToLeaderSeconds.last()).toBeCloseTo(4, 5);
  });

  it('breaks the gap chain rather than guessing past a missing link', () => {
    const store = new LiveStore(() => 0);

    store.apply({
      ...tower,
      drivers: [
        towerRow('id:1', { position: 1, gapToCarAheadMs: null }),
        towerRow('id:2', { position: 2, gapToCarAheadMs: null }),
        towerRow('id:3', { position: 3, gapToCarAheadMs: 2_000 }),
      ],
    });

    // Everyone behind an unreported gap is an unknown distance back, and a hole is the honest
    // drawing of that.
    expect(store.raceTracesFor('id:2').gapToLeaderSeconds.last()).toBeNaN();
    expect(store.raceTracesFor('id:3').gapToLeaderSeconds.last()).toBeNaN();
  });

  it('decimates the race rings by elapsed time, not by snapshot count', () => {
    const clock = { now: 0 };
    const store = new LiveStore(() => clock.now);
    const snapshot = { ...tower, drivers: [towerRow('id:1', { position: 1 })] };

    store.apply(snapshot);
    for (let i = 0; i < 9; i++) {
      clock.now += 100;
      store.apply(snapshot);
    }

    // Ten snapshots inside one second is one sample. A race lasts hours and the tower arrives ten
    // times a second; without this the ring would hold four minutes of it.
    expect(store.raceTracesFor('id:1').position.length).toBe(1);
  });

  it('derives fuel used per lap, and replaces the summaries rather than mutating them', () => {
    const store = following(['id:2']);

    store.apply(focusFrame({ lapNumber: 1, trackPositionFraction: 0.9, fuelLeftLiters: 40 }));
    store.apply(focusFrame({ lapNumber: 2, trackPositionFraction: 0.1, fuelLeftLiters: 37.5 }));
    store.apply(focusFrame({ lapNumber: 3, trackPositionFraction: 0.1, fuelLeftLiters: 35 }));

    store.apply(
      lapHistoryFor([
        { lapNumber: 1, lapTimeMs: 90_000, valid: true },
        { lapNumber: 2, lapTimeMs: 90_500, valid: true },
      ]),
    );

    const before = store.getLapSummaries()['id:2'];
    expect(before?.[0]?.fuelLeftLiters).toBe(40);
    // The first lap seen has nothing to subtract from, so its consumption is unknown rather than a
    // full tank's worth.
    expect(before?.[0]?.fuelUsedLiters).toBeNull();
    expect(before?.[1]?.fuelUsedLiters).toBeCloseTo(2.5, 5);

    store.apply(focusFrame({ lapNumber: 4, trackPositionFraction: 0.1, fuelLeftLiters: 32.5 }));

    // Replaced, never mutated: useSyncExternalStore compares by identity, and a mutated array would
    // never look changed.
    expect(store.getLapSummaries()['id:2']).not.toBe(before);
  });
});
