import { screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { renderApp } from '../../testing/renderApp';
import { stubFetch, type FetchStub } from '../../testing/stubFetch';

/**
 * The history routes end to end: loader, fetch, render, and picking a lap.
 *
 * These go through `renderApp` rather than rendering a component directly, because the thing worth
 * asserting is that a route with a loader — the first in this app — actually resolves its data
 * before the page paints.
 */
describe('the stored-session routes', () => {
  let stub: FetchStub | null = null;

  afterEach(() => {
    stub?.restore();
    stub = null;
  });

  const sessionId = '11111111-1111-1111-1111-111111111111';

  const storedSession = {
    sessionId,
    startedAtUtc: new Date().toISOString(),
    trackName: 'Suzuka',
    layoutName: 'Grand Prix',
    carName: 'Test GT3 Car',
    playerName: 'Ayrton',
    sessionType: 3,
    lapCount: 2,
    sampleCount: 6,
  };

  function samplesFor(lapNumber: number) {
    return {
      sessionId,
      lapNumber,
      samples: [0, 1, 2].map((i) => ({
        sequenceNumber: i,
        timestampUtc: new Date().toISOString(),
        simulationTime: i * 0.1,
        lapNumber,
        sector: 1,
        speed: 40 + i,
        throttle: 1,
        brake: 0,
        steering: 0,
        engineRpm: 6500,
        fuelLeft: 40,
      })),
    };
  }

  it('lists stored sessions on /sessions', async () => {
    stub = stubFetch({
      '/api/v1/sessions': { body: { sessions: [storedSession] } },
    });

    await renderApp('/sessions');

    expect(screen.getByText('Suzuka')).toBeDefined();
    expect(screen.getByText('2 laps')).toBeDefined();
  });

  it('shows the empty state when nothing is stored', async () => {
    stub = stubFetch({ '/api/v1/sessions': { body: { sessions: [] } } });

    await renderApp('/sessions');

    expect(screen.getByText('No stored sessions')).toBeDefined();
  });

  it('opens a session with its laps and charts the first sampled one', async () => {
    stub = stubFetch({
      [`/api/v1/sessions/${sessionId}`]: { body: storedSession },
      [`/api/v1/sessions/${sessionId}/laps`]: {
        body: [
          { lapNumber: 1, lapTimeMs: 93_250, isValid: true },
          { lapNumber: 2, lapTimeMs: 92_000, isValid: true },
        ],
      },
      [`/api/v1/sessions/${sessionId}/telemetry/laps`]: { body: [1, 2] },
      [`/api/v1/sessions/${sessionId}/telemetry?lap=2`]: { body: samplesFor(2) },
    });

    await renderApp(`/sessions/${sessionId}`);

    expect(screen.getByText('Suzuka — Grand Prix')).toBeDefined();
    expect(screen.getByText('Lap 1')).toBeDefined();
    expect(screen.getByText('Lap 2')).toBeDefined();

    // A lap is selected without being asked for, so the page is never a chart-shaped hole waiting
    // for a click. Lap 2 is the fastest timed one here.
    await waitFor(() => {
      expect(stub?.requested).toContain(`/api/v1/sessions/${sessionId}/telemetry?lap=2`);
    });
  });

  it('opens on the fastest timed lap, not the out-lap', async () => {
    // Lap 1 is the out-lap in every real session and is untimed; opening on it charts the least
    // interesting thing in the session.
    stub = stubFetch({
      [`/api/v1/sessions/${sessionId}`]: { body: storedSession },
      [`/api/v1/sessions/${sessionId}/laps`]: {
        body: [
          { lapNumber: 2, lapTimeMs: 93_250, isValid: true },
          { lapNumber: 3, lapTimeMs: 92_068, isValid: true },
          { lapNumber: 4, lapTimeMs: 92_896, isValid: true },
        ],
      },
      [`/api/v1/sessions/${sessionId}/telemetry/laps`]: { body: [1, 2, 3, 4] },
      [`/api/v1/sessions/${sessionId}/telemetry?lap=3`]: { body: samplesFor(3) },
    });

    await renderApp(`/sessions/${sessionId}`);

    await waitFor(() => {
      expect(stub?.requested).toContain(`/api/v1/sessions/${sessionId}/telemetry?lap=3`);
    });
  });

  it('falls back to the first sampled lap when nothing is timed', async () => {
    stub = stubFetch({
      [`/api/v1/sessions/${sessionId}`]: { body: storedSession },
      [`/api/v1/sessions/${sessionId}/laps`]: { body: [] },
      [`/api/v1/sessions/${sessionId}/telemetry/laps`]: { body: [5, 6] },
      [`/api/v1/sessions/${sessionId}/telemetry?lap=5`]: { body: samplesFor(5) },
    });

    await renderApp(`/sessions/${sessionId}`);

    await waitFor(() => {
      expect(stub?.requested).toContain(`/api/v1/sessions/${sessionId}/telemetry?lap=5`);
    });
  });

  it('ignores a timed lap that has no telemetry when choosing a default', async () => {
    // The two lists genuinely diverge, so the fastest lap overall may be uncharteable.
    stub = stubFetch({
      [`/api/v1/sessions/${sessionId}`]: { body: storedSession },
      [`/api/v1/sessions/${sessionId}/laps`]: {
        body: [
          { lapNumber: 2, lapTimeMs: 90_000, isValid: true },
          { lapNumber: 3, lapTimeMs: 92_068, isValid: true },
        ],
      },
      [`/api/v1/sessions/${sessionId}/telemetry/laps`]: { body: [3] },
      [`/api/v1/sessions/${sessionId}/telemetry?lap=3`]: { body: samplesFor(3) },
    });

    await renderApp(`/sessions/${sessionId}`);

    await waitFor(() => {
      expect(stub?.requested).toContain(`/api/v1/sessions/${sessionId}/telemetry?lap=3`);
    });
  });

  it('offers only the laps that actually have telemetry', async () => {
    // The lap list and the sampled-lap list genuinely diverge — a lap row is written on completion,
    // samples arrive throughout — and offering a lap that charts nothing is the failure this avoids.
    stub = stubFetch({
      [`/api/v1/sessions/${sessionId}`]: { body: storedSession },
      [`/api/v1/sessions/${sessionId}/laps`]: {
        body: [
          { lapNumber: 1, lapTimeMs: 93_250, isValid: true },
          { lapNumber: 2, lapTimeMs: 92_000, isValid: true },
        ],
      },
      [`/api/v1/sessions/${sessionId}/telemetry/laps`]: { body: [2] },
      [`/api/v1/sessions/${sessionId}/telemetry?lap=2`]: { body: samplesFor(2) },
    });

    await renderApp(`/sessions/${sessionId}`);

    expect(screen.queryByText('Lap 1')).toBeNull();
    expect(screen.getByText('Lap 2')).toBeDefined();
  });

  it('says so when a session stored no telemetry at all', async () => {
    stub = stubFetch({
      [`/api/v1/sessions/${sessionId}`]: { body: { ...storedSession, sampleCount: 0 } },
      [`/api/v1/sessions/${sessionId}/laps`]: { body: [] },
      [`/api/v1/sessions/${sessionId}/telemetry/laps`]: { body: [] },
    });

    await renderApp(`/sessions/${sessionId}`);

    expect(screen.getByText('No telemetry stored')).toBeDefined();
  });

  it('shows the read API problem detail when a lap will not load', async () => {
    stub = stubFetch({
      [`/api/v1/sessions/${sessionId}`]: { body: storedSession },
      [`/api/v1/sessions/${sessionId}/laps`]: {
        body: [{ lapNumber: 1, lapTimeMs: 93_250, isValid: true }],
      },
      [`/api/v1/sessions/${sessionId}/telemetry/laps`]: { body: [1] },
      [`/api/v1/sessions/${sessionId}/telemetry?lap=1`]: {
        status: 400,
        body: {
          detail: 'That lap holds 41000 samples; this endpoint serves at most 36000 at a time.',
        },
      },
    });

    await renderApp(`/sessions/${sessionId}`);

    await waitFor(() => {
      expect(screen.getByText(/41000 samples/)).toBeDefined();
    });
  });
});
