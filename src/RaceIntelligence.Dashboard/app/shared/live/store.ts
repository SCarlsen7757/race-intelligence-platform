import type {
  ExtrasFrameMessage,
  FocusFrameMessage,
  LapHistoryMessage,
  LiveErrorMessage,
  LiveRoomSummary,
  LiveViewMessage,
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

  constructor(capacity: number = TRACE_CAPACITY) {
    this.values = new Float32Array(capacity);
  }

  get length(): number {
    return this.filled;
  }

  push(value: number): void {
    this.values[this.writeIndex] = value;
    this.writeIndex = (this.writeIndex + 1) % this.values.length;
    if (this.filled < this.values.length) {
      this.filled++;
    }
  }

  clear(): void {
    this.writeIndex = 0;
    this.filled = 0;
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

/** The traces the focus panel plots, all sharing one sample index. */
export interface FocusTraces {
  throttle: TraceBuffer;
  brake: TraceBuffer;
  clutch: TraceBuffer;
  steering: TraceBuffer;
  speed: TraceBuffer;
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
  /** Latest focus frame. Read by the paint loop, never by a component's render. */
  focusFrame: FocusFrameMessage | null = null;

  /** Rolling traces for the focused driver. */
  readonly traces: FocusTraces = {
    throttle: new TraceBuffer(),
    brake: new TraceBuffer(),
    clutch: new TraceBuffer(),
    steering: new TraceBuffer(),
    speed: new TraceBuffer(),
  };

  /** Frames received since the last paint — a dropped-frame readout for the debug corner. */
  focusFramesReceived = 0;

  private rooms: LiveRoomSummary[] = [];
  private tower: TowerSnapshotMessage | null = null;
  private lastError: LiveErrorMessage | null = null;
  private connected = false;

  // Keyed by driver so several expanded rows can be held at once. Replaced rather than mutated:
  // useSyncExternalStore compares snapshots by identity, and a mutated map would never look
  // changed.
  private lapHistories: Readonly<Record<string, LapHistoryMessage>> = {};

  private extras: ExtrasFrameMessage | null = null;

  private readonly listeners = new Set<() => void>();

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
  getLastError = (): LiveErrorMessage | null => this.lastError;
  getLapHistories = (): Readonly<Record<string, LapHistoryMessage>> => this.lapHistories;
  getExtras = (): ExtrasFrameMessage | null => this.extras;
  isConnected = (): boolean => this.connected;

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
        this.extras = message;
        this.emit();
        break;

      case 'error':
        this.lastError = message;
        this.emit();
        break;
    }
  }

  /** Clears the focused driver's traces — on switching driver, room, or losing the connection. */
  resetFocus(): void {
    const hadExtras = this.extras !== null;
    this.clearFocusState();

    // Extras are the one part of the focus stream React can see, so dropping them is the one part
    // of a reset that has to be announced. Only when there were any: an unconditional emit here
    // would fire on every `focusDriver` call for nothing.
    if (hadExtras) {
      this.emit();
    }
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

  private clearFocusState(): void {
    this.focusFrame = null;
    this.focusFramesReceived = 0;
    this.extras = null;
    this.traces.throttle.clear();
    this.traces.brake.clear();
    this.traces.clutch.clear();
    this.traces.steering.clear();
    this.traces.speed.clear();
  }

  private applyFocus(frame: FocusFrameMessage): void {
    // A frame for a driver we are no longer following can still be in flight when the subscription
    // changes. Dropping it here keeps one sample of the previous car out of the new car's traces,
    // where it would read as a glitch rather than as the switch it is.
    //
    // Cleared without emitting, because this runs on the focus path: a subscriber notified from
    // here would be notified at frame rate, which is the one thing this store exists to prevent.
    // Nothing is lost — the reset that accompanies a deliberate driver change already emitted.
    if (this.focusFrame !== null && this.focusFrame.driverKey !== frame.driverKey) {
      this.clearFocusState();
    }

    this.focusFrame = frame;
    this.focusFramesReceived++;

    // Pedals are absent on some simulators rather than zero, and a missing throttle must not be
    // plotted as a lifted one. NaN leaves a gap in the trace, which is the honest rendering.
    this.traces.throttle.push(frame.throttle ?? Number.NaN);
    this.traces.brake.push(frame.brake ?? Number.NaN);
    this.traces.clutch.push(frame.clutch ?? Number.NaN);
    this.traces.steering.push(frame.steering);
    this.traces.speed.push(frame.speedMetersPerSecond);
  }

  private emit(): void {
    for (const listener of this.listeners) {
      listener();
    }
  }
}
