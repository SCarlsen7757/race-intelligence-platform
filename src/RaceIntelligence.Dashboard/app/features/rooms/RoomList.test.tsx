import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from '@tanstack/react-router';
import { act, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { LiveRoomSummary } from '../../shared/live/contracts';
import { RoomList } from './RoomList';

function room(overrides: Partial<LiveRoomSummary> = {}): LiveRoomSummary {
  return {
    roomId: 'room-1',
    gameKey: 'raceroom',
    trackName: 'Spa',
    layoutName: 'Grand Prix',
    sessionType: 2,
    driverCount: 12,
    publishers: [
      {
        clientId: 'client-1',
        clientName: 'Gaming PC',
        driverName: 'Mark',
        simDriverId: '4242',
        connectedAtUtc: new Date().toISOString(),
        capabilities: ['TyreWear'],
      },
    ],
    lastUpdatedAtUtc: new Date().toISOString(),
    ...overrides,
  };
}

/**
 * The list renders `Link`s, so it needs a router around it.
 *
 * Anchors rather than click handlers because a session is a place now: middle-clicking a row
 * should open it in a tab, and hovering it should show where it goes in the status bar. Neither
 * works with a button.
 */
async function renderList(rooms: LiveRoomSummary[], connected = true) {
  const rootRoute = createRootRoute({
    component: () => <RoomList rooms={rooms} connected={connected} />,
  });
  const sessionRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: '/rooms/$roomId',
    component: () => null,
  });

  const router = createRouter({
    routeTree: rootRoute.addChildren([sessionRoute]),
    history: createMemoryHistory({ initialEntries: ['/'] }),
  });

  // Matching a route is asynchronous; rendering without loading first puts an empty div on screen.
  await router.load();

  return render(<RouterProvider router={router} />);
}

describe('RoomList', () => {
  it('lists a live session with its track, type and car count', async () => {
    await renderList([room()]);

    expect(screen.getByText('Spa')).toBeDefined();
    expect(screen.getByText('Grand Prix')).toBeDefined();
    expect(screen.getByText('Race')).toBeDefined();
    expect(screen.getByText('12 cars')).toBeDefined();
  });

  it('prefers the driver name over the machine name', async () => {
    await renderList([room()]);

    expect(screen.getByText('Mark')).toBeDefined();
    expect(screen.queryByText('Gaming PC')).toBeNull();
  });

  it('falls back to the machine name when the driver is unknown', async () => {
    await renderList([room({ publishers: [{ ...room().publishers[0]!, driverName: null }] })]);

    expect(screen.getByText('Gaming PC')).toBeDefined();
  });

  it('links each session to its own URL, so it can be opened or shared', async () => {
    await renderList([room()]);

    expect(screen.getByRole('link').getAttribute('href')).toBe('/rooms/room-1');
  });

  /**
   * A room whose collectors have all dropped is kept briefly so a reconnect rejoins it rather than
   * starting a new session. Saying so beats an empty space that looks like a rendering bug.
   */
  it('says when a session has no collector attached', async () => {
    await renderList([room({ publishers: [] })]);

    expect(screen.getByText(/No collector connected/)).toBeDefined();
  });

  it('distinguishes an empty hub from a disconnected dashboard', async () => {
    await renderList([], true);
    expect(screen.getByText(/Start a collector/)).toBeDefined();

    await renderList([], false);
    expect(screen.getByText(/Waiting for the hub/)).toBeDefined();
  });

  it('uses the singular for a one-car session', async () => {
    await renderList([room({ driverCount: 1 })]);

    expect(screen.getByText('1 car')).toBeDefined();
  });

  /**
   * The room list is only pushed over the socket when something changes, so a quiet session's
   * `rooms` array never gets replaced. The age still has to move — that's the whole bug this
   * covers — so the clock has to be the thing ticking it, not a new prop from above.
   *
   * Fake timers go on after `renderList`'s own `router.load()` await, per that helper's own
   * warning: installing them earlier stalls the router's async route matching.
   */
  it("ticks a session's age without a new rooms array", async () => {
    const staleRoom = room({
      lastUpdatedAtUtc: new Date('2026-08-18T11:59:00.000Z').toISOString(),
    });

    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(new Date('2026-08-18T12:00:00.000Z'));
    try {
      await renderList([staleRoom]);

      expect(screen.getByText('1m ago')).toBeDefined();

      act(() => {
        vi.advanceTimersByTime(60_000);
      });

      expect(screen.getByText('2m ago')).toBeDefined();
    } finally {
      vi.useRealTimers();
    }
  });
});
