/**
 * Reading sessions that have already happened.
 *
 * **The first HTTP in this app.** Everything else on screen arrives over one WebSocket, and that
 * was a deliberate property rather than an accident — but a socket can only carry what is
 * happening now, and none of the charts that plot a stint or a session can be answered by a
 * thirty-second rolling window.
 *
 * **No query library.** TanStack Router's loaders already give caching, dedupe-on-preload and
 * pending states, and this data is immutable once written, which is the case a query cache earns
 * least. Adding one would be a sixth runtime dependency for behaviour the router already has.
 */

import type { StoredLap, StoredTelemetry, StoredSessionPage, StoredSession } from './contracts';
import { readUrl } from './readUrl';

/**
 * A read that failed, carrying the status so a caller can tell "no such session" from "the service
 * is down" — the two produce very different things to put on screen.
 */
export class ReadApiError extends Error {
  constructor(
    readonly status: number,
    readonly path: string,
    message: string,
  ) {
    super(message);
    this.name = 'ReadApiError';
  }

  /** Whether this was a 404, i.e. the thing asked for does not exist rather than could not be fetched. */
  get isNotFound(): boolean {
    return this.status === 404;
  }
}

/**
 * One GET against the read API.
 *
 * The error path reads the body before throwing because the API answers failures with RFC 7807
 * problem documents, and their `detail` is written to be shown — "That lap holds 41000 samples;
 * this endpoint serves at most 36000 at a time" is worth surfacing rather than replacing with
 * "request failed".
 */
async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  // `signal` spread in only when present: `exactOptionalPropertyTypes` is on, and `RequestInit`
  // types the field as `AbortSignal | null` rather than optional-undefined.
  const response = await fetch(readUrl(path), {
    ...(signal === undefined ? {} : { signal }),
    headers: { accept: 'application/json' },
  });

  if (!response.ok) {
    throw new ReadApiError(response.status, path, await problemDetail(response));
  }

  return (await response.json()) as T;
}

async function problemDetail(response: Response): Promise<string> {
  try {
    const body: unknown = await response.json();
    if (typeof body === 'object' && body !== null && 'detail' in body) {
      const detail = (body as { detail?: unknown }).detail;
      if (typeof detail === 'string' && detail !== '') {
        return detail;
      }
    }
  } catch {
    // A non-JSON error body is not itself an error worth reporting — the status is the fact that
    // matters, and it is already on the exception.
  }

  return `Request failed with status ${response.status}.`;
}

/** Options for {@link fetchSessions}. */
export interface FetchSessionsOptions {
  /** How many to return. The server clamps this, and refuses anything outside its range. */
  readonly limit?: number;
  /** Return only sessions that started strictly before this instant. The previous page's `nextBefore`. */
  readonly before?: string;
  readonly signal?: AbortSignal;
}

/** A page of stored sessions, newest first. */
export function fetchSessions(options: FetchSessionsOptions = {}): Promise<StoredSessionPage> {
  const query = new URLSearchParams();
  if (options.limit !== undefined) {
    query.set('limit', String(options.limit));
  }
  if (options.before !== undefined) {
    query.set('before', options.before);
  }

  const suffix = query.size > 0 ? `?${query.toString()}` : '';
  return getJson<StoredSessionPage>(`/api/v1/sessions${suffix}`, options.signal);
}

/** One stored session. Throws a {@link ReadApiError} with `isNotFound` when there is no such session. */
export function fetchSession(sessionId: string, signal?: AbortSignal): Promise<StoredSession> {
  return getJson<StoredSession>(`/api/v1/sessions/${sessionId}`, signal);
}

/** Every lap of one session, in lap order. */
export function fetchLaps(sessionId: string, signal?: AbortSignal): Promise<StoredLap[]> {
  return getJson<StoredLap[]>(`/api/v1/sessions/${sessionId}/laps`, signal);
}

/**
 * Which laps of a session actually have telemetry.
 *
 * Not the same list as {@link fetchLaps} returns, and the difference is real: a lap row is written
 * when a lap completes, samples arrive throughout. A picker offering laps to chart wants this one.
 */
export function fetchSampledLapNumbers(sessionId: string, signal?: AbortSignal): Promise<number[]> {
  return getJson<number[]>(`/api/v1/sessions/${sessionId}/telemetry/laps`, signal);
}

/**
 * Telemetry for one or more laps, each in capture order.
 *
 * Several laps in one request because an overlay — your best lap against your current one — is the
 * normal way this is read, and four round trips to draw one picture is four chances for the laps to
 * arrive out of step. The server caps how many it will serve at once and refuses the rest by name.
 */
export function fetchLapTelemetry(
  sessionId: string,
  lapNumbers: readonly number[],
  signal?: AbortSignal,
): Promise<StoredTelemetry> {
  const laps = lapNumbers.join(',');
  return getJson<StoredTelemetry>(`/api/v1/sessions/${sessionId}/telemetry?lap=${laps}`, signal);
}
