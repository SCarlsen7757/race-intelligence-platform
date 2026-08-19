import type {
  ExtrasFrameMessage,
  FocusFrameMessage,
  LapHistoryMessage,
  LiveErrorMessage,
  LiveRoomSummary,
  LiveViewMessage,
  RaceRoomExtras,
  SessionStateMessage,
  TowerSnapshotMessage,
} from './contracts';
import { parseExtras, reportedOrNaN } from './extras';

/**
 * How many focus frames the trace buffers keep.
 *
 * At 60 Hz this is thirty seconds — long enough to see the whole braking, turn-in and exit of a
 * corner, and to compare the last few. Ring buffers, so memory is flat no matter how
 * long a session runs; a growing array would be a slow leak over a two-hour race.
 */
export const TRACE_CAPACITY = 1800;

/** A fixed-size ring of numbers, written every frame and read once per paint. */
export class TraceBuffer {
  private readonly values: Float32Array;
  private writeIndex = 0;
  private filled = 0;
  private pushCount = 0;

  constructor(capacity: number = TRACE_CAPACITY) {
    this.values = new Float32Array(capacity);
  }

  get length(): number {
    return this.filled;
  }

  /**
   * How many values have ever been pushed.
   *
   * A paint loop cannot use `length` to tell whether there is anything new to draw — it plateaus
   * the moment the ring is full, and a chart keyed on it would freeze an hour into a stint. This
   * never stops moving, so it is what a loop that only wants to repaint on new data should watch.
   */
  get version(): number {
    return this.pushCount;
  }

  push(value: number): void {
    this.values[this.writeIndex] = value;
    this.writeIndex = (this.writeIndex + 1) % this.values.length;
    if (this.filled < this.values.length) {
      this.filled++;
    }
    this.pushCount++;
  }

  clear(): void {
    this.writeIndex = 0;
    this.filled = 0;
    this.pushCount = 0;
  }

  /** The most recently pushed value, or NaN when nothing has been pushed yet. */
  last(): number {
    if (this.filled === 0) {
      return Number.NaN;
    }

    const index = (this.writeIndex - 1 + this.values.length) % this.values.length;
    return this.values[index]!;
  }

  /**
   * Copies the ring out in oldest-to-newest order.
   *
   * Into a caller-supplied array when given one, because the chart re-reads this every animation
   * frame and allocating a fresh array 60 times a second is exactly the garbage this design
   * exists to avoid.
   */
  toArray(into?: Float64Array<ArrayBuffer>): Float64Array<ArrayBuffer> {
    const out = into && into.length === this.filled ? into : new Float64Array(this.filled);
    const start = this.filled === this.values.length ? this.writeIndex : 0;

    for (let i = 0; i < this.filled; i++) {
      out[i] = this.values[(start + i) % this.values.length]!;
    }

    return out;
  }

  /**
   * Copies the ring out in the shape a chart understands, turning every NaN into null.
   *
   * **This is what makes "absent is not zero" true on screen and not only in memory.** uPlot decides
   * where a line breaks by testing values against `null`, and a NaN passes that test as a number:
   * it is drawn, `lineTo` silently ignores the non-finite coordinate, and the path carries on from
   * the previous point to the next one. The result is a confident straight line across data that
   * was never captured — the exact reading `spanGaps: false` is set everywhere to prevent, quietly
   * not working. There is no NaN handling anywhere in the library to lean on instead.
   *
   * A plain array rather than a typed one, because that is the only shape that can hold the null.
   * It is still reused between paints — only the contents are written — so the paint loops keep
   * allocating nothing, which is the constraint the whole ring-buffer design exists to satisfy.
   */
  toNullableArray(into?: (number | null)[]): (number | null)[] {
    const out = into && into.length === this.filled ? into : new Array<number | null>(this.filled);
    const start = this.filled === this.values.length ? this.writeIndex : 0;

    for (let i = 0; i < this.filled; i++) {
      const value = this.values[(start + i) % this.values.length]!;
      out[i] = Number.isNaN(value) ? null : value;
    }

    return out;
  }
}

/**
 * One lap, sampled across lap progress rather than across time.
 *
 * Each bin holds the elapsed time at which the car reached that point of the lap. A delta between
 * two laps is then a subtraction bin by bin, and the answer is in seconds gained or lost at every
 * point of the circuit — which is the chart a driver actually wants, and the one the handover this
 * milestone came from rates highest.
 *
 * Bins never visited stay NaN, so a lap joined halfway through has holes rather than a fabricated
 * first sector. That is also what makes {@link isComplete} answerable, and a lap that cannot answer
 * it must never become a reference.
 */
export class LapTrace {
  /** Elapsed lap time at each bin, NaN where the car was never observed. */
  private readonly elapsed = new Float32Array(LAP_BINS).fill(Number.NaN);
  private filled = 0;

  /** The lap this is recording. */
  constructor(readonly lapNumber: number) {}

  /**
   * The furthest bin written so far, used to refuse a fraction that has gone backwards.
   *
   * A lap that wrapped without the lap counter incrementing yet — which happens, because the two
   * arrive in the same frame only by luck — would otherwise write its first metres over the last
   * ones and leave a lap that is a blend of two. Refusing anything behind the high-water mark is
   * cheaper and more certain than trying to detect the wrap.
   */
  private highWaterBin = -1;

  /** How many bins hold a reading. */
  get sampleCount(): number {
    return this.filled;
  }

  /**
   * Whether this lap was observed from the line to the line.
   *
   * Not every bin — a 60 Hz sample of a fast straight genuinely skips bins, and demanding all
   * thousand would disqualify every lap ever driven. What matters is that the lap was being watched
   * for its whole length, which the first and last bins answer.
   */
  isComplete(): boolean {
    return !Number.isNaN(this.elapsed[0]!) && !Number.isNaN(this.elapsed[LAP_BINS - 1]!);
  }

  /**
   * Records where the car was, and how long the lap had taken to get there.
   *
   * Silently ignores a fraction outside 0..1 or behind the high-water mark — see
   * {@link highWaterBin}.
   */
  push(fraction: number, elapsedSeconds: number): void {
    if (!Number.isFinite(fraction) || fraction < 0 || fraction > 1) {
      return;
    }

    const bin = Math.min(LAP_BINS - 1, Math.floor(fraction * LAP_BINS));
    if (bin < this.highWaterBin) {
      return;
    }

    this.highWaterBin = bin;

    if (Number.isNaN(this.elapsed[bin]!)) {
      this.filled++;
    }

    this.elapsed[bin] = elapsedSeconds;
  }

  /** Elapsed time at one bin, or NaN where the car was never seen. */
  elapsedAt(bin: number): number {
    return this.elapsed[bin] ?? Number.NaN;
  }

  /**
   * This lap's time against a reference, bin by bin, as seconds gained (negative) or lost.
   *
   * NaN wherever either lap has no reading, so the chart draws a hole rather than a delta computed
   * against a time nobody set. Written into a caller-supplied array for the usual reason: this is
   * read from a paint loop.
   */
  deltaTo(reference: LapTrace, into?: Float64Array<ArrayBuffer>): Float64Array<ArrayBuffer> {
    const out = into && into.length === LAP_BINS ? into : new Float64Array(LAP_BINS);

    for (let bin = 0; bin < LAP_BINS; bin++) {
      out[bin] = this.elapsed[bin]! - reference.elapsed[bin]!;
    }

    return out;
  }
}

/**
 * How many decimated samples the tyre traces keep, and how far apart they are taken.
 *
 * A tyre asks a different question from a pedal. Throttle and brake are read a corner at a time, so
 * a minute of samples at full rate is the right window; pressure, wear and temperature move over a
 * stint, and sixty seconds of them is a flat line that says nothing. Sampling once a second and
 * keeping 900 slots gives the requested rolling fifteen-minute stint window.
 *
 * Decimated by elapsed time rather than by counting frames, because the collector's poll rate is
 * not a constant: a machine under load reports fewer frames per second, and a fixed "every 60th"
 * would silently stretch the window whenever the game got busy.
 */
export const TYRE_TRACE_CAPACITY = 900;
export const TYRE_SAMPLE_INTERVAL_MS = 1000;

/**
 * One assist flag as a ring holds it: 1 intervening, 0 watched and quiet, NaN never reported.
 *
 * Nullish rather than falsy, which is the whole point. `flag ? 1 : NaN` reads the same for a
 * simulator that said "no" and one that said nothing, and those are the two cases a trace has to
 * keep apart — see {@link FocusTraces.absActive}.
 */
function assistState(flag: boolean | null | undefined): number {
  return flag === null || flag === undefined ? Number.NaN : flag ? 1 : 0;
}

/** One per-wheel channel over time, in the wire's wheel order — FL, FR, RL, RR. */
export type WheelTraces = readonly [TraceBuffer, TraceBuffer, TraceBuffer, TraceBuffer];

/**
 * How many bins one lap is divided into, across normalised lap progress.
 *
 * **This is what makes a lap comparison possible without a track map.** The wire carries
 * `trackPositionFraction`, 0 at the line and 1 back at it, so a lap can be indexed by *where the car
 * is* rather than by when a sample happened to arrive. Two laps then line up bin for bin, however
 * differently they were driven — no circuit geometry, no corner definitions, nothing for anyone to
 * maintain per track. That constraint is the whole reason this milestone's charts are buildable.
 *
 * A thousand bins is about ten metres on a five-kilometre lap, which is finer than the difference
 * any driver can act on and coarse enough that a lap is a few tens of kilobytes.
 */
export const LAP_BINS = 1000;

/**
 * The extras channels this dashboard plots, kept on the tyre rings' slower sample index.
 *
 * Extras arrive at roughly 1 Hz on their own channel, which is the same cadence the tyre rings
 * decimate to, so they share a capacity and a window rather than inventing a third timebase.
 *
 * Held by the store rather than by the panels that draw them. They used to live in a ref inside
 * `BrakePanel`, which worked and was the only such exception on screen — every other channel is a
 * ring the store owns. The exception existed only because the extras document was parsed per panel;
 * with one parse on arrival there is no longer a reason for it, and a brake chart becomes an
 * ordinary `LiveChart` caller like every other chart.
 */
export interface ExtrasTraces {
  // Per-wheel channels, FL/FR/RL/RR.
  brakeTemperatureCelsius: WheelTraces;
  brakePressureKiloNewtons: WheelTraces;
  brakeWear: WheelTraces;
  tyreGrip: WheelTraces;
  tyreLoadNewtons: WheelTraces;

  // Scalars — car health and energy, which move over a stint rather than through a corner.
  engineTempCelsius: TraceBuffer;
  engineOilTempCelsius: TraceBuffer;
  engineOilPressureKpa: TraceBuffer;
  fuelPressureKpa: TraceBuffer;
  turboPressureBar: TraceBuffer;
  batteryStateOfChargePercent: TraceBuffer;
  virtualEnergyLeftMj: TraceBuffer;
}

/**
 * How much of the race the gap and position rings keep, and how far apart their samples are.
 *
 * A tower snapshot arrives about ten times a second and a race lasts hours, so a timeline needs
 * decimating far harder than a tyre chart does. One sample a second over two hours is 7,200 slots,
 * which is a few tens of kilobytes per driver and enough resolution to see an undercut land.
 */
export const RACE_TRACE_CAPACITY = 7200;
export const RACE_SAMPLE_INTERVAL_MS = 1000;

/** One driver's race, as the timeline widget reads it. */
export interface RaceTraces {
  /** Running position. Whole numbers, but a ring holds floats and a position is never absent. */
  position: TraceBuffer;
  /**
   * Seconds behind the leader.
   *
   * Derived rather than read: the wire carries only the gap to the car ahead, so the leader's own
   * gap is a running sum down the order. Computed once per snapshot here rather than once per
   * widget, because two widgets deriving it independently is two chances to derive it differently.
   */
  gapToLeaderSeconds: TraceBuffer;
}

/** One completed lap's derived numbers, as the fuel and pace widgets read them. */
export interface LapSummary {
  lapNumber: number;
  /** Fuel in the tank as the lap ended, or null where the frame never reported it. */
  fuelLeftLiters: number | null;
  /** Litres burned over this lap, or null on the first lap seen — there is nothing to subtract. */
  fuelUsedLiters: number | null;
  lapTimeMs: number | null;
  /** Whether the simulator refused the lap. Undefined is unknown, never invalid. */
  valid?: boolean;
}

/** One extras frame, decoded once. */
export interface ExtrasSnapshot {
  /** The message as it arrived, still carrying its capture time. */
  message: ExtrasFrameMessage;
  /**
   * The decoded document, or null when there was none or it would not parse.
   *
   * Decoded here so that a wall of extras-fed tiles shares one parse rather than each doing its
   * own. A reader must still treat every value as raw — see `reportedNumber`.
   */
  document: RaceRoomExtras | null;
}

/**
 * The tyre channels over a stint, sampled at {@link TYRE_SAMPLE_INTERVAL_MS}.
 *
 * Held apart from the input traces rather than alongside them because the two share no sample
 * index: plotting a 1 Hz series against a 60 Hz one on the same x axis would put a tyre reading
 * sixty times further back than it belongs.
 */
export interface TyreTraces {
  pressureKpa: WheelTraces;
  wear: WheelTraces;
  temperatureCelsius: WheelTraces;
}

/** The traces the wall plots for one driver. */
export interface FocusTraces {
  /** Input channels, one sample per focus frame, all sharing one sample index. */
  throttle: TraceBuffer;
  brake: TraceBuffer;
  clutch: TraceBuffer;
  steering: TraceBuffer;
  speed: TraceBuffer;
  gear: TraceBuffer;
  engineRpm: TraceBuffer;
  /**
   * Whether the car's assists were intervening, as 1, 0, or NaN for a simulator that stays quiet.
   *
   * Three states rather than two, and the third is what makes the trace honest. A flat line along
   * the bottom says the assist was watched and did nothing; a gap says nobody asked. Collapsing
   * those into a single falsy value would let a simulator that never reports ABS look exactly like
   * a lap where it never engaged, which is the difference the chart exists to show.
   */
  absActive: TraceBuffer;
  tractionControlActive: TraceBuffer;
  /** Tyre channels, on their own slower sample index. */
  tyres: TyreTraces;
  /** Extras channels, sharing the tyre rings' cadence — see {@link ExtrasTraces}. */
  extras: ExtrasTraces;
}

function wheelTraces(): WheelTraces {
  return [
    new TraceBuffer(TYRE_TRACE_CAPACITY),
    new TraceBuffer(TYRE_TRACE_CAPACITY),
    new TraceBuffer(TYRE_TRACE_CAPACITY),
    new TraceBuffer(TYRE_TRACE_CAPACITY),
  ];
}

function extrasTraces(): ExtrasTraces {
  return {
    brakeTemperatureCelsius: wheelTraces(),
    brakePressureKiloNewtons: wheelTraces(),
    brakeWear: wheelTraces(),
    tyreGrip: wheelTraces(),
    tyreLoadNewtons: wheelTraces(),
    engineTempCelsius: new TraceBuffer(TYRE_TRACE_CAPACITY),
    engineOilTempCelsius: new TraceBuffer(TYRE_TRACE_CAPACITY),
    engineOilPressureKpa: new TraceBuffer(TYRE_TRACE_CAPACITY),
    fuelPressureKpa: new TraceBuffer(TYRE_TRACE_CAPACITY),
    turboPressureBar: new TraceBuffer(TYRE_TRACE_CAPACITY),
    batteryStateOfChargePercent: new TraceBuffer(TYRE_TRACE_CAPACITY),
    virtualEnergyLeftMj: new TraceBuffer(TYRE_TRACE_CAPACITY),
  };
}

function newTraces(): FocusTraces {
  return {
    throttle: new TraceBuffer(),
    brake: new TraceBuffer(),
    clutch: new TraceBuffer(),
    steering: new TraceBuffer(),
    speed: new TraceBuffer(),
    gear: new TraceBuffer(),
    engineRpm: new TraceBuffer(),
    absActive: new TraceBuffer(),
    tractionControlActive: new TraceBuffer(),
    tyres: {
      pressureKpa: wheelTraces(),
      wear: wheelTraces(),
      temperatureCelsius: wheelTraces(),
    },
    extras: extrasTraces(),
  };
}

/**
 * Every per-wheel extras channel, paired with how one wheel's value is read out of the document.
 *
 * A reader rather than a field name, because the channels no longer share a shape: a brake
 * temperature is an object carrying its operating window beside the reading, where the rest are
 * bare numbers. Keeping the difference here means the push loop stays one loop, and the knowledge
 * of what the document looks like stays in one table rather than spreading into it.
 */
const EXTRAS_WHEEL_CHANNELS = [
  ['brakeTemperatureCelsius', (extras, wheel) => extras.brakeTemperatureCelsius?.[wheel]?.current],
  ['brakePressureKiloNewtons', (extras, wheel) => extras.brakePressureKiloNewtons?.[wheel]],
  ['brakeWear', (extras, wheel) => extras.brakeWear?.[wheel]],
  ['tyreGrip', (extras, wheel) => extras.tyreGrip?.[wheel]],
  ['tyreLoadNewtons', (extras, wheel) => extras.tyreLoadNewtons?.[wheel]],
] as const satisfies readonly (readonly [
  keyof ExtrasTraces,
  (extras: RaceRoomExtras, wheel: number) => number | undefined,
])[];

/** Every scalar extras channel, paired with where it is read from the document. */
const EXTRAS_SCALAR_CHANNELS = [
  ['engineTempCelsius', 'engineTempCelsius'],
  ['engineOilTempCelsius', 'engineOilTempCelsius'],
  ['engineOilPressureKpa', 'engineOilPressureKpa'],
  ['fuelPressureKpa', 'fuelPressureKpa'],
  ['turboPressureBar', 'turboPressureBar'],
  ['batteryStateOfChargePercent', 'batteryStateOfChargePercent'],
  ['virtualEnergyLeftMj', 'virtualEnergyLeftMj'],
] as const satisfies readonly (readonly [keyof ExtrasTraces, keyof RaceRoomExtras])[];

/** Everything held for one followed driver. Plain fields — none of this goes through React. */
interface DriverFocus {
  frame: FocusFrameMessage | null;
  traces: FocusTraces;
  framesReceived: number;
  /** When the tyre rings last took a sample, so the decimation is by time and not by frame count. */
  lastTyreSampleAtMs: number;
  /**
   * The capture time of the extras frame last pushed into the rings.
   *
   * Extras are already decimated by the collector, so the rings follow the frames rather than a
   * clock. This is what stops a re-delivered or repeated document being counted twice — the sample
   * index has to advance once per document, or a stint's width would depend on how the socket
   * happened to behave.
   */
  lastExtrasCapturedAtUtc: string | null;

  /** The lap being recorded, or null before the first frame names one. */
  currentLap: LapTrace | null;
  /**
   * Fuel on the last frame seen, so a lap can be closed with the reading it ended on.
   *
   * The frame that reports the new lap number is already into the next lap, so its fuel belongs to
   * that lap and not the one just finished. This holds the previous one.
   */
  lastFuelLeftLiters: number;
  /**
   * `simulationTime` when the current lap started, so elapsed time is a subtraction.
   *
   * Taken from the frame rather than the wall clock: the simulator's own clock is the only one that
   * stops when the game is paused and does not drift against the samples it stamps.
   */
  currentLapStartedAt: number;
  /** Complete laps observed this session, keyed by lap number. */
  readonly completedLaps: Map<number, LapTrace>;
}

/**
 * Everything arriving over the live socket.
 *
 * **The 60 Hz rule lives here.** Focus frames are written straight into plain fields and never
 * touch React state — a `setState` per frame is a full render cycle sixty times a second, and it
 * drops frames on a laptop long before it drops them on a desktop. The focus panel reads these
 * fields from a single `requestAnimationFrame` loop and paints to canvas.
 *
 * The slow-changing half — the room list, the tower, lap history, extras, errors — goes through
 * React normally, because at 10 Hz and below the render cost is irrelevant and the ergonomics are
 * worth a great deal. `subscribe` is what React binds to, and it is deliberately *not* called for
 * focus frames.
 */
export class LiveStore {
  /**
   * Everything held per followed driver, keyed by driver key.
   *
   * A map rather than a single slot because two drivers can be compared side by side, and the two
   * streams must never mix: one set of rings per car is what makes "the same row means the same
   * channel in both" true of the data and not only of the layout.
   */
  private readonly focus = new Map<string, DriverFocus>();

  /**
   * The drivers the connection has actually subscribed to.
   *
   * Frames for anyone else are dropped. A frame for a driver just unfollowed can still be in flight
   * when the subscription changes, and admitting it would leave a car on screen that nobody asked
   * for — with rings that then never advance again.
   */
  private followedDriverKeys: ReadonlySet<string> = new Set();

  /**
   * The drivers currently holding a frame — the one thing about the focus stream React is told.
   *
   * Clicking "Show" subscribes and then sits on a panel full of em dashes until the first frame
   * lands. On a LAN that is twenty milliseconds and invisible; through a tunnel it is long enough
   * to click twice and wonder whether the first one registered.
   *
   * A set of keys rather than a count, and replaced rather than mutated, because it is written on
   * transitions only: gaining the first frame, losing the stream, dropping a subscription. That is
   * what keeps it inside the 60 Hz rule — one emit per subscription, not one per frame.
   */
  private focusReadyKeys: ReadonlySet<string> = new Set();

  private rooms: LiveRoomSummary[] = [];
  private tower: TowerSnapshotMessage | null = null;
  private sessionState: SessionStateMessage | null = null;
  private lastError: LiveErrorMessage | null = null;
  private connected = false;

  // Keyed by driver so several expanded rows can be held at once. Replaced rather than mutated:
  // useSyncExternalStore compares snapshots by identity, and a mutated map would never look
  // changed.
  private lapHistories: Readonly<Record<string, LapHistoryMessage>> = {};

  // Per driver, and replaced rather than mutated, for exactly the reasons lap histories are: two
  // damage panels can be on screen at once, and useSyncExternalStore compares by identity.
  //
  // Holds the decoded document beside the message, so the parse happens once here rather than once
  // per panel per frame.
  private extras: Readonly<Record<string, ExtrasSnapshot>> = {};

  /**
   * The race timeline, per driver, held outside React.
   *
   * The whole field, not only the followed car: a gap chart is about where everyone is, and
   * standings cover every driver in the session rather than only those running a collector. That is
   * also why this is keyed off tower snapshots rather than focus frames.
   */
  private readonly raceTraces = new Map<string, RaceTraces>();

  /** When the race rings last sampled, so the decimation is by elapsed time not by snapshot count. */
  private lastRaceSampleAtMs = Number.NEGATIVE_INFINITY;

  /**
   * Fuel in the tank as each lap ended, per driver, per lap number.
   *
   * Captured off the focus stream, which is the only place fuel is reported, but consumed by a
   * lap-rate summary — so it lives here as a plain field rather than in React, and only the summary
   * it feeds is state.
   */
  private readonly fuelByLap = new Map<string, Map<number, number>>();

  /**
   * Per-lap derived numbers, per driver.
   *
   * React state, and the one part of this that is: tens of values changing once a lap is exactly
   * what React is cheap for, and a fuel chart wanting a re-render per lap is a re-render per lap.
   * Replaced rather than mutated, because `useSyncExternalStore` compares by identity.
   */
  private lapSummaries: Readonly<Record<string, readonly LapSummary[]>> = {};

  private readonly listeners = new Set<() => void>();

  /**
   * @param now Reads the clock the tyre decimation measures against. Injected so a test can drive
   * an hour of stint through the store without waiting for one.
   */
  constructor(private readonly now: () => number = Date.now) {}

  /** Subscribes to slow-changing state. Never fires for focus frames — that is the whole point. */
  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  // Snapshot getters are stable references so React's useSyncExternalStore does not loop: each
  // returns the same object until something actually replaces it.
  getRooms = (): LiveRoomSummary[] => this.rooms;
  getTower = (): TowerSnapshotMessage | null => this.tower;
  getSessionState = (): SessionStateMessage | null => this.sessionState;
  getLastError = (): LiveErrorMessage | null => this.lastError;
  getLapHistories = (): Readonly<Record<string, LapHistoryMessage>> => this.lapHistories;
  getExtras = (): Readonly<Record<string, ExtrasSnapshot>> => this.extras;
  isConnected = (): boolean => this.connected;
  getFocusReadyKeys = (): ReadonlySet<string> => this.focusReadyKeys;
  getLapSummaries = (): Readonly<Record<string, readonly LapSummary[]>> => this.lapSummaries;

  /**
   * One driver's race timeline, created on demand.
   *
   * Created rather than returned-or-null for the same reason `tracesFor` is: a widget mounts before
   * the first snapshot names its driver and holds the reference for its lifetime.
   */
  raceTracesFor(driverKey: string): RaceTraces {
    let traces = this.raceTraces.get(driverKey);
    if (traces === undefined) {
      traces = {
        position: new TraceBuffer(RACE_TRACE_CAPACITY),
        gapToLeaderSeconds: new TraceBuffer(RACE_TRACE_CAPACITY),
      };

      this.raceTraces.set(driverKey, traces);
    }

    return traces;
  }

  /**
   * One driver's latest focus frame, or null before the first has arrived.
   *
   * Read from a paint loop, never from a render — see the class remarks. Returns null rather than
   * throwing for a driver nobody is following, because a panel can outlive its subscription by a
   * frame while React catches up with the URL.
   */
  frameFor(driverKey: string): FocusFrameMessage | null {
    return this.focus.get(driverKey)?.frame ?? null;
  }

  /**
   * One driver's rolling traces, created on demand.
   *
   * Created rather than returned-or-null because the panels mount before the first frame arrives and
   * hold the reference for their lifetime: handing out a placeholder that was later replaced would
   * leave every paint loop reading rings nothing writes to.
   */
  tracesFor(driverKey: string): FocusTraces {
    return this.ensureFocus(driverKey).traces;
  }

  /** Frames received for one driver — a dropped-frame readout for the debug corner. */
  framesReceivedFor(driverKey: string): number {
    return this.focus.get(driverKey)?.framesReceived ?? 0;
  }

  /**
   * States which drivers the connection is following, dropping the rest.
   *
   * Called by the connection whenever the set changes, and it is what makes dropping one half of a
   * comparison leave the other half alone: only the keys that actually went away lose their rings.
   */
  setFollowedDrivers(driverKeys: Iterable<string>): void {
    const followed = new Set(driverKeys);
    this.followedDriverKeys = followed;

    let announce = false;
    const remainingExtras = { ...this.extras };

    for (const driverKey of [...this.focus.keys()]) {
      if (!followed.has(driverKey)) {
        this.focus.delete(driverKey);
      }
    }

    for (const driverKey of Object.keys(remainingExtras)) {
      if (!followed.has(driverKey)) {
        delete remainingExtras[driverKey];
        this.extras = remainingExtras;
        announce = true;
      }
    }

    const readyKeys = [...this.focusReadyKeys].filter((driverKey) => followed.has(driverKey));
    if (readyKeys.length !== this.focusReadyKeys.size) {
      this.focusReadyKeys = new Set(readyKeys);
      announce = true;
    }

    // Extras and readiness are the only parts of the focus stream React can see, so losing either
    // is the only part of this that has to be announced — and only when something actually went,
    // or every subscription change would emit for nothing.
    if (announce) {
      this.emit();
    }
  }

  setConnected(connected: boolean): void {
    if (this.connected !== connected) {
      this.connected = connected;
      this.emit();
    }
  }

  /** Applies one message from the socket. */
  apply(message: LiveViewMessage): void {
    switch (message.type) {
      case 'roomList':
        this.rooms = message.rooms;
        this.emit();
        break;

      case 'sessionState':
        this.sessionState = message;
        this.emit();
        break;

      case 'towerSnapshot':
        this.tower = message;
        this.pushRaceSample(message);
        this.emit();
        break;

      case 'focusFrame':
        this.applyFocus(message);
        break;

      case 'lapHistory':
        this.lapHistories = { ...this.lapHistories, [message.driverKey]: message };
        this.summariseLaps(message);
        this.emit();
        break;

      case 'extrasFrame':
        // Roughly 1 Hz, so this one *does* go through React. The whole reason extras have their own
        // channel is that they change slowly enough for that to be free.
        this.applyExtras(message);
        break;

      case 'error':
        // A room that is gone is the other half of `interruptFocus`. A socket down longer than the
        // hub's room expiry reconnects and replays a `watchRoom` the hub can no longer honour, and
        // this is how it says so — at which point every driver key in flight stops meaning
        // anything, exactly as it does on a room switch. Reached from the opposite direction, so
        // the same clearing has to happen here.
        if (message.code === 'unknownRoom' || message.code === 'roomClosed') {
          this.resetFocus();
          this.resetLapHistory();
        }

        this.lastError = message;
        this.emit();
        break;
    }
  }

  /**
   * Records that every followed driver's stream was interrupted, without throwing the history away.
   *
   * The hub keeps a room alive for thirty seconds after its last frame so that a socket which drops
   * and returns inside that window rejoins the same room — `LiveHubOptions.RoomExpiry` says as much
   * in its own remarks. Clearing the rings on a disconnect discarded the client's half of that
   * bargain: a two-second hiccup mid-stint reconnected to the same room and restarted a
   * sixty-second pedal trace from empty, with nothing on screen to explain the blank.
   *
   * So the rings stay and the outage is written into them. `resetFocus` remains the right answer
   * for a room switch, where the driver keys genuinely stop meaning anything; this is the answer
   * for a gap in a stream that is about to resume.
   */
  interruptFocus(): void {
    for (const entry of this.focus.values()) {
      // The same discipline `applyFocus` follows for a pedal the simulator did not report: absent
      // is not zero. Resuming without this would bridge the outage with a straight line through
      // data that was never captured, which is the same lie in a different costume — and every
      // series sets `spanGaps: false` precisely so the hole stays a hole.
      entry.traces.throttle.push(Number.NaN);
      entry.traces.brake.push(Number.NaN);
      entry.traces.clutch.push(Number.NaN);
      entry.traces.steering.push(Number.NaN);
      entry.traces.speed.push(Number.NaN);
      entry.traces.gear.push(Number.NaN);
      entry.traces.engineRpm.push(Number.NaN);
      entry.traces.absActive.push(Number.NaN);
      entry.traces.tractionControlActive.push(Number.NaN);

      for (let wheel = 0; wheel < 4; wheel++) {
        entry.traces.tyres.pressureKpa[wheel]!.push(Number.NaN);
        entry.traces.tyres.wear[wheel]!.push(Number.NaN);
        entry.traces.tyres.temperatureCelsius[wheel]!.push(Number.NaN);

        for (const [ring] of EXTRAS_WHEEL_CHANNELS) {
          entry.traces.extras[ring][wheel]!.push(Number.NaN);
        }
      }

      for (const [ring] of EXTRAS_SCALAR_CHANNELS) {
        entry.traces.extras[ring].push(Number.NaN);
      }

      // Dropped rather than held. `LiveReadout` paints the last frame forever, so keeping it would
      // leave the speed sitting at 217 km/h through an outage — a held value presented as current.
      // Null falls back to the placeholder, which is the honest rendering.
      entry.frame = null;

      // Measured against the wrong side of the gap otherwise: an interval that elapsed during the
      // outage would swallow the first frame back instead of sampling it.
      entry.lastTyreSampleAtMs = Number.NEGATIVE_INFINITY;

      // Cleared for the same reason, from the other direction: the hub re-sends extras on re-focus,
      // and a repeated capture time would otherwise be mistaken for the duplicate this guards
      // against and dropped — leaving the first document back out of the rings entirely.
      entry.lastExtrasCapturedAtUtc = null;
    }

    // The followed set is deliberately left alone. Frames cannot arrive while the socket is down,
    // and the reconnect re-states the same subscription — clearing it here is what used to take the
    // rings with it.
    this.dropVisibleFocusState();
  }

  /** Clears every followed driver's traces — on leaving a room, or losing the connection. */
  resetFocus(): void {
    this.focus.clear();
    this.followedDriverKeys = new Set();
    this.fuelByLap.clear();

    // The race timeline goes with them. It is keyed by driver, and on a room switch the keys stop
    // naming anyone — a gap chart carrying the previous race's order into this one would be worse
    // than an empty chart, because nothing on it would look wrong.
    this.raceTraces.clear();
    this.lastRaceSampleAtMs = Number.NEGATIVE_INFINITY;

    this.dropVisibleFocusState();
  }

  /**
   * Forgets everything about the focus stream React can see, announcing it only if there was any.
   *
   * That is two things: the extras documents, and which drivers are holding a frame. Both are
   * dropped on a disconnect as well as on a room switch — an extras frame is a snapshot with an
   * age, and a damage panel held through an outage claims to describe a car it has not heard from,
   * while a driver whose stream has stopped is not one whose panel should still read as live. The
   * hub re-answers both on re-focus, so nothing is lost by asking again.
   *
   * Conditional, because an unconditional emit here fires on every reset and every disconnect for
   * nothing.
   */
  private dropVisibleFocusState(): void {
    if (Object.keys(this.extras).length === 0 && this.focusReadyKeys.size === 0) {
      return;
    }

    this.extras = {};
    this.focusReadyKeys = new Set();
    this.emit();
  }

  /** Forgets one driver's history, on collapsing their row. */
  dropLapHistory(driverKey: string): void {
    if (!(driverKey in this.lapHistories)) {
      return;
    }

    const next = { ...this.lapHistories };
    delete next[driverKey];
    this.lapHistories = next;

    const summaries = { ...this.lapSummaries };
    delete summaries[driverKey];
    this.lapSummaries = summaries;

    this.emit();
  }

  /** Forgets every driver's history — on leaving a room, where the keys stop meaning anything. */
  resetLapHistory(): void {
    if (
      Object.keys(this.lapHistories).length === 0 &&
      Object.keys(this.lapSummaries).length === 0
    ) {
      return;
    }

    this.lapHistories = {};
    this.lapSummaries = {};
    this.emit();
  }

  /** Dismisses the current error, so the banner can be closed. */
  clearError(): void {
    if (this.lastError !== null) {
      this.lastError = null;
      this.emit();
    }
  }

  private ensureFocus(driverKey: string): DriverFocus {
    let entry = this.focus.get(driverKey);
    if (entry === undefined) {
      entry = {
        frame: null,
        traces: newTraces(),
        framesReceived: 0,
        // Negative infinity rather than "now", so the first frame of a stint is sampled instead of
        // being swallowed by an interval that has not elapsed yet.
        lastTyreSampleAtMs: Number.NEGATIVE_INFINITY,
        lastExtrasCapturedAtUtc: null,
        currentLap: null,
        currentLapStartedAt: 0,
        lastFuelLeftLiters: Number.NaN,
        completedLaps: new Map(),
      };

      this.focus.set(driverKey, entry);
    }

    return entry;
  }

  private applyFocus(frame: FocusFrameMessage): void {
    // A frame for a driver we are no longer following can still be in flight when the subscription
    // changes. Dropping it here keeps one sample of a car nobody asked for off the screen — and,
    // more importantly, stops it creating a whole set of rings that never advance again.
    //
    // Dropped without emitting, because this runs on the focus path: a subscriber notified from
    // here would be notified at frame rate, which is the one thing this store exists to prevent.
    if (!this.followedDriverKeys.has(frame.driverKey)) {
      return;
    }

    const entry = this.ensureFocus(frame.driverKey);

    // Read before the assignment below overwrites it. Null means this is the first frame of a
    // subscription, or the first one back after an interruption — the one moment on this path
    // React has any business hearing about, and the reason the emit below is not a frame-rate emit.
    const wasHoldingAFrame = entry.frame !== null;

    entry.frame = frame;
    entry.framesReceived++;

    if (!wasHoldingAFrame) {
      this.focusReadyKeys = new Set(this.focusReadyKeys).add(frame.driverKey);
      this.emit();
    }

    // Pedals are absent on some simulators rather than zero, and a missing throttle must not be
    // plotted as a lifted one. NaN leaves a gap in the trace, which is the honest rendering.
    entry.traces.throttle.push(frame.throttle ?? Number.NaN);
    entry.traces.brake.push(frame.brake ?? Number.NaN);
    entry.traces.clutch.push(frame.clutch ?? Number.NaN);
    entry.traces.steering.push(frame.steering);
    entry.traces.speed.push(frame.speedMetersPerSecond);

    // Gear is nullable on the wire and neutral is a real gear, so an unreported gearbox has to be a
    // hole rather than a zero — the same discipline the pedals follow.
    entry.traces.gear.push(frame.gear ?? Number.NaN);
    entry.traces.engineRpm.push(frame.engineRpm);

    // Three-valued on purpose — see the remarks on `FocusTraces.absActive`. `?? null` first, so a
    // reported `false` becomes 0 and an absent one becomes NaN; `frame.absActive ? 1 : NaN` would
    // quietly turn "not intervening" into "not reported" and lose the baseline.
    entry.traces.absActive.push(assistState(frame.absActive));
    entry.traces.tractionControlActive.push(assistState(frame.tractionControlActive));

    this.pushTyreSample(entry, frame);
    this.pushLapSample(entry, frame);
  }

  /**
   * Adds one sample to the tyre rings, at most once per {@link TYRE_SAMPLE_INTERVAL_MS}.
   *
   * The same NaN discipline the pedals follow, and for the same reason: tyre arrays are nullable on
   * the wire, and a wheel the simulator did not report must leave a hole rather than being bridged
   * into a confident line — or, worse, drawn at zero, which for a pressure reads as a flat tyre.
   */
  private pushTyreSample(entry: DriverFocus, frame: FocusFrameMessage): void {
    const now = this.now();
    if (now - entry.lastTyreSampleAtMs < TYRE_SAMPLE_INTERVAL_MS) {
      return;
    }

    entry.lastTyreSampleAtMs = now;

    for (let wheel = 0; wheel < 4; wheel++) {
      entry.traces.tyres.pressureKpa[wheel]!.push(frame.tyrePressureKpa[wheel] ?? Number.NaN);
      entry.traces.tyres.wear[wheel]!.push(frame.tyreWear[wheel] ?? Number.NaN);
      // The middle of the tread is what the stint trace plots — one line per tyre, which is the
      // question "is this corner running away from its opposite number". The shoulders and the
      // window are on the frame too and are read straight off it by the widgets that want them: a
      // spread is drawn at one instant across the car, and a band does not move, so neither is
      // something a ring of samples over fifteen minutes would answer better.
      entry.traces.tyres.temperatureCelsius[wheel]!.push(
        frame.tyreTemperatureCelsius[wheel]?.middle ?? Number.NaN,
      );
    }
  }

  /**
   * Adds one sample to every driver's race rings, at most once per {@link RACE_SAMPLE_INTERVAL_MS}.
   *
   * Gap to leader is derived here, once, from the only gap the wire carries — the one to the car
   * ahead. Summed down the running order, so the leader is at zero and everyone else is however far
   * back the chain of gaps puts them. Deriving it in each widget instead would be the same sum
   * computed several times and, worse, several chances to disagree about it.
   *
   * A driver whose gap ahead is unreported breaks the chain: everyone behind them is then an unknown
   * distance back, so the sum goes NaN and stays NaN for the rest of the order. That is the honest
   * answer — a gap built on a missing link is a guess — and it draws as a hole.
   */
  private pushRaceSample(snapshot: TowerSnapshotMessage): void {
    const now = this.now();
    if (now - this.lastRaceSampleAtMs < RACE_SAMPLE_INTERVAL_MS) {
      return;
    }

    this.lastRaceSampleAtMs = now;

    // By position, because the running order is what the sum walks down. A row without a position
    // cannot be placed in that chain at all.
    const ordered = [...snapshot.drivers]
      .filter((row) => row.position !== null && row.position !== undefined)
      .sort((a, b) => a.position! - b.position!);

    let gapToLeader = 0;

    for (let index = 0; index < ordered.length; index++) {
      const row = ordered[index]!;
      const traces = this.raceTracesFor(row.driverKey);

      if (index > 0) {
        const gapAheadMs = row.gapToCarAheadMs;
        gapToLeader +=
          gapAheadMs === null || gapAheadMs === undefined ? Number.NaN : gapAheadMs / 1000;
      }

      traces.position.push(row.position!);
      traces.gapToLeaderSeconds.push(gapToLeader);
    }
  }

  /**
   * Turns one driver's lap history into the per-lap numbers the fuel and pace widgets read.
   *
   * Fuel per lap is a subtraction between consecutive lap-end readings, which is why it is derived
   * here rather than sampled: the wire reports what is in the tank, and what a race engineer asks is
   * what went out of it. The first lap seen has nothing to subtract from and reports null rather
   * than a full tank's worth of consumption.
   *
   * Lap history is a full snapshot per driver rather than a delta — `contracts.ts` is explicit — so
   * this recomputes from it rather than accumulating, and cannot drift out of step with it.
   */
  private summariseLaps(message: LapHistoryMessage): void {
    const fuelAtLapEnd = this.fuelByLap.get(message.driverKey);

    const summaries: LapSummary[] = message.laps.map((record) => {
      const fuelLeftLiters = fuelAtLapEnd?.get(record.lapNumber) ?? null;
      const previous = fuelAtLapEnd?.get(record.lapNumber - 1) ?? null;

      return {
        lapNumber: record.lapNumber,
        fuelLeftLiters,
        // Only where both ends are known. A refuelling stop makes this negative, which is correct:
        // the tank did gain fuel, and hiding it would leave a stint's consumption looking wrong.
        fuelUsedLiters:
          fuelLeftLiters !== null && previous !== null ? previous - fuelLeftLiters : null,
        lapTimeMs: record.lapTimeMs ?? null,
        ...(record.valid === null || record.valid === undefined ? {} : { valid: record.valid }),
      };
    });

    this.lapSummaries = { ...this.lapSummaries, [message.driverKey]: summaries };
  }

  /**
   * Records where the car is on its lap, closing the previous lap when the counter moves.
   *
   * The lap counter and the wrap of `trackPositionFraction` arrive in the same frame only by luck,
   * which is what `LapTrace.push` refuses a backwards fraction for. Here the counter is trusted
   * over the fraction: a lap ends when the simulator says it did.
   */
  private pushLapSample(entry: DriverFocus, frame: FocusFrameMessage): void {
    const fraction = frame.trackPositionFraction;

    if (entry.currentLap === null || entry.currentLap.lapNumber !== frame.lapNumber) {
      // Kept only if it was watched end to end. A partial lap is useless as a reference and
      // misleading as a comparison, and this is the one place that can tell.
      if (entry.currentLap !== null && entry.currentLap.isComplete()) {
        entry.completedLaps.set(entry.currentLap.lapNumber, entry.currentLap);
      }

      if (entry.currentLap !== null && Number.isFinite(entry.lastFuelLeftLiters)) {
        this.recordLapFuel(frame.driverKey, entry.currentLap.lapNumber, entry.lastFuelLeftLiters);
      }

      entry.currentLap = new LapTrace(frame.lapNumber);
      entry.currentLapStartedAt = frame.simulationTime;
    }

    entry.lastFuelLeftLiters = frame.fuelLeftLiters;

    if (fraction === null || fraction === undefined) {
      return;
    }

    entry.currentLap.push(fraction, frame.simulationTime - entry.currentLapStartedAt);
  }

  /**
   * Records what was left in the tank as one lap ended, and refreshes that driver's summaries.
   *
   * The refresh matters: lap history and focus frames arrive on different channels, so a lap's time
   * and its fuel almost never land together. Without re-summarising here, a lap's consumption would
   * stay null until the next history snapshot happened along.
   */
  private recordLapFuel(driverKey: string, lapNumber: number, fuelLeftLiters: number): void {
    let byLap = this.fuelByLap.get(driverKey);
    if (byLap === undefined) {
      byLap = new Map();
      this.fuelByLap.set(driverKey, byLap);
    }

    byLap.set(lapNumber, fuelLeftLiters);

    const history = this.lapHistories[driverKey];
    if (history !== undefined) {
      this.summariseLaps(history);
      this.emit();
    }
  }

  /** The lap being driven, for a delta chart to compare against the reference. */
  currentLapFor(driverKey: string): LapTrace | null {
    return this.focus.get(driverKey)?.currentLap ?? null;
  }

  /**
   * The lap a delta is measured against: this session's fastest clean, completely observed lap.
   *
   * Three rules, and the middle one is the one worth stating plainly. A lap is eligible when:
   *
   * - it was observed from the line to the line, so there are no holes to compare against;
   * - the simulator did not explicitly refuse it. **`valid === undefined` is unknown, not
   *   invalid** — `contracts.ts` says as much about personal bests, and the same holds here.
   *   Disqualifying every lap a simulator declined to comment on would leave most sessions with no
   *   reference at all, which is worse than measuring against a lap that might have had a wheel
   *   over a kerb;
   * - it has a recorded time to be fastest by.
   *
   * Pit laps and out laps need no special case. They are slow, so they never win.
   */
  referenceLapFor(driverKey: string): LapTrace | null {
    const entry = this.focus.get(driverKey);
    if (entry === undefined) {
      return null;
    }

    const laps = this.lapHistories[driverKey]?.laps ?? [];

    let best: LapTrace | null = null;
    let bestMs = Number.POSITIVE_INFINITY;

    for (const record of laps) {
      if (record.valid === false || record.lapTimeMs === null || record.lapTimeMs === undefined) {
        continue;
      }

      const trace = entry.completedLaps.get(record.lapNumber);
      if (trace === undefined || record.lapTimeMs >= bestMs) {
        continue;
      }

      best = trace;
      bestMs = record.lapTimeMs;
    }

    return best;
  }

  /**
   * Records one extras frame: decoded once, kept for React, and pushed into the extras rings.
   *
   * The decode is the point of this method. Panels used to each call `JSON.parse` on the same
   * string, so a wall showing brakes, tyre grip and car health decoded one document three times a
   * second to produce three identical objects — against a channel whose whole reason for existing
   * is that parsing it should be rare.
   *
   * Frames for a driver nobody follows are dropped, as focus frames are, and for the same reason:
   * admitting one leaves a car on screen that nothing will ever update again.
   */
  private applyExtras(message: ExtrasFrameMessage): void {
    if (!this.followedDriverKeys.has(message.driverKey)) {
      return;
    }

    const document = parseExtras(message.extras);
    this.extras = { ...this.extras, [message.driverKey]: { message, document } };

    this.pushExtrasSample(this.ensureFocus(message.driverKey), message, document);
    this.emit();
  }

  /**
   * Adds one sample to the extras rings, once per document.
   *
   * A malformed or missing document still advances the rings, writing NaN across every channel.
   * Skipping it instead would close the gap in the trace and draw a line through the outage, which
   * is the same lie `spanGaps: false` is set everywhere to prevent — the reading genuinely was not
   * taken, and the chart should say so.
   */
  private pushExtrasSample(
    entry: DriverFocus,
    message: ExtrasFrameMessage,
    document: RaceRoomExtras | null,
  ): void {
    if (entry.lastExtrasCapturedAtUtc === message.capturedAtUtc) {
      return;
    }

    entry.lastExtrasCapturedAtUtc = message.capturedAtUtc;

    for (const [ring, read] of EXTRAS_WHEEL_CHANNELS) {
      for (let wheel = 0; wheel < 4; wheel++) {
        entry.traces.extras[ring][wheel]!.push(
          reportedOrNaN(document === null ? undefined : read(document, wheel)),
        );
      }
    }

    for (const [ring, field] of EXTRAS_SCALAR_CHANNELS) {
      entry.traces.extras[ring].push(reportedOrNaN(document?.[field]));
    }
  }

  private emit(): void {
    for (const listener of this.listeners) {
      listener();
    }
  }
}
