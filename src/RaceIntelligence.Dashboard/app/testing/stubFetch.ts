/**
 * A `fetch` stub, in the same spirit as the hand-rolled `FakeWebSocket` the socket tests use.
 *
 * Hand-rolled rather than MSW for the reason that file gives for its own duplication: the surface
 * being faked is small and completely known — a handful of GETs against one origin — and a service
 * worker's worth of machinery to intercept them would be more to understand than the thing under
 * test. If the read surface ever grows a write side or conditional requests, revisit.
 */

/** What a stubbed route answers with. */
export interface StubResponse {
  /** Defaults to 200. */
  readonly status?: number;
  /** Serialised as the JSON body. */
  readonly body: unknown;
}

/** Routes to answer, keyed by the path (and query) the client will request. */
export type StubRoutes = Readonly<Record<string, StubResponse>>;

/** A handle for asserting on what was requested, and for putting the real `fetch` back. */
export interface FetchStub {
  /** Every path requested, in order, with the origin stripped. */
  readonly requested: readonly string[];
  restore(): void;
}

/**
 * Replaces `globalThis.fetch` for the duration of a test.
 *
 * An unmatched path throws rather than 404s, and deliberately: a test that mistypes a route should
 * fail saying so, not exercise the client's not-found branch and pass for the wrong reason. The
 * message lists what was registered, because the mismatch is nearly always a query string.
 */
export function stubFetch(routes: StubRoutes): FetchStub {
  const original = globalThis.fetch;
  const requested: string[] = [];

  globalThis.fetch = (input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    const path = url.replace(/^https?:\/\/[^/]+/, '');
    requested.push(path);

    const match = routes[path];
    if (match === undefined) {
      throw new Error(
        `stubFetch: no route registered for '${path}'. Registered: ${Object.keys(routes).join(', ') || '(none)'}`,
      );
    }

    return Promise.resolve(
      new Response(JSON.stringify(match.body), {
        status: match.status ?? 200,
        headers: { 'content-type': 'application/json' },
      }),
    );
  };

  return {
    requested,
    restore() {
      globalThis.fetch = original;
    },
  };
}
