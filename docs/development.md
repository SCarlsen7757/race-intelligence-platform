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
| Docker Desktop | **only** for Option A below, and for the full test suite |

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

Occasionally useful for debugging one service in isolation. The catch: the ingest API gets its
connection string from Aspire, so standalone it will throw
`Connection string 'raceintel' is not configured.` at startup. Supply one yourself:

```powershell
$env:ConnectionStrings__raceintel = "Host=localhost;Database=raceintel;Username=postgres;Password=postgres"
dotnet run --project src/RaceIntelligence.Ingest.Api --launch-profile https
```

Then run the collector as in Option B. The development defaults already line up — the API's
`appsettings.Development.json` sets `Ingest:ApiKey` to `dev-local-only-key`, the collector's sets the
same key and points at `https://localhost:7038/`, which is the API's `https` launch profile. So the
two talk to each other with no extra configuration.

---

## What you should see

The connector runs a small state machine, and the console is where you observe it:

```
Disconnected  →  WaitingForSimulator  →  Connected  →  InSession
```

- **WaitingForSimulator** — polling for the `RRRE64` (or `RRRE`) process. Normal before you launch
  the game.
- **Connected** — shared memory opened and its version accepted. The connector requires API major
  version 3 and refuses anything else rather than risk misreading every field.
- **InSession** — you left the menus into a recognized session type. Telemetry is flowing at 60 Hz.

A session ends when you return to the menus, pass the checkered flag, change track/layout/session
type, or restart the session in-game.

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

Without Docker running, roughly 30 of 161 tests **skip** rather than fail — the Aspire and
PostgreSQL integration suites detect the missing container runtime and opt out. A run reporting
`131 passed, 30 skipped` is a healthy run, not a broken one.

## Adding a database migration

```powershell
dotnet ef migrations add <Name> --project src/RaceIntelligence.Persistence
```

A design-time factory (`RaceIntelligenceDbContextFactory`) exists so this works without starting the
API.

## Health endpoints

`/health` and `/alive` are mapped **only in Development** — the stock Aspire guard, since exposing
check details publicly has security implications. Don't expect them to answer on a production
deployment until that's deliberately changed.
