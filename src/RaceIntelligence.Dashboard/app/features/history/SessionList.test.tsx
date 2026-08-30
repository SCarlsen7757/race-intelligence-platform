import {
  createMemoryHistory,
  createRootRoute,
  createRouter,
  RouterProvider,
} from '@tanstack/react-router';
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { StoredSession } from '../../shared/history/contracts';
import { SessionList } from './SessionList';

/**
 * Renders the list inside a router, because every card is a `Link` and a `Link` outside a router
 * throws. The route tree is a bare root — nothing here navigates, it only needs `Link` to resolve.
 */
async function renderList(sessions: readonly StoredSession[]) {
  const rootRoute = createRootRoute({ component: () => <SessionList sessions={sessions} /> });
  const router = createRouter({
    routeTree: rootRoute,
    history: createMemoryHistory({ initialEntries: ['/'] }),
  });

  await router.load();
  render(<RouterProvider router={router} />);
}

// Overrides allow an explicit `undefined`, which `Partial` does not under
// `exactOptionalPropertyTypes` — and "this session has no track name" is exactly what several of
// these tests are about.
type SessionOverrides = { [K in keyof StoredSession]?: StoredSession[K] | undefined };

function session(overrides: SessionOverrides = {}): StoredSession {
  // Asserted because spreading the overrides widens every required field to `| undefined`, which is
  // the price of letting a test say "this one has no track name" at all.
  return {
    sessionId: '11111111-1111-1111-1111-111111111111',
    startedAtUtc: new Date().toISOString(),
    trackName: 'Suzuka',
    layoutName: 'Grand Prix',
    carName: 'Test GT3 Car',
    playerName: 'Ayrton',
    sessionType: 3,
    lapCount: 12,
    sampleCount: 5000,
    ...overrides,
  } as StoredSession;
}

describe('SessionList', () => {
  it('says so when there is nothing stored', async () => {
    await renderList([]);

    expect(screen.getByText('No stored sessions')).toBeDefined();
  });

  it('shows the track, car and lap count', async () => {
    await renderList([session()]);

    expect(screen.getByText('Suzuka')).toBeDefined();
    expect(screen.getByText('Grand Prix')).toBeDefined();
    expect(screen.getByText('Test GT3 Car')).toBeDefined();
    expect(screen.getByText('12 laps')).toBeDefined();
  });

  it('singularises a one-lap session', async () => {
    await renderList([session({ lapCount: 1 })]);

    expect(screen.getByText('1 lap')).toBeDefined();
  });

  it('warns when a session stored no telemetry', async () => {
    // A real case — laps recorded, upload never caught up — and the card has to say so, because
    // opening it would otherwise show a page with nothing on it and no explanation.
    await renderList([session({ sampleCount: 0 })]);

    expect(screen.getByText('No telemetry stored')).toBeDefined();
  });

  it('links each session to its own page', async () => {
    await renderList([session()]);

    const link = screen.getByRole('link');
    expect(link.getAttribute('href')).toContain('/sessions/11111111-1111-1111-1111-111111111111');
  });

  it('prefers the name in use at the time over the driver current name', async () => {
    // playerName is what the session recorded; driverName tracks renames. Showing the latter would
    // relabel history every time somebody changes their handle.
    await renderList([session({ playerName: 'Ayrton', driverName: 'Renamed Later' })]);

    expect(screen.getByText('Ayrton')).toBeDefined();
  });

  it('falls back to the driver name when the session recorded none', async () => {
    await renderList([session({ playerName: undefined, driverName: 'Known Driver' })]);

    expect(screen.getByText('Known Driver')).toBeDefined();
  });

  it('survives a session whose track never resolved', async () => {
    await renderList([session({ trackName: undefined, layoutName: undefined })]);

    expect(screen.getByText('Unknown track')).toBeDefined();
  });
});
