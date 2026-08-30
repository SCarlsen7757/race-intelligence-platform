# 0003 — Deployment topology: containers, two origins, one tunnel

**Status:** Accepted, and exercised by the first test deployment. The compose file
(`compose.test.yml`) is named for what it is — a test topology built from a working tree, not a
release artifact, because there are no release artifacts yet.
**Relates to:** [0001 — per-simulator storage](0001-per-sim-storage.md), whose "N deployments" cost
this file is the first concrete instance of.

---

## Context

Nothing had ever been deployed. `src/RaceIntelligence.AppHost/AppHost.cs` orchestrates the whole
pipeline for local development and says in its own header that it is not the production topology,
but nothing described what the production topology *was*. The repo had no Dockerfile, no compose
file, no `.dockerignore`, and no `ASPNETCORE_URLS` anywhere — the packaging simply did not exist.

The first real test session needs the backend on a home-server VM, the collector on the gaming PC,
and the dashboard reachable from outside the house.

## Decision

**Four processes, split by where they can physically run.**

The collector runs on the gaming PC and nowhere else. It reads a Windows named shared-memory block
(`$R3E`) that exists only while RaceRoom is running, so it is not containerised and does not appear
in the compose file. Everything else — PostgreSQL, the ingest API, the live hub, the dashboard —
runs in containers on the server.

**Images are built from a clone on the server; there is no registry.**

Consistent with the versioning rule in `CLAUDE.md`: there are no tags, nothing is released, and
every process is expected to come from the same commit. A registry would introduce exactly the
version skew the project has decided it does not yet have.

**Migrations are a one-shot container, not a startup step.**

Both API hosts apply migrations only in Development; production is deliberately out-of-band. Rather
than delete that guard, the compose file supplies the missing out-of-band step: a self-contained EF
Core migration bundle in its own image, which everything needing a prepared schema waits on with
`service_completed_successfully`. A failed migration is then a container that exited non-zero with
its own logs, rather than an API in a restart loop — and it stays correct if the API is ever run
with more than one replica.

**Three public hostnames, because the dashboard, the hub and the read API are genuinely three
origins.**

The dashboard is a Node service; the browser loads the page from it and then opens its WebSocket
*straight at the hub*, not proxied back through Node. One hostname would mean putting the hub behind
the dashboard's path space and reintroducing the proxy hop that arrangement exists to avoid. So:
`race-web.*` to the dashboard, `race-api.*` to the hub, `race-read.*` to the read API, and both
back-end services name the dashboard's public origin in their allowlists.

The read API is the third because history is not live state and the hub holds none: it has no
database credentials by design, and giving it some to serve one more route would undo the property
that makes it the safe service to expose. See [0001](0001-per-sim-storage.md) for why the read API
is per-simulator and why it is not simply more endpoints on the ingest host.

**The ingest API stays on the LAN — and the read API does not.**

`Ingest.Api/Auth/ApiKeyFilter.cs` documents its own key check as a Phase-1 compromise with a
non-constant-time comparison and says plainly that it should not be exposed beyond the LAN. It is
therefore absent from the tunnel's ingress. The hub is the service built for exposure — constant-time
key comparison, origin allowlist, WebSocket keep-alive — and the read API is built for it from the
other direction: it holds no key to leak, writes nothing, and serves only GETs behind its own origin
allowlist. Those two are published; the ingest API is not.

**That split is why the read API is a separate process rather than a `MapGet` beside the existing
`MapPost`.** Same database, same table, opposite auth posture — and one service cannot have two.

The collector also publishes to the hub over the LAN rather than out through the tunnel and back,
which is both lower latency and one less dependency during a race.

**No application healthchecks in compose.**

`/health` and `/alive` are mapped only in Development, with a comment warning against exposing them
elsewhere. Ordering is instead expressed with PostgreSQL's `pg_isready` and the migration step's
completion, which is what actually needed sequencing. Adding container healthchecks would have meant
changing that code to serve compose's convenience.

## Consequences

- **`HUB_URL` and `READ_URL` are baked into the dashboard image at build time.** Vite `define`s it into the client
  bundle because the browser has no environment to read and the live routes are client-only.
  Repointing the dashboard at a different hub is a rebuild, not a restart, and it must be the public
  `https://` origin — the socket scheme is derived from that value, so an internal `http://` address
  produces a `ws://` URL that browsers block as mixed content. This is the single sharpest edge in
  the whole topology and the most likely cause of a dashboard that loads but never connects.
- **Three secrets, not one**, continuing the separation AppHost already established: the ingest key,
  the hub key, and PostgreSQL's password. They guard services with different exposure, so a leak of
  one must not compromise the others.
- **A second simulator means a second stack.** This is ADR 0001's "N deployments" cost arriving in
  concrete form: another database, another ingest API, another migration bundle. The hub and the
  dashboard are shared and do not multiply.
- **Nothing here applies forwarded headers.** No `UseForwardedHeaders`, `UseHttpsRedirection` or
  `UseHsts` exists anywhere, which is why the services sit behind a tunnel without complaint. The
  cost is that request logs show the tunnel's address rather than the client's, and any future
  scheme-dependent code would be wrong behind the proxy.
