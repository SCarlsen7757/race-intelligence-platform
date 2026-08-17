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
          capabilities: ['TyreWear', 'TyrePressure', 'TyreTemperature'],
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

    // The panels the collector's declared capabilities allow, and no others.
    expect(screen.getByText('Tyre wear')).toBeDefined();
    expect(screen.getByText('Tyre pressure')).toBeDefined();
    expect(screen.queryByText('Damage')).toBeNull();
  });

  /**
   * The reason room and driver moved into the URL at all. The previous dashboard held both in
   * component state, so a refresh — or a link sent to whoever was asking about the tyres — landed
   * on an empty session list.
   */
  it('restores the session and the driver from the URL alone', async () => {
    await renderApp('/rooms/room-1/id:4242');
    await act(async () => {
      socket().deliver(roomList);
      socket().deliver(tower);
    });

    expect(allSent()).toContainEqual({ type: 'watchRoom', roomId: 'room-1' });
    expect(allSent()).toContainEqual({ type: 'focusDriver', driverKey: 'id:4242' });

    // Named from the tower rather than from the key in the path, so a link opens on a person
    // rather than on an id.
    expect(screen.getByRole('button', { name: 'Stop watching Mark Carlsen' })).toBeDefined();
  });

  /**
   * The comparison, end to end from the browser's side: two cars open at once, both streaming, and
   * the pair written into the URL so it survives a refresh and pastes into a message.
   */
  it('compares two drivers and says so in the URL', async () => {
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

    expect(app.currentPath()).toBe('/rooms/room-1/id:4242,id:9');
    expect(allSent()).toContainEqual({ type: 'focusDriver', driverKey: 'id:4242' });
    expect(allSent()).toContainEqual({ type: 'focusDriver', driverKey: 'id:9' });

    // Both columns, and the same section titles in both — the alignment is the whole point.
    expect(screen.getAllByText('Tyre wear')).toHaveLength(2);
  });

  /**
   * Dropping one half of a comparison must leave the other half alone. The named `unfocusDriver` is
   * what makes that true on the wire: clearing everything and re-stating the rest would leave a
   * window at 60 Hz in which the driver still being watched sends nothing.
   */
  it('drops one driver of a comparison without disturbing the other', async () => {
    const app = await renderApp('/rooms/room-1/id:4242,id:9');
    await act(async () => {
      socket().deliver(roomList);
      socket().deliver(twoCollectorTower);
    });

    await act(async () => {
      screen.getByRole('button', { name: 'Stop watching Mark Carlsen' }).click();
    });

    expect(app.currentPath()).toBe('/rooms/room-1/id:9');
    expect(allSent()).toContainEqual({ type: 'unfocusDriver', driverKey: 'id:4242' });
    expect(allSent()).not.toContainEqual({ type: 'unfocusDriver', driverKey: 'id:9' });
    expect(allSent()).not.toContainEqual({ type: 'focusDriver', driverKey: null });
  });

  /**
   * In a session where one person runs a collector there is genuinely nobody to compare against.
   * Saying so beats an empty second column, and beats a viewer hunting for a second button that
   * does not exist.
   */
  it('says why there is no second car when nobody else is publishing', async () => {
    await renderApp('/rooms/room-1/id:4242');
    await act(async () => {
      socket().deliver(roomList);
      socket().deliver(tower);
    });

    expect(screen.getByText(/no second car to compare against/)).toBeDefined();
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
