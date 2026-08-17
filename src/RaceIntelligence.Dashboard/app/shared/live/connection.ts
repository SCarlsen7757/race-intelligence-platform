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
  private focusedDriverKey: string | null = null;

  // A set, not a single key: several tower rows can be expanded at once, and each one is its own
  // subscription the hub conflates separately.
  private readonly lapHistoryDriverKeys = new Set<string>();

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
    this.focusedDriverKey = null;
    this.lapHistoryDriverKeys.clear();
    this.store.resetFocus();
    this.store.resetLapHistory();

    this.send({ type: 'watchRoom', roomId });
  }

  /** Follows one driver at full rate, or stops following with `null`. */
  focusDriver(driverKey: string | null): void {
    if (this.focusedDriverKey === driverKey) {
      return;
    }

    this.focusedDriverKey = driverKey;
    this.store.resetFocus();
    this.send({ type: 'focusDriver', driverKey });
  }

  /** Asks for one driver's completed laps, and keeps receiving them until told otherwise. */
  subscribeLapHistory(driverKey: string): void {
    if (this.lapHistoryDriverKeys.has(driverKey)) {
      return;
    }

    this.lapHistoryDriverKeys.add(driverKey);
    this.send({ type: 'subscribeLapHistory', driverKey });
  }

  /**
   * Stops one driver's lap history.
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
    if (!this.lapHistoryDriverKeys.delete(driverKey)) {
      return;
    }

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

        if (this.focusedDriverKey !== null) {
          this.send({ type: 'focusDriver', driverKey: this.focusedDriverKey });
        }

        for (const driverKey of this.lapHistoryDriverKeys) {
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
      this.store.resetFocus();
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
    // watchedRoomId/focusedDriverKey/lapHistoryDriverKeys and replayed on reconnect, so queueing
    // the command as well would send it twice.
    if (this.socket?.readyState === WebSocket.OPEN) {
      this.socket.send(JSON.stringify(command));
    }
  }
}
