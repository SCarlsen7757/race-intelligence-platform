import { act, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  LiveViewCommand,
  LiveViewMessage,
  TowerSnapshotMessage,
} from './shared/live/contracts';
import { renderApp } from './testing/renderApp';

/**
 * A stand-in for the browser's WebSocket that a test can push messages into and read commands out
 * of.
 *
 * jsdom has no WebSocket, so something has to fill the gap regardless. Making it scriptable turns
 * that necessity into the one test that covers the whole frontend path — router, provider, socket,
 * store, and every screen — against exactly the message shapes the hub's own contract tests pin.
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
    // Asynchronous, as a real socket is: the app must not depend on being open synchronously.
    queueMicrotask(() => this.onopen?.());
  }

  send(data: string): void {
    this.sent.push(JSON.parse(data) as LiveViewCommand);
  }

  close(): void {
    this.readyState = 3;
    this.onclose?.();
  }

  /** Delivers a message from the hub. */
  deliver(message: LiveViewMessage): void {
    this.onmessage?.(new MessageEvent('message', { data: JSON.stringify(message) }));
  }
}

const roomList: LiveViewMessage = {
  type: 'roomList',
  rooms: [
    {
      roomId: 'room-1',
      gameKey: 'raceroom',
      trackName: 'Spa',
      layoutName: 'Grand Prix',
      sessionType: 2,
      driverCount: 2,
      publishers: [
        {
          clientId: 'client-1',
          clientName: 'Gaming PC',
          driverName: 'Mark',
          simDriverId: '4242',
          connectedAtUtc: new Date().toISOString(),
          capabilities: ['TyreWear', 'TyrePressure', 'TyreTemperature', 'BrakeTemperature'],
        },
      ],
      lastUpdatedAtUtc: new Date().toISOString(),
    },
  ],
};

const tower: TowerSnapshotMessage = {
  type: 'towerSnapshot',
  roomId: 'room-1',
  capturedAtUtc: new Date().toISOString(),
  drivers: [
    {
      driverKey: 'id:4242',
      displayName: 'Mark Carlsen',
      position: 1,
      completedLaps: 3,
      previousLapMs: 102_500,
      bestLapMs: 102_000,
      currentSectorMs: [],
      previousSectorMs: [],
      bestSectorMs: [],
      pitLaneState: -1,
      pitStopStatus: -1,
      finishStatus: 0,
      tier: 'Self',
    },
    {
      driverKey: 'id:9',
      displayName: 'Rival',
      position: 2,
      completedLaps: 3,
      currentSectorMs: [],
      previousSectorMs: [],
      bestSectorMs: [],
      pitLaneState: -1,
      pitStopStatus: -1,
      finishStatus: 0,
      tier: 'Observed',
    },
  ],
};

/**
 * The same session with a collector on both cars, so there is something to compare.
 *
 * Only a `Self`-tier driver has full-rate channels at all, which is exactly why the single-collector
 * tower above cannot produce a comparison.
 */
const twoCollectorTower: TowerSnapshotMessage = {
  ...tower,
  drivers: tower.drivers.map((driver) => ({ ...driver, tier: 'Self' as const })),
};

function socket(): FakeWebSocket {
  const instance = FakeWebSocket.instances.at(-1);
  if (instance === undefined) {
    throw new Error('The app never opened a socket.');
  }

  return instance;
}

/** Every command sent across every socket this test opened, in order. */
function allSent(): LiveViewCommand[] {
  return FakeWebSocket.instances.flatMap((instance) => instance.sent);
}

describe('the dashboard', () => {
  beforeEach(() => {
    FakeWebSocket.instances = [];
    vi.stubGlobal('WebSocket', FakeWebSocket);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  /**
   * The two-origin change in one assertion. The dashboard is served by a Node process now, so a
   * socket opened against the page's own origin would reach the dev server, not the hub — and
   * would fail in a way that looks like the hub being down.
   */
  it('opens its socket against the configured hub, not the page it was served from', async () => {
    await renderApp();
    await act(async () => {});

    expect(socket().url).toBe('ws://localhost:5044/live/view');
    expect(socket().url).not.toContain(window.location.host);
  });

  it('shows the sessions the hub reports', async () => {
    await renderApp();
    await act(async () => {
      socket().deliver(roomList);
    });

    expect(screen.getByText('Spa')).toBeDefined();
  });

  /** The whole first slice from the browser's side: pick a session, see the field. */
  it('watches a session and renders its timing tower', async () => {
    const app = await renderApp();
    await act(async () => {
      socket().deliver(roomList);
    });

    await act(async () => {
      screen.getByRole('link', { name: /Spa/ }).click();
    });

    expect(app.currentPath()).toBe('/rooms/room-1');
    expect(allSent()).toContainEqual({ type: 'watchRoom', roomId: 'room-1' });

    await act(async () => {
      socket().deliver(tower);
    });

    expect(screen.getByText('Mark Carlsen')).toBeDefined();
    expect(screen.getByText('Rival')).toBeDefined();
    expect(screen.getByText('1:42.500')).toBeDefined();
  });

  it('subscribes to a driver whose own machine is publishing', async () => {
    await renderApp('/rooms/room-1');
    await act(async () => {
      socket().deliver(roomList);
      socket().deliver(tower);
    });

    await act(async () => {
      screen.getByRole('button', { name: 'Open telemetry for Mark Carlsen' }).click();
    });

    expect(allSent()).toContainEqual({ type: 'focusDriver', driverKey: 'id:4242' });

    // Opening a car opens the wall, headed by that car. There is no fixed per-driver region left
    // on the page: what the car is doing, what the driver is doing and what it is set to are all
    // widgets now, so a heading that reappeared here would be the regression this removed.
    expect(screen.getByRole('heading', { name: 'Mark Carlsen' })).toBeDefined();

    // What the collector's capabilities allow is what the wall offers to add.
    await act(async () => {
      screen.getByRole('button', { name: '+ Add widget' }).click();
    });

    expect(screen.getByText('Tyre pressure')).toBeDefined();
    expect(screen.getByText('Brake temperature')).toBeDefined();
    expect(screen.queryByText('Damage')).toBeNull();
  });

  /**
   * The room still comes from the URL; the cars no longer do. A refresh lands back on the same
   * session, and which cars are being watched is state belonging to that room — the wall beside it
   * is what carries an arrangement between sessions.
   */
  it('restores the session from the URL, and starts with no car open', async () => {
    await renderApp('/rooms/room-1');
    await act(async () => {
      socket().deliver(roomList);
      socket().deliver(tower);
    });

    expect(allSent()).toContainEqual({ type: 'watchRoom', roomId: 'room-1' });
    expect(allSent()).not.toContainEqual({ type: 'focusDriver', driverKey: 'id:4242' });

    // With nobody open the tower is the whole interface, so there is no wall beside it.
    expect(screen.queryByRole('region', { name: 'Pit wall' })).toBeNull();

    await act(async () => {
      screen.getByRole('button', { name: 'Open telemetry for Mark Carlsen' }).click();
    });

    // Named from the tower rather than from the key, so the wall opens on a person.
    expect(screen.getByRole('region', { name: 'Pit wall' })).toBeDefined();
    expect(screen.getByRole('heading', { name: 'Mark Carlsen' })).toBeDefined();
  });

  /**
   * One car at a time. Opening a second replaces the first rather than joining it, and the swap is
   * two named commands on the wire — the car being dropped is unfocused by name, never by clearing
   * the focus and re-stating what is left. That distinction is what protects the ring buffers: a
   * reset would take the departing car's rings, and at 60 Hz it would leave a window in which the
   * arriving car sends nothing either.
   */
  it('replaces the open car when a second is opened', async () => {
    const app = await renderApp('/rooms/room-1');
    await act(async () => {
      socket().deliver(roomList);
      socket().deliver(twoCollectorTower);
    });

    await act(async () => {
      screen.getByRole('button', { name: 'Open telemetry for Mark Carlsen' }).click();
    });
    await act(async () => {
      screen.getByRole('button', { name: 'Open telemetry for Rival' }).click();
    });

    // The URL names the room and nothing else.
    expect(app.currentPath()).toBe('/rooms/room-1');
    expect(allSent()).toContainEqual({ type: 'focusDriver', driverKey: 'id:4242' });
    expect(allSent()).toContainEqual({ type: 'focusDriver', driverKey: 'id:9' });
    expect(allSent()).toContainEqual({ type: 'unfocusDriver', driverKey: 'id:4242' });
    expect(allSent()).not.toContainEqual({ type: 'focusDriver', driverKey: null });

    // One wall, headed by the car that replaced the first.
    expect(screen.getByRole('heading', { name: 'Rival' })).toBeDefined();
    expect(screen.queryByRole('heading', { name: 'Mark Carlsen' })).toBeNull();
  });

  /**
   * Closing the open car returns the page to its other state: the tower, and nothing else. The
   * stream is given up by name at the same time, because the follow set is derived from the
   * selection rather than tracked beside it.
   */
  it('closes the car and goes back to the tower alone', async () => {
    await renderApp('/rooms/room-1');
    await act(async () => {
      socket().deliver(roomList);
      socket().deliver(tower);
    });

    await act(async () => {
      screen.getByRole('button', { name: 'Open telemetry for Mark Carlsen' }).click();
    });

    expect(screen.getByRole('region', { name: 'Pit wall' })).toBeDefined();

    await act(async () => {
      screen.getByRole('button', { name: 'Open telemetry for Mark Carlsen' }).click();
    });

    expect(screen.queryByRole('region', { name: 'Pit wall' })).toBeNull();
    expect(allSent()).toContainEqual({ type: 'unfocusDriver', driverKey: 'id:4242' });
  });

  /**
   * Lap history comes from standings, which the hub has for the whole field — so expansion is not
   * a privilege of the drivers running a collector. That distinction is the entire point of
   * separating the disclosure control from the telemetry one.
   */
  it('expands a driver with no collector of their own', async () => {
    await renderApp('/rooms/room-1');
    await act(async () => {
      socket().deliver(roomList);
      socket().deliver(tower);
    });

    const rival = screen.getByRole('button', { name: /Rival/ });
    expect(rival.getAttribute('aria-expanded')).toBe('false');

    await act(async () => {
      rival.click();
    });

    expect(rival.getAttribute('aria-expanded')).toBe('true');
    expect(allSent()).toContainEqual({ type: 'subscribeLapHistory', driverKey: 'id:9' });

    await act(async () => {
      socket().deliver({
        type: 'lapHistory',
        roomId: 'room-1',
        driverKey: 'id:9',
        truncated: false,
        laps: [
          {
            lapNumber: 1,
            lapTimeMs: 95_000,
            sectorMs: [30_000, 62_000, 95_000],
            valid: true,
          },
        ],
      });
    });

    expect(screen.getByText('1:35.000')).toBeDefined();
  });

  /** A collapsed row that kept receiving history would have the hub working for nobody. */
  it('unsubscribes when a row is collapsed', async () => {
    await renderApp('/rooms/room-1');
    await act(async () => {
      socket().deliver(roomList);
      socket().deliver(tower);
    });

    const rival = screen.getByRole('button', { name: /Rival/ });
    await act(async () => {
      rival.click();
    });
    await act(async () => {
      rival.click();
    });

    expect(allSent()).toContainEqual({ type: 'unsubscribeLapHistory', driverKey: 'id:9' });

    // Named rather than "drop all, then re-state the rest": subscriptions are a set, so clearing
    // them to remove one leaves a window in which the other open rows are unsubscribed, and a lap
    // completed inside that window is simply missed.
    expect(allSent()).not.toContainEqual({ type: 'subscribeLapHistory', driverKey: null });
  });

  /**
   * The hub keeps a room alive for thirty seconds after its last frame, so a socket that drops and
   * returns inside that window must resume the same room rather than dumping the viewer back to
   * the list. It can only do that by re-stating the subscription — the hub holds no per-viewer
   * memory across connections.
   */
  it('re-states its subscription after a reconnect', async () => {
    await renderApp('/rooms/room-1');
    await act(async () => {
      socket().deliver(roomList);
    });

    const dropped = socket();
    await act(async () => {
      dropped.close();
      // Past the first reconnect delay.
      await new Promise((resolve) => setTimeout(resolve, 700));
    });

    expect(FakeWebSocket.instances.length).toBeGreaterThan(1);
    expect(socket().sent).toContainEqual({ type: 'watchRoom', roomId: 'room-1' });
  });

  it('reports a lost connection rather than looking live', async () => {
    await renderApp();
    await act(async () => {
      socket().deliver(roomList);
    });

    expect(screen.getByText('Connected')).toBeDefined();

    await act(async () => {
      socket().close();
    });

    expect(screen.getByText('Reconnecting…')).toBeDefined();
  });

  it("shows the hub's answer when a driver has no telemetry", async () => {
    await renderApp();
    await act(async () => {
      socket().deliver({
        type: 'error',
        code: 'noTelemetryForDriver',
        message: 'That driver is not running a collector, so only timing is available for them.',
      });
    });

    expect(screen.getByText(/not running a collector/)).toBeDefined();
  });
});
