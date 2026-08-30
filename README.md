# Race Intelligence Platform

A simulator-agnostic telemetry and race strategy platform for sim racing. It collects raw telemetry
from a racing simulator, stores it permanently, and builds analysis, strategy and eventually machine
learning on top of that history.

The guiding rule: **raw data is permanent, intelligence is replaceable.** Telemetry is never
modified, so any future algorithm can be re-run against the complete history.

---

## Status

**Telemetry collection works end to end** — simulator to database — and a live view of the session
runs alongside it. RaceRoom is the only connector so far, and analysis has just started. The roadmap
beyond that is in [docs/architecture.md](docs/architecture.md).

| Area | State |
|---|---|
| RaceRoom connector | Working — shared memory, 60 Hz, layout-gated across game versions |
| Canonical telemetry model | Working — simulator-independent, capability-based |
| Collector (plugin host) | Working — one poll loop, plugins observe it independently |
| Ingest plugin (buffer + upload) | Working — bounded buffer, batched MessagePack upload |
| Live plugin (publish) | Working — WebSocket publisher, decoupled from the ingest path |
| Ingest API | Working — API-key auth, versioned contracts |
| Live hub | Working — rooms, viewer fan-out, timing tower and lap history projection |
| Dashboard | Working — session list, timing tower, driver focus, comparison, track map |
| PostgreSQL persistence | Working — EF Core + bulk binary-copy writer |
| Analysis | Started — one deterministic model implemented and covered by tests |
| Strategy / ML / AI race engineer | Interfaces only — no implementation behind them yet |

With Docker running, `dotnet test RaceIntelligence.slnx` should come back fully green. The Aspire
and PostgreSQL integration suites need a container runtime and skip themselves when one isn't
available — that's the only reason a test here skips.

---

## How it fits together

```
  Gaming PC                            Home Server                        Browser
  ─────────                            ───────────                        ───────
  RaceRoom
     │  shared memory
     ▼
  Connector ──► Canonical Model ──► Collector
                                        │
                        ingest plugin   ├──► Buffer ──► Upload ──HTTP──► Ingest API
                                        │                                    │
                                        │                                    ▼
                                        │                               PostgreSQL
                                        │                                    │
                                        │                                    ▼
                                        │                    Analysis ─► Strategy ─► ML ─► AI
                                        │
                        live plugin     └──► Publish ──WebSocket──► Live Hub ──WS──► Dashboard
```

The collector runs on the machine with the simulator. It polls the connector once and hands every
sample to the plugins that are enabled: the ingest plugin buffers and uploads for permanent storage,
the live plugin publishes the same samples straight to the hub. Neither path can stall the other —
losing the network does not stop recording, and a full buffer does not stop the live view.

The ingest API, hub and database run on the home server; the dashboard is a separate Node app on its
own origin, and the browser opens its WebSocket directly at the hub. The backend never knows which
simulator produced the data — connectors translate into a canonical model, and the strategy engine
asks a capability system what the data supports rather than branching on the game.

---

## Getting started

You need the .NET 10 SDK (see `global.json`), Node for the dashboard, Windows for the RaceRoom
connector, and Docker Desktop if you want the full local stack.

```powershell
# Everything locally — PostgreSQL, ingest API, live hub, dashboard and collector
dotnet user-secrets set "Parameters:ingest-api-key" "dev-local-only-key" --project src/RaceIntelligence.AppHost
dotnet run --project src/RaceIntelligence.AppHost
```

That one command brings up the whole stack, the dashboard included — AppHost runs it as its own Vite
process and tells it where the hub is, so there is nothing to wire up by hand. Open the Aspire
dashboard it prints, and follow the `dashboard` resource's endpoint to the race engineer's view.

See **[docs/development.md](docs/development.md)** for the other ways to run it — collector only
against a home server, or each service standalone — plus configuration reference and troubleshooting.

```powershell
dotnet build RaceIntelligence.slnx
dotnet test  RaceIntelligence.slnx
```

---

## Repository layout

```
src/
  RaceIntelligence.Core                  Sessions, laps, capabilities, analysis abstractions
  RaceIntelligence.RaceRoom.Telemetry    RaceRoom's telemetry sample, generated from the manifest
  RaceIntelligence.RaceRoom.Channels.Generator  The generator that reads channels/*.channels
  RaceIntelligence.Connectors.RaceRoom   RaceRoom shared-memory connector
  RaceIntelligence.Collector             Poll loop and plugin host (runs on the gaming PC)
  RaceIntelligence.Collector.Abstractions  Plugin and observer interfaces
  RaceIntelligence.Collector.Plugins.Ingest  Buffering and background upload
  RaceIntelligence.Collector.Plugins.Live    WebSocket publishing to the live hub
  RaceIntelligence.Ingest.Api            Telemetry ingest endpoints
  RaceIntelligence.Ingest.Contracts      RaceRoom's collector-to-API wire
  RaceIntelligence.Live.Contracts        RaceRoom's collector-hub-dashboard wires
  RaceIntelligence.Web                   Live hub — rooms, viewer fan-out, tower projection
  RaceIntelligence.Dashboard             TanStack Start dashboard (TypeScript, runs on Node)
  RaceIntelligence.Persistence.Core      Shared EF Core entities and repositories (no schema)
  RaceIntelligence.Persistence.RaceRoom  RaceRoom's tables, migrations and bulk writer
  RaceIntelligence.Analysis              Analysis engine          (started)
  RaceIntelligence.Strategy              Strategy engine          (interfaces only)
  RaceIntelligence.ML                    Model training           (interfaces only)
  RaceIntelligence.AI.RaceEngineer       AI race engineer         (interfaces only)
  RaceIntelligence.AppHost               Aspire orchestration for local development only
  RaceIntelligence.ServiceDefaults       Shared telemetry, health checks, resilience
tests/                                   One test project per component
docs/                                    Architecture and development guides
```

---

## Built with

.NET 10 · Aspire · PostgreSQL · EF Core · MessagePack · WebSockets · Serilog · OpenTelemetry ·
xUnit v3 · Testcontainers · TanStack Start · React · Vite · Vitest

---

## Adding a simulator

**A second simulator needs more than a connector, and that is a decision rather than an oversight.**
It was one connector for a while: the collector posted a canonical model plus a JSON `extras`
document, and each simulator's storage decided what to do with what arrived. That document turned out
to be 68% of the telemetry table, holding twenty-nine channels nothing checked at compile time — so
[ADR 0001](docs/decisions/0001-per-sim-storage.md) was amended and the wires became per-simulator
too, typed end to end.

So a simulator now brings a connector, a channel manifest, an ingest contract, a schema and a read
API — and the manifest generates most of the middle three. The collector's plugin host, the live
hub's own job (rooms, viewers, fan-out) and the dashboard's standard views — timing tower, lap
history, driver focus — stay simulator-agnostic; an accurate `SimCapabilities` set still lights them
up.

What a new simulator *may* want is its own focus panels, for readouts that are genuinely specific in
presentation rather than in data. Those register in `app/sims/registry.ts`, keyed by game, and each
declares the capabilities it needs — see [docs/architecture.md](docs/architecture.md).

---

## Documentation

- **[docs/development.md](docs/development.md)** — running it, configuration, tests, migrations
- **[docs/architecture.md](docs/architecture.md)** — design rationale, data model, roadmap
- **[src/RaceIntelligence.Dashboard/README.md](src/RaceIntelligence.Dashboard/README.md)** — the
  dashboard's own commands, and where it expects the hub to be

---

## License

Released under the [MIT License](LICENSE). Use it, fork it, build connectors for it.

The RaceRoom shared-memory structs under `src/RaceIntelligence.Connectors.RaceRoom/Interop/` are an
independent C# port of the layout published by
[r3e-api](https://github.com/kwstudios-sweden/r3e-api), taken from
[`sample-csharp/src/R3E.cs`](https://github.com/kwstudios-sweden/r3e-api/raw/refs/heads/master/sample-csharp/src/R3E.cs);
see those files and [docs/development.md](docs/development.md) for details.
