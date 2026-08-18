import type {
  ExtrasFrameMessage,
  FocusFrameMessage,
  LapHistoryMessage,
  LiveErrorMessage,
  LiveRoomSummary,
  LiveViewMessage,
  SessionStateMessage,
  TowerSnapshotMessage,
} from './contracts';

/**
 * How many focus frames the trace buffers keep.
 *
 * At 60 Hz this is a hair over sixty seconds — long enough to see the whole braking, turn-in and
 * exit of any corner, and to compare the last few. Ring buffers, so memory is flat no matter how
 * long a session runs; a growing array would be a slow leak over a two-hour race.
 */
export const TRACE_CAPACITY = 3600;

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
}

/**
 * How many decimated samples the tyre traces keep, and how far apart they are taken.
 *
 * A tyre asks a different question from a pedal. Throttle and brake are read a corner at a time, so
 * a minute of samples at full rate is the right window; pressure, wear and temperature move over a
 * stint, and sixty seconds of them is a flat line that says nothing. Sampling once a second and
 * keeping the same number of slots turns the same memory into an hour — long enough that a stint's
 * whole shape is on screen at once.
 *
 * Decimated by elapsed time rather than by counting frames, because the collector's poll rate is
 * not a constant: a machine under load reports fewer frames per second, and a fixed "every 60th"
 * would silently stretch the window whenever the game got busy.
 */
export const TYRE_TRACE_CAPACITY = 3600;
export const TYRE_SAMPLE_INTERVAL_MS = 1000;

/** One per-wheel channel over time, in the wire's wheel order — FL, FR, RL, RR. */
export type WheelTraces = readonly [TraceBuffer, TraceBuffer, TraceBuffer, TraceBuffer];

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

/** The traces the focus panel plots for one driver. */
export interface FocusTraces {
  /** Input channels, one sample per focus frame, all sharing one sample index. */
  throttle: TraceBuffer;
  brake: TraceBuffer;
  clutch: TraceBuffer;
  steering: TraceBuffer;
  speed: TraceBuffer;
  /** Tyre channels, on their own slower sample index. */
  tyres: TyreTraces;
}

function wheelTraces(): WheelTraces {
  return [
    new TraceBuffer(TYRE_TRACE_CAPACITY),
    new TraceBuffer(TYRE_TRACE_CAPACITY),
    new TraceBuffer(TYRE_TRACE_CAPACITY),
    new TraceBuffer(TYRE_TRACE_CAPACITY),
  ];
}

function newTraces(): FocusTraces {
  return {
    throttle: new TraceBuffer(),
    brake: new TraceBuffer(),
    clutch: new TraceBuffer(),
    steering: new TraceBuffer(),
    speed: new TraceBuffer(),
    tyres: {
      pressureKpa: wheelTraces(),
      wear: wheelTraces(),
      temperatureCelsius: wheelTraces(),
    },
  };
}

/** Everything held for one followed driver. Plain fields — none of this goes through React. */
interface DriverFocus {
  frame: FocusFrameMessage | null;
  traces: FocusTraces;
  framesReceived: number;
  /** When the tyre rings last took a sample, so the decimation is by time and not by frame count. */
  lastTyreSampleAtMs: number;
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
  private extras: Readonly<Record<string, ExtrasFrameMessage>> = {};

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
  getExtras = (): Readonly<Record<string, ExtrasFrameMessage>> => this.extras;
  isConnected = (): boolean => this.connected;
  getFocusReadyKeys = (): ReadonlySet<string> => this.focusReadyKeys;

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
        this.emit();
        break;

      case 'focusFrame':
        this.applyFocus(message);
        break;

      case 'lapHistory':
        this.lapHistories = { ...this.lapHistories, [message.driverKey]: message };
        this.emit();
        break;

      case 'extrasFrame':
        // Roughly 1 Hz, so this one *does* go through React. The whole reason extras have their own
        // channel is that they change slowly enough for that to be free.
        this.extras = { ...this.extras, [message.driverKey]: message };
        this.emit();
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

      for (let wheel = 0; wheel < 4; wheel++) {
        entry.traces.tyres.pressureKpa[wheel]!.push(Number.NaN);
        entry.traces.tyres.wear[wheel]!.push(Number.NaN);
        entry.traces.tyres.temperatureCelsius[wheel]!.push(Number.NaN);
      }

      // Dropped rather than held. `LiveReadout` paints the last frame forever, so keeping it would
      // leave the speed sitting at 217 km/h through an outage — a held value presented as current.
      // Null falls back to the placeholder, which is the honest rendering.
      entry.frame = null;

      // Measured against the wrong side of the gap otherwise: an interval that elapsed during the
      // outage would swallow the first frame back instead of sampling it.
      entry.lastTyreSampleAtMs = Number.NEGATIVE_INFINITY;
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
    this.emit();
  }

  /** Forgets every driver's history — on leaving a room, where the keys stop meaning anything. */
  resetLapHistory(): void {
    if (Object.keys(this.lapHistories).length === 0) {
      return;
    }

    this.lapHistories = {};
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

    this.pushTyreSample(entry, frame);
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
      entry.traces.tyres.temperatureCelsius[wheel]!.push(
        frame.tyreTemperatureCelsius[wheel] ?? Number.NaN,
      );
    }
  }

  private emit(): void {
    for (const listener of this.listeners) {
      listener();
    }
  }
}
