import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LiveConnection } from './connection';
import type { FocusFrameMessage, LiveViewCommand, LiveViewMessage } from './contracts';
import { LiveStore } from './store';

/**
 * A scriptable stand-in for the browser's WebSocket, as `app.test.tsx` uses.
 *
 * Duplicated rather than shared because the two tests want different things from it: that one
 * drives the whole app through the router, this one holds a connection and a store side by side so
 * it can ask what a dropped socket did to the rings. Extracting a common fake would couple an
 * integration test to a unit test for four lines of code.
 */
class FakeWebSocket {
  static readonly OPEN = 1;
  static instances: FakeWebSocket[] = [];

  readyState = FakeWebSocket.OPEN;
  sent: LiveViewCommand[] = [];

  onopen: (() => void) | null = null;
  onmessage: ((event: MessageEvent<string>) => void) | null = null;
  onclose: (() => void) | null = null;
  onerror: (() => void) | null = null;

  constructor(public readonly url: string) {
    FakeWebSocket.instances.push(this);
  }

  send(data: string): void {
    this.sent.push(JSON.parse(data) as LiveViewCommand);
  }

  close(): void {
    this.readyState = 3;
    this.onclose?.();
  }

  deliver(message: LiveViewMessage): void {
    this.onmessage?.(new MessageEvent('message', { data: JSON.stringify(message) }));
  }
}

function socket(): FakeWebSocket {
  const instance = FakeWebSocket.instances.at(-1);
  if (instance === undefined) {
    throw new Error('No socket was opened.');
  }

  return instance;
}

/**
 * One tyre's temperatures, with a plausible window around the given middle-of-tread reading.
 *
 * Shoulders either side of the middle rather than three identical numbers, so a test that started
 * reading the wrong one would fail rather than pass by coincidence.
 */

function focusFrame(overrides: Partial<FocusFrameMessage> = {}): FocusFrameMessage {
  return {
    type: 'focusFrame',
    roomId: 'room-1',
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
    brakePressureKiloNewtons: [],
    ...overrides,
  };
}

/** A connection already watching a room and following one driver, with a frame in its rings. */
function watching(): { store: LiveStore; connection: LiveConnection } {
  const store = new LiveStore();
  const connection = new LiveConnection(store);

  connection.connect();
  socket().onopen?.();
  connection.watchRoom('room-1');
  connection.focusDrivers(['id:2']);
  socket().deliver(focusFrame({ throttle: 1 }));

  return { store, connection };
}

describe('LiveConnection', () => {
  beforeEach(() => {
    FakeWebSocket.instances = [];
    vi.useFakeTimers();
    vi.stubGlobal('WebSocket', FakeWebSocket);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  /**
   * The bug this file exists to pin. `socket.onclose` used to call `resetFocus`, so a two-second
   * hiccup mid-stint reconnected into the same room and restarted a sixty-second pedal trace from
   * empty — discarding the client's half of the bargain the hub's room expiry exists to keep.
   */
  it('keeps the traces when the socket drops and comes back to the same room', () => {
    const { store, connection } = watching();

    socket().close();
    vi.advanceTimersByTime(1000);
    socket().onopen?.();
    socket().deliver(focusFrame({ throttle: 0.25 }));

    // The frame before the drop, the gap it left, and the frame after it.
    expect([...store.tracesFor('id:2').throttle.toArray()]).toEqual([1, Number.NaN, 0.25]);

    connection.dispose();
  });

  it('replays the room and the focus once the socket is back', () => {
    const { connection } = watching();

    socket().close();
    vi.advanceTimersByTime(1000);
    socket().onopen?.();

    expect(socket().sent).toContainEqual({ type: 'watchRoom', roomId: 'room-1' });
    expect(socket().sent).toContainEqual({ type: 'focusDriver', driverKey: 'id:2' });

    connection.dispose();
  });

  /** A driver key only means something inside a room, so the one place that clears is still right. */
  it('clears the traces on a room switch', () => {
    const { store, connection } = watching();

    connection.watchRoom('room-2');

    expect(store.tracesFor('id:2').throttle.length).toBe(0);

    connection.dispose();
  });

  /**
   * Down longer than the hub's thirty-second room expiry and the room is genuinely gone. The
   * replayed `watchRoom` is refused, and that refusal has to cost the rings what the disconnect
   * deliberately did not.
   */
  it('clears the traces when the reconnect finds the room gone', () => {
    const { store, connection } = watching();

    socket().close();
    vi.advanceTimersByTime(1000);
    socket().onopen?.();
    socket().deliver({ type: 'error', code: 'unknownRoom', message: 'No such room.' });

    expect(store.tracesFor('id:2').throttle.length).toBe(0);

    connection.dispose();
  });

  /**
   * The room-wide lap feed and an expanded tower row can both want the same driver's lap history
   * at once — two independent owners, where the old plain-Set bookkeeping only ever expected one.
   * Refcounted so neither owner's unsubscribe can silence a subscription the other still needs.
   */
  describe('lap history refcounting', () => {
    it('sends subscribeLapHistory only once when two owners subscribe the same driver', () => {
      const { connection } = watching();

      connection.subscribeLapHistory('id:3');
      connection.subscribeLapHistory('id:3');

      expect(
        socket().sent.filter(
          (command) => command.type === 'subscribeLapHistory' && command.driverKey === 'id:3',
        ),
      ).toHaveLength(1);

      connection.dispose();
    });

    it('does not unsubscribe while another owner still wants the same driver', () => {
      const { store, connection } = watching();
      const dropped = vi.spyOn(store, 'dropLapHistory');

      connection.subscribeLapHistory('id:3');
      connection.subscribeLapHistory('id:3');
      connection.unsubscribeLapHistory('id:3');

      expect(
        socket().sent.filter(
          (command) => command.type === 'unsubscribeLapHistory' && command.driverKey === 'id:3',
        ),
      ).toHaveLength(0);
      expect(dropped).not.toHaveBeenCalled();

      connection.dispose();
    });

    it('unsubscribes once every owner has let go', () => {
      const { store, connection } = watching();
      const dropped = vi.spyOn(store, 'dropLapHistory');

      connection.subscribeLapHistory('id:3');
      connection.subscribeLapHistory('id:3');
      connection.unsubscribeLapHistory('id:3');
      connection.unsubscribeLapHistory('id:3');

      expect(
        socket().sent.filter(
          (command) => command.type === 'unsubscribeLapHistory' && command.driverKey === 'id:3',
        ),
      ).toHaveLength(1);
      expect(dropped).toHaveBeenCalledWith('id:3');

      connection.dispose();
    });
  });
});
