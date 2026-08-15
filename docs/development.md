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

---

## Option A — the whole stack locally (Aspire)

Runs PostgreSQL, the ingest API and the collector together on this one machine. Use this when you're
developing the pipeline itself and want to see telemetry land in a database.

```powershell
# One-time: set the shared secret the collector and API both use.
dotnet user-secrets set "Parameters:ingest-api-key" "dev-local-only-key" --project src/RaceIntelligence.AppHost

# One-time: fix the local Postgres password so it doesn't regenerate on every run — otherwise any
# external tool (DataGrip, psql, ...) has to be reconfigured each time you restart AppHost.
dotnet user-secrets set "Parameters:postgres-password" "dev-local-only-password" --project src/RaceIntelligence.AppHost

dotnet run --project src/RaceIntelligence.AppHost
```

The Aspire dashboard opens in a browser with all three resources. PostgreSQL runs in a container
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
$env:Collector__IngestBaseUrl = "https://home-server:5443/"
$env:Collector__ApiKey        = "<the server's Ingest__ApiKey value>"
dotnet run --project src/RaceIntelligence.Collector
```

`IngestBaseUrl` **must end in a trailing slash** or the relative request paths won't combine
correctly with `HttpClient.BaseAddress`.

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
dotnet run --project src/RaceIntelligence.Ingest.Api --launch-profile https
```

Any other PostgreSQL works too — adjust host, port and password to match it.

Then run the collector as in Option B. The API key lines up on its own — the API's
`appsettings.Development.json` sets `Ingest:ApiKey` to `dev-local-only-key` and the collector's sets
the same key — but the base URL does not. Under Aspire the collector resolves the API through
service discovery (`https+http://ingest-api/`, injected by AppHost), so no port is baked into
`appsettings.Development.json`. Running without Aspire there is nothing to resolve, so point the
collector at the API's `https` launch profile URL yourself — see
`src/RaceIntelligence.Ingest.Api/Properties/launchSettings.json` for the port:

```powershell
$env:Collector__IngestBaseUrl = "https://localhost:<https-port-from-launchSettings>/"
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

| Setting | Default | Notes |
|---|---|---|
| `IngestBaseUrl` | `https://localhost:5443/` | Trailing slash required |
| `ApiKey` | *(empty)* | Required. Sent as the `X-Api-Key` header |
| `PollInterval` | `00:00:00.0166667` | 60 Hz |
| `BufferCapacity` | `20000` | Samples held before `BufferFullMode` applies |
| `BufferFullMode` | `Wait` | `Wait` or `DropWrite` |
| `MaxBatchSize` | `500` | Flush trigger by size |
| `MaxBatchAge` | `00:00:02` | Flush trigger by age |

Override any of them with `Collector__<Name>` as an environment variable, or via user secrets on the
gaming PC. **Don't put a real API key in `appsettings.json`.**

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
dotnet ef migrations add <Name> --project src/RaceIntelligence.Persistence
dotnet ef migrations script --project src/RaceIntelligence.Persistence --output docs/schema.sql
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
