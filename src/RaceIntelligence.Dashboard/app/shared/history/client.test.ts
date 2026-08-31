import { afterEach, describe, expect, it } from 'vitest';
import { DEFAULT_READ_URL } from './readUrlBuild';
import { fetchLapTelemetry, fetchSession, fetchSessions, ReadApiError } from './client';
import { stubFetch, type FetchStub } from '../../testing/stubFetch';

describe('the read API client', () => {
  let stub: FetchStub | null = null;

  afterEach(() => {
    stub?.restore();
    stub = null;
  });

  it('reads a page of sessions from the read origin, not the hub', async () => {
    stub = stubFetch({ '/api/v1/sessions': { body: { sessions: [], nextBefore: null } } });

    await fetchSessions();

    // The two origins are genuinely different services; a history fetch aimed at the hub gets a 404.
    expect(stub.requested).toEqual(['/api/v1/sessions']);
  });

  it('sends limit and before only when asked for them', async () => {
    stub = stubFetch({
      '/api/v1/sessions': { body: { sessions: [] } },
      '/api/v1/sessions?limit=5': { body: { sessions: [] } },
      '/api/v1/sessions?limit=5&before=2026-01-01T00%3A00%3A00Z': { body: { sessions: [] } },
    });

    await fetchSessions();
    await fetchSessions({ limit: 5 });
    await fetchSessions({ limit: 5, before: '2026-01-01T00:00:00Z' });

    expect(stub.requested).toEqual([
      '/api/v1/sessions',
      '/api/v1/sessions?limit=5',
      '/api/v1/sessions?limit=5&before=2026-01-01T00%3A00%3A00Z',
    ]);
  });

  it('asks for the laps it was given, keyed back by lap', async () => {
    stub = stubFetch({
      '/api/v1/sessions/abc/telemetry?lap=3': {
        body: { sessionId: 'abc', laps: [{ lapNumber: 3, samples: [] }] },
      },
    });

    const telemetry = await fetchLapTelemetry('abc', [3]);

    expect(telemetry.laps.map((lap) => lap.lapNumber)).toEqual([3]);
  });

  it('spells several laps as one comma-separated request', async () => {
    stub = stubFetch({
      '/api/v1/sessions/abc/telemetry?lap=3,5': {
        body: {
          sessionId: 'abc',
          laps: [
            { lapNumber: 3, samples: [] },
            { lapNumber: 5, samples: [] },
          ],
        },
      },
    });

    // One round trip, so an overlay cannot have its laps arrive out of step.
    const telemetry = await fetchLapTelemetry('abc', [3, 5]);

    expect(telemetry.laps.map((lap) => lap.lapNumber)).toEqual([3, 5]);
  });

  it('surfaces the problem detail rather than a generic failure', async () => {
    stub = stubFetch({
      '/api/v1/sessions/abc/telemetry?lap=3': {
        status: 400,
        body: {
          detail: 'Lap 3 holds 41000 samples; this endpoint serves at most 36000 for one lap.',
        },
      },
    });

    // The server writes these to be shown. Replacing them with "request failed" throws away the
    // only sentence that explains what to do next.
    await expect(fetchLapTelemetry('abc', [3])).rejects.toThrow(/41000 samples/);
  });

  it('marks a 404 so a caller can tell "no such session" from "service down"', async () => {
    stub = stubFetch({
      '/api/v1/sessions/missing': { status: 404, body: { detail: 'No session.' } },
    });

    // The two produce very different things to put on screen, so the distinction has to survive
    // the throw rather than being recovered from the message.
    const error = await fetchSession('missing').catch((cause: unknown) => cause);

    expect(error).toBeInstanceOf(ReadApiError);
    expect((error as ReadApiError).isNotFound).toBe(true);
    expect((error as ReadApiError).status).toBe(404);
  });

  it('does not mark a 503 as not-found', async () => {
    stub = stubFetch({ '/api/v1/sessions/abc': { status: 503, body: { detail: 'Down.' } } });

    const error = await fetchSession('abc').catch((cause: unknown) => cause);

    expect((error as ReadApiError).isNotFound).toBe(false);
  });

  it('falls back to a status message when the error body is not a problem document', async () => {
    stub = stubFetch({ '/api/v1/sessions': { status: 503, body: 'nope' } });

    await expect(fetchSessions()).rejects.toThrow(/status 503/);
  });

  it('builds URLs against the default read origin under test', () => {
    // Pinned in vitest.config.ts so this assertion does not answer differently on a machine that
    // happens to have READ_URL exported.
    expect(DEFAULT_READ_URL).toBe('http://localhost:5049');
  });
});
