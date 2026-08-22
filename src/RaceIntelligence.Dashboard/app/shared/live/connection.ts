import type { LiveViewCommand, LiveViewMessage } from './contracts';
import { hubSocketUrl } from './hubUrl';
import type { LiveStore } from './store';

/** Where the hub's viewing socket lives. Must match `LiveEndpoints.ViewPath`. */
const VIEW_PATH = '/live/view';

const FIRST_RECONNECT_DELAY_MS = 500;
const MAX_RECONNECT_DELAY_MS = 10_000;

/**
 * The dashboard's connection to the hub.
 *
 * Reconnects on its own, with capped exponential backoff, and re-sends whatever the viewer was
 * watching once it is back. That last part is what makes a dropped connection invisible: the hub
 * keeps a room alive for thirty seconds after its last frame, so a socket that drops and returns
 * inside that window resumes the same room rather than sending the user back to the list.
 *
 * The socket is opened against the **hub's** origin, not the page's. The dashboard is served by a
 * Node process on its own origin now, so there is nothing to infer from `window.location` — see
 * `hubUrl.ts`.
 */
export class LiveConnection {
  private socket: WebSocket | null = null;
  private reconnectDelay = FIRST_RECONNECT_DELAY_MS;
  private reconnectTimer: number | null = null;
  private closed = false;

  private watchedRoomId: string | null = null;

  // A set, not a single key: several drivers can be compared side by side, and the hub conflates
  // each one's 60 Hz stream separately.
  private focusedDriverKeys: ReadonlySet<string> = new Set();

  // A refcount, not a set: several tower rows can be expanded at once, and the room-wide lap feed
  // subscribes the same drivers independently of any row being expanded. Two unrelated owners can
  // therefore want the same driverKey at once, so an unsubscribe from one must not silence the
  // other — the wire command is only sent on the true 0→1 and 1→0 edges.
  private readonly lapHistoryDriverKeys = new Map<string, number>();

  constructor(private readonly store: LiveStore) {}

  connect(): void {
    this.closed = false;
    this.open();
  }

  /** Closes for good. Cancels any pending reconnect rather than letting it fire after teardown. */
  dispose(): void {
    this.closed = true;

    if (this.reconnectTimer !== null) {
      window.clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }

    this.socket?.close();
    this.socket = null;
  }

  /** Subscribes to a room's timing tower, or leaves the current one with `null`. */
  watchRoom(roomId: string | null): void {
    if (this.watchedRoomId === roomId) {
      return;
    }

    this.watchedRoomId = roomId;

    // Mirrors what the hub does on its side: a driver key is only meaningful within a room, so a
    // focus cannot survive a room switch. Clearing it here as well keeps the reconnect replay
    // below from re-sending a focus that belongs to the room we just left. The same argument
    // applies to every open lap-history subscription.
    this.focusedDriverKeys = new Set();
    this.lapHistoryDriverKeys.clear();
    this.store.resetFocus();
    this.store.resetLapHistory();

    this.send({ type: 'watchRoom', roomId });
  }

  /**
   * Follows exactly these drivers at full rate, adding and dropping to match.
   *
   * Stated as the whole set rather than one driver at a time because that is what the URL says — the
   * caller knows which cars should be on screen, not which changed. The diff is what keeps the
   * promise that matters: dropping one half of a comparison sends a single `unfocusDriver` and
   * leaves the other half's subscription, and therefore its rings, completely untouched.
   */
  focusDrivers(driverKeys: readonly string[]): void {
    const next = new Set(driverKeys);
    const added = [...next].filter((key) => !this.focusedDriverKeys.has(key));
    const removed = [...this.focusedDriverKeys].filter((key) => !next.has(key));

    if (added.length === 0 && removed.length === 0) {
      return;
    }

    this.focusedDriverKeys = next;

    // Before the commands, so a frame already in flight for a dropped driver is refused rather than
    // landing in rings the panel has stopped reading.
    this.store.setFollowedDrivers(next);

    for (const driverKey of removed) {
      this.send({ type: 'unfocusDriver', driverKey });
    }

    for (const driverKey of added) {
      this.send({ type: 'focusDriver', driverKey });
    }
  }

  /**
   * Asks for one driver's completed laps, and keeps receiving them until every caller that asked
   * has let go. Safe to call more than once for the same driver from independent owners — only the
   * first caller actually sends the wire command.
   */
  subscribeLapHistory(driverKey: string): void {
    const count = this.lapHistoryDriverKeys.get(driverKey) ?? 0;
    this.lapHistoryDriverKeys.set(driverKey, count + 1);

    if (count === 0) {
      this.send({ type: 'subscribeLapHistory', driverKey });
    }
  }

  /**
   * Lets go of one driver's lap history. Only sent to the hub, and only dropped from the store,
   * once every caller that subscribed has unsubscribed — a collapsed row while the lap feed still
   * wants the same driver must not silence it, and vice versa.
   *
   * Sent rather than merely forgotten locally: a collapsed row that kept receiving history would
   * have the hub building and queueing a snapshot per lap for something nobody is looking at.
   *
   * Names the driver rather than clearing and re-stating the rest. Subscriptions are a set, so
   * `subscribeLapHistory: null` drops all of them — emulating a single unsubscribe that way leaves
   * a window in which the other rows are unsubscribed, and a lap completed inside that window is
   * simply missed.
   */
  unsubscribeLapHistory(driverKey: string): void {
    const count = this.lapHistoryDriverKeys.get(driverKey);
    if (count === undefined) {
      return;
    }

    if (count > 1) {
      this.lapHistoryDriverKeys.set(driverKey, count - 1);
      return;
    }

    this.lapHistoryDriverKeys.delete(driverKey);
    this.send({ type: 'unsubscribeLapHistory', driverKey });
    this.store.dropLapHistory(driverKey);
  }

  private open(): void {
    const socket = new WebSocket(hubSocketUrl(VIEW_PATH));
    this.socket = socket;

    socket.onopen = () => {
      this.reconnectDelay = FIRST_RECONNECT_DELAY_MS;
      this.store.setConnected(true);

      // Re-state the subscription. The hub holds no memory of a viewer across connections — it
      // deliberately keeps no per-viewer identity — so a reconnect that said nothing would receive
      // only the room list and leave a tower frozen on screen looking live.
      if (this.watchedRoomId !== null) {
        this.send({ type: 'watchRoom', roomId: this.watchedRoomId });

        // Re-stated rather than restored: the drop leaves the followed set intact, so this is a
        // no-op in the ordinary case and only matters when the set changed while the socket was
        // down. Stated unconditionally because the replay has to be idempotent — the alternative
        // is tracking whether it drifted, for an assignment that costs nothing.
        this.store.setFollowedDrivers(this.focusedDriverKeys);

        for (const driverKey of this.focusedDriverKeys) {
          this.send({ type: 'focusDriver', driverKey });
        }

        for (const driverKey of this.lapHistoryDriverKeys.keys()) {
          this.send({ type: 'subscribeLapHistory', driverKey });
        }
      }
    };

    socket.onmessage = (event: MessageEvent<string>) => {
      let message: LiveViewMessage;
      try {
        message = JSON.parse(event.data) as LiveViewMessage;
      } catch {
        // A frame the hub could not have sent. Dropping it beats tearing down a working
        // connection over one unparseable message.
        return;
      }

      this.store.apply(message);
    };

    socket.onclose = () => {
      this.store.setConnected(false);

      // Interrupted, not reset. The hub holds the room for thirty seconds after its last frame so
      // this socket can come back to it, and throwing the rings away here would mean a two-second
      // hiccup costs a minute of pedal trace. The gap is recorded instead, and drawn as a gap.
      this.store.interruptFocus();

      this.scheduleReconnect();
    };

    // A failed connection fires error then close; the reconnect is scheduled from close alone so
    // it is not scheduled twice.
    socket.onerror = () => socket.close();
  }

  private scheduleReconnect(): void {
    if (this.closed || this.reconnectTimer !== null) {
      return;
    }

    this.reconnectTimer = window.setTimeout(() => {
      this.reconnectTimer = null;
      if (!this.closed) {
        this.open();
      }
    }, this.reconnectDelay);

    this.reconnectDelay = Math.min(this.reconnectDelay * 2, MAX_RECONNECT_DELAY_MS);
  }

  private send(command: LiveViewCommand): void {
    // Silently dropped while disconnected, deliberately. The intent is already recorded in
    // watchedRoomId/focusedDriverKeys/lapHistoryDriverKeys and replayed on reconnect, so queueing
    // the command as well would send it twice.
    if (this.socket?.readyState === WebSocket.OPEN) {
      this.socket.send(JSON.stringify(command));
    }
  }
}
