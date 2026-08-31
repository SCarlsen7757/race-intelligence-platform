# Running the platform in development

This describes running the platform on the **gaming PC** — the machine RaceRoom is installed on.
That's the only machine where the collector can do anything useful, because the RaceRoom connector
reads a Windows named shared-memory block that only exists while the game is running.

## Prerequisites

| | Needed for |
|---|---|
| .NET SDK 10.0.302 (see `global.json`) | everything |
| Windows | the RaceRoom connector — it is marked `[SupportedOSPlatform("windows")]` |
| RaceRoom, running | actual telemetry. The collector starts fine without it and waits |
| Docker Desktop | Options A and C below, and the full test suite. Not needed for Option B |
| Node 24 | the dashboard — its own service now, so also Option A, which launches it |

---

## Option A — the whole stack locally (Aspire)

Runs PostgreSQL, the ingest API, the identity registry and the collector together on this one
machine. Use this when you're developing the pipeline itself and want to see telemetry land in a
database.

```powershell
# One-time: set the shared secret the collector and API both use.
dotnet user-secrets set "Parameters:ingest-api-key" "dev-local-only-key" --project src/RaceIntelligence.AppHost

# One-time: the secret the collector presents to the live hub when publishing. A separate key from
# the ingest one on purpose — the two guard services with different exposure, and the hub is the one
# meant to be reachable through a tunnel.
dotnet user-secrets set "Parameters:live-api-key" "dev-local-only-key" --project src/RaceIntelligence.AppHost

# One-time: the secret guarding the cross-simulator identity registry. A third key, on the same
# argument as the second: it guards the only hand-curated state in the platform, which is a reason
# to keep it apart rather than to economise on keys.
dotnet user-secrets set "Parameters:identity-api-key" "dev-local-only-key" --project src/RaceIntelligence.AppHost

# One-time: fix the local Postgres password so it doesn't regenerate on every run — otherwise any
# external tool (DataGrip, psql, ...) has to be reconfigured each time you restart AppHost.
dotnet user-secrets set "Parameters:postgres-password" "dev-local-only-password" --project src/RaceIntelligence.AppHost

dotnet run --project src/RaceIntelligence.AppHost
```

The Aspire dashboard opens in a browser with every resource, the race-engineer dashboard among
them. The collector publishes live as well as archiving here — AppHost sets
`Collector__Live__Enabled`, unlike the shipped default — because the point of the full graph is to
exercise the whole pipeline.

AppHost also wires the two halves of the two-origin split, so neither needs configuring by hand: the
dashboard is given the hub's endpoint as `HUB_URL`, and the hub is given the dashboard's origin as
`Live__AllowedOrigins__0`. Node must be on `PATH`; Aspire runs `npm install` and `npm run dev` for
the dashboard resource.

The hub's own room list is readable at `<web>/api/v1/live/rooms` for a curl-level check.

PostgreSQL runs in a container
with a persistent data volume, so telemetry survives restarts — deliberately, since losing a test
session's data on every restart would make the "raw data is permanent" behaviour impossible to
exercise. It's also reachable on a fixed host port (`55432`, chosen to avoid colliding with a
locally installed Postgres on the default `5432`) with the fixed password set above, so an external
tool's connection settings — host `localhost`, port `55432`, user `postgres`, database
`raceintel` — stay valid across restarts too.

**Database migrations apply automatically here.** That only happens in Development
(`Program.cs`); production applies them out-of-band as an explicit step.

## Option B — collector only, against the home server

Once the ingest API and PostgreSQL live on the home server, this is the normal day-to-day mode on
the gaming PC. No Docker needed here.

```powershell
dotnet run --project src/RaceIntelligence.Collector
```

Point it at the server by overriding two settings — environment variables are easiest:

```powershell
$env:Collector__Ingest__BaseUrl = "https://home-server:5443/"
$env:Collector__Ingest__ApiKey  = "<this collector's key from the server's Ingest__ApiKeys__* set>"
dotnet run --project src/RaceIntelligence.Collector
```

`Ingest:BaseUrl` **must end in a trailing slash** or the relative request paths won't combine
correctly with `HttpClient.BaseAddress`.

To also publish a live view for a race engineer to watch, add the hub and pass `--live`:

```powershell
$env:Collector__Live__BaseUrl = "https://home-server:5444/"
$env:Collector__Live__ApiKey  = "<the hub's publish key>"
dotnet run --project src/RaceIntelligence.Collector -- --live
```

## Option C — API and collector separately, no Aspire

Occasionally useful for debugging one service in isolation. Two things are missing without Aspire:
a database, and the connection string pointing at it.

Nothing in this repo starts a bare PostgreSQL for you, so use the one AppHost creates — start
AppHost once as in Option A and leave its container running, then stop the AppHost-launched API and
run it yourself. That container listens on `55432` with the fixed password you set in Option A.

Without a connection string the API throws `Connection string 'raceintel' is not configured.` at
startup, so supply one:

```powershell
$env:ConnectionStrings__raceintel = "Host=localhost;Port=55432;Database=raceintel;Username=postgres;Password=dev-local-only-password"

# Which simulator this database holds. Required: one database per simulator (ADR 0001), so the API
# refuses to start without it and refuses any session claiming a different simulator.
$env:Ingest__GameKey = "raceroom"
dotnet run --project src/RaceIntelligence.Ingest.RaceRoom --launch-profile https
```

Any other PostgreSQL works too — adjust host, port and password to match it.

Then run the collector as in Option B. The API key lines up on its own — the API's
`appsettings.Development.json` sets `Ingest:ApiKeys:dev-collector` to `dev-local-only-key` and the
collector's sets the same key — but the base URL does not. Under Aspire the collector resolves the API through
service discovery (`https+http://ingest-api/`, injected by AppHost), so no port is baked into
`appsettings.Development.json`. Running without Aspire there is nothing to resolve, so point the
collector at the API's `https` launch profile URL yourself — see
`src/RaceIntelligence.Ingest.RaceRoom/Properties/launchSettings.json` for the port:

```powershell
$env:Collector__Ingest__BaseUrl = "https://localhost:<https-port-from-launchSettings>/"
```

---

## What you should see

The connector runs a small state machine, and the console is where you observe it:

```
Disconnected  →  WaitingForSimulator  →  Connected  →  InSession
```

- **WaitingForSimulator** — polling for the `RRRE64` (or `RRRE`) process. Normal before you launch
  the game.
- **Connected** — shared memory opened and its layout accepted. The connector requires API major
  version 3 and refuses anything else rather than risk misreading every field. Within major 3 it
  checks the block's layout rather than its minor version — see
  [The RaceRoom shared-memory layout](#the-raceroom-shared-memory-layout) below.
- **InSession** — you left the menus into a recognized session type. Telemetry is flowing at 60 Hz.

A session ends when you return to the menus, pass the checkered flag, change track/layout/session
type, or restart the session in-game.

## The RaceRoom shared-memory layout

The structs under `src/RaceIntelligence.Connectors.RaceRoom/Interop/` are a hand-written C# port of
the layout RaceRoom publishes in its `$R3E` shared-memory block. The source of record is one file in
the official [r3e-api](https://github.com/kwstudios-sweden/r3e-api) repository:

```
https://github.com/kwstudios-sweden/r3e-api/raw/refs/heads/master/sample-csharp/src/R3E.cs
```

It is vendored by hand — nothing generates or downloads it at build time — and it is cross-checked
against the C header the same repo publishes (`sample-c/src/r3e.h`). Both currently declare API
version **3.5**.

### Why the connector doesn't gate on the version number

Upstream ships no changelog saying which minor version moved which field, so "accept minor ≥ 5"
would be a guess in both directions: it refuses older builds whose layout is in fact identical over
the bytes we read, and it waves through a newer build that inserted a field ahead of one we read —
the case that silently corrupts every value rather than failing.

So the gate is **structural**. The block describes its own layout in its header, and
`R3EVersionGate` compares that description against the compiled structs:

| Header field | Compared against | Catches |
|---|---|---|
| `all_drivers_offset` | offset of `num_cars` (2008) | any field added, removed or resized ahead of the driver array |
| `driver_data_size` | `sizeof(R3EDriverData)` (328) | growth *inside* the driver array, which the offset alone can't see |

The result:

- **Any major-3 build whose layout matches is accepted**, whatever minor it reports — one
  transcription covers many game versions, including ones older than 3.5.
- **A build whose layout has moved is refused**, even if its minor looks new enough, with both
  numbers in the message: `reports its driver data at byte offset 2040, but this connector's
  transcription puts it at 2008`.
- **Major 4 is refused outright.** Upstream reserves majors for exactly the incompatible reshuffle a
  prefix check cannot absorb.

The reported major/minor is still recorded on every session, so data collected under different game
versions stays distinguishable later.

### Re-syncing after a RaceRoom update

If the connector starts refusing to connect with a layout-mismatch message, RaceRoom changed the
block:

1. Diff the current `R3E.cs` (URL above) against `Interop/` field by field.
2. Update the structs, keeping every `*Unused*` reserved field — deleting one shifts everything after it.
3. Update the hand-computed offsets in `R3ESharedRawLayoutTests` to match, and the pinned values in
   `R3EVersionGateTests.ExpectedLayout_MatchesTheHandComputedOffsetsFromR3ECs`.

Those layout tests are the only automated defense against a transcription error — they compute
offsets by hand from the published header and compare against what the CLR actually lays out, so a
wrong field type or position fails the build rather than the race.

---

## Stopping it

**Use Ctrl+C. Don't close the console window.** The upload service does a best-effort final flush of
the open batch on graceful shutdown; closing the window gives Windows only a few seconds before it
kills the process, and the buffer is in memory, so an in-flight batch can be lost.

Also worth knowing: a batch that exhausts the HTTP retry policy is logged at `Error` and
**discarded**, not re-queued — deliberately, to avoid reordering it behind newer samples. Those
`Error` lines are the only signal that telemetry went missing, so glance at the console after a
session.

## Configuration reference

Collector settings bind from the `Collector` section. They're validated on startup, so a bad value
fails immediately rather than silently dropping telemetry mid-race.

The collector itself only reads the simulator. Everything that *sends* that data anywhere is a
**plugin**, configured in its own block under `Collector` and switched on independently:

- **Ingest** — archives telemetry to the ingest API for permanent storage. On by default.
- **Live** — publishes a live view to the dashboard hub for a race engineer to watch. Off by
  default, because it sends this machine's session somewhere other people can see it.

Enabling neither fails at startup: the collector would read the simulator and do nothing with it.

Each plugin validates only its own block, and only when it is switched on — so a publish-only
collector is never asked for an ingest API key it will never send. Plugins are also isolated from
each other at run time: an ingest API outage cannot stop the live view, and an unreachable hub
cannot stop archiving.

The two are deliberately *not* built on a shared sink interface. The archive path is buffered,
ordered and retried, and a sample it drops is gone for good; the live path is conflating and
newest-wins, and a frame it keeps instead of dropping is a stale frame shown to a race engineer as
current. One interface spanning both would force one path into the other's failure mode.

| Setting | Default | Notes |
|---|---|---|
| `PollInterval` | `00:00:00.0166667` | 60 Hz. Feeds both jobs |
| `Ingest:Enabled` | `true` | Archive to the ingest API |
| `Ingest:BaseUrl` | `https://localhost:5443/` | Trailing slash required |
| `Ingest:ApiKey` | *(empty)* | Required when enabled. Sent as `X-Api-Key`. Must be one of the ingest API's configured `Ingest:ApiKeys` values |
| `Ingest:BufferCapacity` | `20000` | Samples held before `BufferFullMode` applies |
| `Ingest:BufferFullMode` | `Wait` | `Wait` or `DropWrite` |
| `Ingest:MaxBatchSize` | `500` | Flush trigger by size |
| `Ingest:MaxBatchAge` | `00:00:02` | Flush trigger by age |
| `Live:Enabled` | `false` | Publish to the dashboard hub |
| `Live:BaseUrl` | `https://localhost:5444/` | `http`/`https`, not `ws` — the scheme is switched when the socket opens |
| `Live:ApiKey` | *(empty)* | Required when enabled. Viewing the dashboard is open; publishing is not |
| `Live:ClientId` | *(generated)* | Set once per machine so a restart isn't a new client |
| `Live:ClientName` | *(machine name)* | Label shown in the dashboard's client list |
| `Live:StandingsInterval` | `00:00:00.100` | 10 Hz timing tower. Must not be shorter than `PollInterval` |
| `Live:ExtrasInterval` | `00:00:01` | 1 Hz simulator-specific document — car damage and the rest. Must not be shorter than `PollInterval` |
| `Live:ReconnectDelay` | `00:00:01` | First backoff after the socket drops |
| `Live:MaxReconnectDelay` | `00:00:30` | Backoff ceiling |

Override any of them with `Collector__<Block>__<Name>` as an environment variable (e.g.
`Collector__Live__ApiKey`), or via user secrets on the gaming PC. **Don't put a real API key in
`appsettings.json`.**

### Adding a second collector

The ingest API holds a set of collector keys, not one shared secret: `Ingest:ApiKeys` maps a label
to a key. Give a new driver their own entry rather than a copy of yours —

```yaml
Ingest__ApiKeys__local-collector: <your key>
Ingest__ApiKeys__friend-gaming-pc: <their key>
```

— and set that key as their collector's `Collector:Ingest:ApiKey`. The label is what shows up in
the ingest API's logs, so name it after the machine or the person.

Revoking one collector is deleting its line and restarting the ingest API; the other keys keep
working. Rotation works the same way, and both take a restart on each side: the server digests its
keys once at startup, and the collector bakes its key into the outgoing request headers once at
registration.

At least one key is required. An ingest API with none configured refuses to start, rather than
rejecting every upload with a 401 that looks like a collector bug.

Either job can also be switched from the command line, which is the quickest way to change your
mind for one run:

```powershell
dotnet run --project src/RaceIntelligence.Collector -- --live             # archive and publish
dotnet run --project src/RaceIntelligence.Collector -- --live --no-ingest # publish only
```

`--live`, `--no-live`, `--ingest` and `--no-ingest` are shorthands for the corresponding
`Collector:<Plugin>:Enabled` key. `--plugin <id>` and `--no-plugin <id>` do the same for any plugin
by name, including ones added later that never get a shorthand of their own:

```powershell
dotnet run --project src/RaceIntelligence.Collector -- --plugin Live --no-plugin Ingest
```

Anything without a shorthand still takes the long form —
`--Collector:Live:StandingsInterval 00:00:00.2`.

With `Live:Enabled` off, nothing consumes standings or extras, and the connector is told neither to
read the simulator's driver array nor to publish the extras document — so a collector that isn't
publishing pays nothing for either feature. Both decisions are made from the registered plugins
rather than by naming the live plugin, so a future plugin that wants standings or extras gets them
without a change to the collector.

`Live:ExtrasInterval` is deliberately much slower than the poll rate. The document is written for
every sample anyway — the archive path stores it per row — so publishing it costs nothing extra;
what the interval limits is how often the dashboard parses JSON to read a damage value that changes
when someone hits a wall. Turn it down if you want a more responsive damage panel and can spare the
parsing; there is no reason to take it below `Live:StandingsInterval`.

### Live hub settings

The hub (`RaceIntelligence.Web`) binds its own settings from the `Live` section.

| Setting | Default | Notes |
|---|---|---|
| `ApiKey` | *(empty)* | **Required.** The key a collector must present to publish. Startup fails without it |
| `AllowedOrigins` | *(empty)* | **Required, and must list at least one origin.** The browser origins allowed to read the room list and open a viewing socket. Startup fails without it |
| `RoomExpiry` | `00:00:30` | How long a session survives with no frames before the hub forgets it |
| `RoomSweepInterval` | `00:00:05` | How often expired rooms are swept |
| `MaxPublisherMessageBytes` | `524288` | Largest frame accepted from a collector |

`AllowedOrigins` takes origins, not URLs — scheme, host and port with no trailing slash, exactly as
a browser sends them:

```json
{ "Live": { "AllowedOrigins": [ "http://localhost:3000" ] } }
```

It drives two mechanisms that a browser applies at different moments: a CORS policy on
`GET /api/v1/live/rooms`, and `WebSocketOptions.AllowedOrigins` on the two sockets. **The hub
refuses to start when the list is empty, because an empty list means "accept every origin"** — the
behaviour before the dashboard moved off this host, and one that looks configured while being open
to any page on the internet.

Only browsers are constrained by it. A collector connects with a raw `ClientWebSocket` and sends no
`Origin` header, which is still accepted: origin is a browser's self-report about which page opened
the connection, and it means nothing coming from a program. Publishing is guarded by the API key
instead.

Two endpoints, with deliberately opposite auth:

| Endpoint | Auth | Purpose |
|---|---|---|
| `GET /live/publish` (WebSocket) | `X-Api-Key` | A collector publishes its session |
| `GET /live/view` (WebSocket) | **none**, origin-checked | A race engineer watches |
| `GET /api/v1/live/rooms` | **none**, CORS | The room list as JSON, identical to what the socket sends |

Publishing is gated and viewing is not because the risks are opposite: a viewer can only read what
someone chose to publish, while a publisher injects the data every race engineer is making decisions
from — and a fabricated timing tower looks exactly like a real one.

Anything that is neither an API route nor a socket is a plain 404. **The hub serves no UI** — see
below.

`RoomExpiry` is measured from the **last frame**, not from the last publisher disconnecting. That
gap is what lets a collector whose socket drops mid-race rejoin the room it was already in, keeping
its room id, its accumulated lap history and every viewer's subscription intact across the reconnect.

### What a viewer can say, and what it is sent

Commands are JSON with a `type` discriminator, sent on `/live/view`:

| Command | Payload | Effect |
|---|---|---|
| `watchRoom` | `roomId` or `null` | Subscribe to a room's tower. `null` leaves it |
| `focusDriver` | `driverKey` or `null` | Follow one driver's full-rate channels. At most one at a time |
| `subscribeLapHistory` | `driverKey` or `null` | Add a driver to the lap-history set. `null` drops them all |
| `unsubscribeLapHistory` | `driverKey` | Remove one driver from that set |

Lap history is a **set**, unlike focus, because several rows can be expanded at once and each costs
about one message a minute where a focus stream costs sixty a second. It also works for any driver
in the session, not only one whose own machine is publishing: it is accumulated from the standings
snapshot, which sees every car. Subscribing is answered immediately with what the hub already holds
rather than at the driver's next lap.

The hub keeps no viewer memory across connections, so a reconnecting dashboard replays its
`watchRoom`, `focusDriver` and `subscribeLapHistory` commands.

Messages back carry the same discriminator: `roomList`, `sessionState`, `towerSnapshot`,
`focusFrame`, `lapHistory`, `extrasFrame`, `error`. Durations are **milliseconds as JSON numbers**,
never `TimeSpan` strings, and nulls are omitted — a value the simulator did not report must never
arrive as `0`.

`sessionState` is what is true of the session rather than of any car in it: the layout's length in
metres, from which the dashboard derives each lap's average speed, and the mandatory pit window. It
is sent **once on watching a room, and again only when it changes** — standings arrive at 10 Hz and
this moves perhaps twice in a race, so broadcasting it per frame would turn a low-rate message into
the second-busiest one on the wire. The unprompted send on subscribing is the other half: a viewer
joining a race whose window opened ten minutes ago has no change left to be told about.

The window's `status` and `unit` cross as **names** (`"Open"`, `"Laps"`) where the per-car pit fields
cross as ints, because this message is cheap enough to spell out and a name cannot drift out of step
with the server the way a hand-maintained table of codes does. Its bounds are already
sentinel-free — the connector turns RaceRoom's `-1` into null, so a banner cannot announce a window
opening on lap −1 — and `unit` must be honoured rather than guessed: the same `25` is lap 25 in a lap
race and the 25-minute mark in a timed one.

`extrasFrame` carries the simulator's own document as an opaque JSON **string**; the hub never parses
it. For RaceRoom that is where car damage lives (`damage.engine`, `damage.transmission`,
`damage.aerodynamics`, `damage.suspension`). **Treat `-1` as unavailable, not as zero** — extras are
written raw and the sentinel is not translated anywhere upstream, so a panel rendering `-1` as
undamaged says the car is fine when the truth is that nobody knows.

Publishers declare a `LiveSchemaVersion` in their hello and the hub refuses a version it does not
speak, naming both. **It is pinned at 1 and does not move before the first release tag** — nothing
is deployed, so the wire shape changes in place instead of climbing a version ladder. The handshake
stays anyway, because it turns a stale process left over from an earlier build into a refusal that
names both versions rather than a decode error mid-race. See the versioning rule in `CLAUDE.md`.

---

## The dashboard

The dashboard lives in `src/RaceIntelligence.Dashboard/` — a [TanStack Start](https://tanstack.com/start)
app on Node, **its own service on its own origin**. The hub no longer serves it, and the browser
opens its WebSocket straight at the hub rather than being proxied through Node.

Direct rather than proxied because it is both the more modular arrangement and the lower-latency
one: a proxy adds a second connection and forces Node's event loop to re-emit every focus frame
sixty times a second, which is jitter as well as delay.

That costs two things, and both are settings rather than assumptions:

- the dashboard has to be told where the hub is — **`HUB_URL`**;
- the hub has to be told which origin to accept — **`Live:AllowedOrigins`**, above.

Room and driver are in the URL (`/`, `/rooms/$roomId`, `/rooms/$roomId/$driverKey`), so a refresh
or a link restores the view.

### TypeScript is held at 6, deliberately

`typescript` is the one dashboard dependency not on its latest version, and it is pinned rather than
merely stale. TypeScript 7 is the native compiler rewrite, and `typescript-eslint` refuses to load
against it outright:

```
typescript-eslint does not support TS 7.0.
```

`tsc`, `vitest` and `vite build` are all perfectly happy on 7 — it is only linting that stops — so
the failure appears late, in CI, after everything else has gone green. Tracking issue:
[typescript-eslint#10940](https://github.com/typescript-eslint/typescript-eslint/issues/10940).

Raise it when typescript-eslint ships TS 7 support, and check `npm run lint` and not just
`npm run typecheck` when you do. The documented alternative — installing TypeScript 6 side by side
purely to give typescript-eslint an API to read — buys nothing here and costs a second compiler in
the tree.

### Working on it

Under AppHost (Option A) neither setting needs touching: it launches the dashboard as a resource
and wires both directions itself. Standalone, it is two terminals:

```powershell
# Terminal 1 — the hub. Its development settings already allow http://localhost:3000.
dotnet run --project src/RaceIntelligence.Web --launch-profile http

# Terminal 2 — the dashboard, with hot reload.
cd src/RaceIntelligence.Dashboard
npm install
npm run dev      # http://localhost:3000
```

Both defaults line up on purpose: the dashboard falls back to `http://localhost:5044`, which is the
hub's `http` launch profile, and the hub's `appsettings.Development.json` allows
`http://localhost:3000`, which is the dashboard's default port. Point it elsewhere with `HUB_URL`:

```powershell
$env:HUB_URL = "https://home-server:5444"
npm run dev
```

**`HUB_URL` and `READ_URL` are read at run time in a deployment, and at config time in dev.** A
built dashboard is told both origins by `server.mjs`, which injects them into the document — so the
image is deployment-agnostic and repointing it is a restart. `npm run dev` runs Vite's dev server
rather than `server.mjs`, so nothing injects anything and the value Vite substituted at config time
is what dev uses. That is why AppHost's `HUB_URL` reaches a dev session with no configuration. See
`app/shared/config/runtimeConfig.ts`.

| Command | Purpose |
|---|---|
| `npm run dev` | Vite dev server with hot reload |
| `npm run build` | Typecheck, then build the client and server bundles into `dist/` |
| `npm start` | Run the built server |
| `npm run typecheck` | Types only |
| `npm run lint` | ESLint, then a Prettier formatting check |
| `npm run format` | Rewrite files to Prettier's formatting |
| `npm test` | Vitest |

`dist/` and `node_modules/` are generated and gitignored. **`dotnet publish` no longer builds the
dashboard** — it is deployed as a Node service of its own, so `npm ci && npm run build && npm start`
on the host that serves it, or a container built from the same three commands
(`src/RaceIntelligence.Dashboard/Dockerfile`; see `docs/deployment.md`).

`npm start` runs **`server.mjs`**, not the build output directly. `vite build` emits
`dist/server/server.js`, but that file is not a server — it exports a `fetch` handler and nothing
else, so running it starts no listener and Node exits 0 immediately, which looks like success.
`server.mjs` is the entry that hosts it: it serves the handler with `srvx` and serves the hashed
bundles in `dist/client/` in front of it, since the SSR handler renders references to those files
but does not serve them. It reads `PORT`, `HOST`, `HUB_URL` and `READ_URL` at run time, and refuses
to start without the last two.

`app/routeTree.gen.ts` is generated from `app/routes/` by the Vite plugin and **is** tracked, so
`npm run typecheck` works without building first. Adding or renaming a route means committing the
regenerated file; CI checks that it is current.

### How it stays fast

The focus stream runs at the collector's full poll rate, and **that data never goes through React
state**. The socket writes into a plain store — plain fields and `Float32Array` ring buffers — and
the focus panel reads it from `requestAnimationFrame` loops that paint the pedal bars to canvas and
the trace through uPlot. React state holds only the slow-changing half: the room list, the tower,
lap history, extras, errors. A `setState` per focus frame would mean a render cycle 60 times a
second, which drops frames on a laptop well before it does on a desktop.

Damage has its own channel at roughly 1 Hz precisely so it can go through React like the rest: it
changes on contact, not per frame, and parsing its JSON sixty times a second would be pure waste.

### Testing without RaceRoom

The dashboard needs a publisher, not a simulator. Anything that speaks the live contracts works —
the collector itself with `--live`, or a small harness posting synthetic frames. That is how the
tower and focus panel were exercised on a machine with no game installed.

---

## Tests

```powershell
dotnet test RaceIntelligence.slnx
```

With Docker running the whole suite should pass, with nothing skipped.

Without a container runtime the Aspire and PostgreSQL integration suites detect its absence and
**skip** rather than fail. A run with skips but no failures is therefore still healthy — it just
means those suites didn't execute. That is the only thing that causes a skip in this repo, so a
skip you can't explain by Docker being down is worth investigating.

## Adding a database migration

```powershell
dotnet ef migrations add <Name> --project src/RaceIntelligence.Persistence.RaceRoom
dotnet ef migrations script --project src/RaceIntelligence.Persistence.RaceRoom --output docs/raceroom-schema.sql
```

A design-time factory (`RaceIntelligenceDbContextFactory`) exists so this works without starting the
API.

The second command matters. `docs/schema.sql` is a generated DDL dump of the migrations, committed
so the schema is reviewable as a schema rather than inferred from entity classes and fluent
configuration spread across a dozen files — a migration shows up in review as the table it actually
creates. It is also what puts the tables and their foreign keys into the knowledge graph, which is
why the graph needs no database running. Regenerate it in the same commit as the migration, or both
the file and the graph keep describing the previous schema.

## Git hooks

The hooks live in `.githooks/` and are tracked in the repository, not in `.git/hooks` — that
directory is not version-controlled, so anything there drifts per machine and never appears in
review. `Directory.Build.props` points `core.hooksPath` at `.githooks` on the first build, so a
fresh clone is configured by `dotnet build` rather than by a setup step someone has to remember.

`post-commit` and `post-checkout` refresh the graphify knowledge graph in the background (a full
update takes several seconds and a commit should not wait for it). Output goes to
`graphify-out/hook.log`. Neither hook can fail a commit — every path exits 0 — and both skip during
rebase, merge and cherry-pick. Set `GRAPHIFY_SKIP_HOOK=1` to bypass them.

To check or change what git is using:

```powershell
git config --get core.hooksPath      # expect: .githooks
```

## Health endpoints

`/health` and `/alive` are mapped **only in Development** — the stock Aspire guard, since exposing
check details publicly has security implications. Don't expect them to answer on a production
deployment until that's deliberately changed.
