# 0003 — Deployment topology: containers, four origins, one tunnel

**Status:** Accepted, and exercised by the first test deployment. **Amended** for a collector on
another person's network: the ingest API is now published (see "The ingest API is published too"
below, which replaces the section that kept it off the tunnel). Two counts in the original text were
already wrong before that amendment and are corrected here — the process list omitted the read API,
and the title said two origins when there were three. The compose file
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

**Five processes, split by where they can physically run.**

The collector runs on a gaming PC and nowhere else. It reads a Windows named shared-memory block
(`$R3E`) that exists only while RaceRoom is running, so it is not containerised and does not appear
in the compose file. There may now be more than one of them, on more than one network. Everything
else — PostgreSQL, the ingest API, the read API, the live hub, the dashboard — runs in containers on
the server.

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

**Four public hostnames, because the dashboard, the hub and the read API are genuinely three
origins — and the ingest API is a fourth thing that is not an origin at all.**

The dashboard is a Node service; the browser loads the page from it and then opens its WebSocket
*straight at the hub*, not proxied back through Node. One hostname would mean putting the hub behind
the dashboard's path space and reintroducing the proxy hop that arrangement exists to avoid. So:
`race-web.*` to the dashboard, `race-api.*` to the hub, `race-read.*` to the read API, and those two
back-end services name the dashboard's public origin in their allowlists.

`race-ingest.*` is the fourth, and it is different in kind: no browser ever opens it. A collector is
configured with it directly. That is why the ingest API has no origin allowlist and should not grow
one — an `Origin` header is something browsers send and console applications do not, so a list to
check it against would be theatre. The cost is real and stated plainly under Consequences: it is the
one published service without that layer.

The read API is the third because history is not live state and the hub holds none: it has no
database credentials by design, and giving it some to serve one more route would undo the property
that makes it the safe service to expose. See [0001](0001-per-sim-storage.md) for why the read API
is per-simulator and why it is not simply more endpoints on the ingest host.

**The ingest API is published too — and the reason it was not has been discharged, not waived.**

This reverses the original decision, which read: *`Ingest.Api/Auth/ApiKeyFilter.cs` documents its own
key check as a Phase-1 compromise with a non-constant-time comparison and says plainly that it should
not be exposed beyond the LAN. It is therefore absent from the tunnel's ingress.*

That sentence stopped describing the code before this amendment was written. `ApiKeyFilter.cs` no
longer says any of it: the check is one key per collector, held under a label, compared in constant
time over SHA-256 digests with no early exit, behind a chained rate limiter. `compose.test.yml` and
`docs/deployment.md` were corrected when that landed; this file was not, and was for a while the only
place in the repo still asserting the old rationale. Recording that is part of the point — the
reversal is half correction.

The reason the ADR gave was a checklist, so here it is discharged item by item:

- *A non-constant-time comparison.* Gone. `CollectorKeyGate` digests both sides and compares with
  `CryptographicOperations.FixedTimeEquals`, which is the posture the hub always had.
- *One shared key, no per-client identity, no rotation.* Gone. Keys are per collector, and revoking
  one is deleting its labelled line and restarting; the others keep working.
- *No rate limiting.* Gone. A concurrency limiter bounds in-flight decode and database work, and
  token buckets bound arrival rate — chained, because a request here is either one lap result or an
  eight-megabyte batch, and counting requests alone would be the wrong unit.
- *TLS termination.* The tunnel terminates it. This matters more here than anywhere else in the
  topology: the collector key is presented on every request, so its confidentiality is exactly the
  transport's, and the same check is worth nothing over plaintext.
- *The batch body as a denial-of-service surface.* Capped at 8 MB and refused with a 413, checked on
  `Content-Length` and again by lowering the request-body limit so a chunked body is cut off rather
  than buffered. Nothing is decoded before that check.

What is **not** discharged, and is accepted knowingly: the ingest API has no origin allowlist, so it
is the one published service without that layer. See the hostname section above for why adding one
would be theatre rather than defence.

The hub was always the service built for exposure — constant-time key comparison, origin allowlist,
WebSocket keep-alive — and the read API is built for it from the other direction: it holds no key to
leak, writes nothing, and serves only GETs behind its own origin allowlist. All four are published
now.

**That difference is why the read API is a separate process rather than a `MapGet` beside the
existing `MapPost`.** Same database, same table, opposite auth posture — and one service cannot have
two. Publishing both did not change that: one holds keys and accepts writes, the other holds none and
only reads. It was never being on the tunnel that separated them.

**gRPC was considered for the collector→ingest hop, and rejected.**

Both ends are C# sharing `RaceIntelligence.Ingest.Contracts` as a project reference, so the codegen'd
cross-language contracts that are gRPC's main argument buy nothing here — they would replace shared
types with generated ones. Telemetry batches are already binary MessagePack, so the "stop shipping
JSON" win was taken years before the question came up. Against that:

- The live hub cannot be gRPC regardless, because the browser opens a WebSocket straight at it and
  grpc-web needs exactly the proxy hop this ADR exists to avoid. gRPC would mean three transports
  rather than one.
- It has to survive the tunnel. Tunnels and reverse proxies handle end-to-end HTTP/2 with trailers
  inconsistently, and the failure mode is a stream reset mid-race. WebSocket and plain REST are the
  two things every tunnel gets right.
- It needs HTTP/2 end to end, which this deployment does not have. The ingest container serves
  cleartext, where Kestrel offers HTTP/1.1 only, so the collector in this house — the one that exists
  today — could not speak gRPC without also reconfiguring Kestrel for h2c.
- `AddStandardResilienceHandler` applies to every `HttpClient` through `AddServiceDefaults`. gRPC's
  retry configuration is a separate and less capable surface.

Revisit if a non-.NET collector appears. An iRacing or Linux collector in another language is the
case where codegen'd contracts stop being ceremony.

A collector *in the same house* publishes to the hub and the ingest API over the LAN rather than out
through the tunnel and back, which is both lower latency and one less dependency during a race. That
is an optimisation available to one install, not a property of the topology: a collector on another
network necessarily takes the tunnel for both.

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
- **Four kinds of secret, and one of them multiplies.** The original three — the ingest key, the hub
  key and PostgreSQL's password — were already four in practice, because the identity registry has
  its own key that this ADR never counted. And the ingest key is now one *per collector*: a new
  driver means a new labelled entry, so the count is open-ended rather than fixed. They guard things
  with different exposure, so a leak of one must not compromise the others — and now a leak of one
  driver's key must not compromise another's.
- **A second simulator means a second stack.** This is ADR 0001's "N deployments" cost arriving in
  concrete form: another database, another ingest API, another migration bundle. The hub and the
  dashboard are shared and do not multiply.
- **Nothing here applies forwarded headers**, and publishing the ingest API gave that a price. No
  `UseForwardedHeaders`, `UseHttpsRedirection` or `UseHsts` exists anywhere, which is why the
  services sit behind a tunnel without complaint. Request logs show the tunnel's address rather than
  the client's, and any future scheme-dependent code would be wrong behind the proxy. New with this
  amendment: the ingest rate limiter chains a per-remote-address bucket as its backstop against a
  caller rotating fabricated keys, and behind the tunnel every remote collector presents the same
  address — so that bucket degrades from a per-client limit to one aggregate cap. Accepted, because
  the limiters above it still partition per collector and two drivers sit far under the aggregate.
  What is lost is isolation: a flood of fabricated keys shares a bucket with real collectors instead
  of being contained away from them. Trusting `X-Forwarded-For` to recover it was considered and not
  taken — it needs `KnownProxies`/`KnownNetworks` pinned to an address Docker assigns dynamically,
  and a forgeable partition key is worse than an aggregate one.
- **HTTP/2 reaches only the tunnel leg.** The collector's ingest client requests HTTP/2 but accepts
  lower, so a remote collector negotiates h2 over TLS at the edge while a collector on the LAN keeps
  talking HTTP/1.1 to the container's cleartext endpoint. That asymmetry is deliberate: forcing the
  LAN leg would mean demanding an exact version from the client, turning any plaintext or
  proxy-terminated hop into a failure instead of a downgrade.
