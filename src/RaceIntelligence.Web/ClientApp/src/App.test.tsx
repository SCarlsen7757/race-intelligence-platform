import { act, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { App } from './App';
import type { LiveViewCommand, LiveViewMessage } from './live/contracts';
import { LiveProvider } from './live/LiveProvider';

/**
 * A stand-in for the browser's WebSocket that a test can push messages into and read commands out
 * of.
 *
 * jsdom has no WebSocket, so something has to fill the gap regardless. Making it scriptable turns
 * that necessity into the one test that covers the whole frontend path — provider, socket,
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

const tower: LiveViewMessage = {
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
      pitStopStatus: -1,
      finishStatus: 0,
      tier: 'Observed',
    },
  ],
};

function socket(): FakeWebSocket {
  const instance = FakeWebSocket.instances.at(-1);
  if (instance === undefined) {
    throw new Error('The app never opened a socket.');
  }

  return instance;
}

describe('App', () => {
  beforeEach(() => {
    FakeWebSocket.instances = [];
    vi.stubGlobal('WebSocket', FakeWebSocket);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const mount = () =>
    render(
      <LiveProvider>
        <App />
      </LiveProvider>,
    );

  it('opens a socket against its own origin, so no address needs configuring', async () => {
    mount();
    await act(async () => {});

    expect(socket().url).toBe(`ws://${window.location.host}/live/view`);
  });

  it('shows the sessions the hub reports', async () => {
    mount();
    await act(async () => {
      socket().deliver(roomList);
    });

    expect(screen.getByText('Spa')).toBeDefined();
  });

  /** The whole first slice from the browser's side: pick a session, see the field. */
  it('watches a session and renders its timing tower', async () => {
    mount();
    await act(async () => {
      socket().deliver(roomList);
    });

    await act(async () => {
      screen.getByRole('button', { name: /Spa/ }).click();
    });

    expect(socket().sent).toContainEqual({ type: 'watchRoom', roomId: 'room-1' });

    await act(async () => {
      socket().deliver(tower);
    });

    expect(screen.getByText('Mark Carlsen')).toBeDefined();
    expect(screen.getByText('Rival')).toBeDefined();
    expect(screen.getByText('1:42.500')).toBeDefined();
  });

  it('subscribes to a driver whose own machine is publishing', async () => {
    mount();
    await act(async () => {
      socket().deliver(roomList);
    });
    await act(async () => {
      screen.getByRole('button', { name: /Spa/ }).click();
    });
    await act(async () => {
      socket().deliver(tower);
    });

    await act(async () => {
      screen.getByRole('button', { name: /Mark Carlsen/ }).click();
    });

    expect(socket().sent).toContainEqual({ type: 'focusDriver', driverKey: 'id:4242' });

    // The panels the collector's declared capabilities allow, and no others.
    expect(screen.getByText('Tyre wear')).toBeDefined();
    expect(screen.getByText('Tyre pressure')).toBeDefined();
  });

  /**
   * The hub keeps a room alive for thirty seconds after its last frame, so a socket that drops and
   * returns inside that window must resume the same room rather than dumping the viewer back to
   * the list. It can only do that by re-stating the subscription — the hub holds no per-viewer
   * memory across connections.
   */
  it('re-states its subscription after a reconnect', async () => {
    mount();
    await act(async () => {
      socket().deliver(roomList);
    });
    await act(async () => {
      screen.getByRole('button', { name: /Spa/ }).click();
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
    mount();
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
    mount();
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
