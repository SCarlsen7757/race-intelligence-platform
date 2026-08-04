# Race Intelligence Platform

A simulator-agnostic telemetry and race strategy platform for sim racing. It collects raw telemetry
from a racing simulator, stores it permanently, and builds analysis, strategy and eventually machine
learning on top of that history.

The guiding rule: **raw data is permanent, intelligence is replaceable.** Telemetry is never
modified, so any future algorithm can be re-run against the complete history.

---

## Status

**Phase 1 — telemetry collection — works end to end.** RaceRoom is the only connector so far.

| Area | State |
|---|---|
| RaceRoom connector | Working — shared memory, 60 Hz, version-gated |
| Canonical telemetry model | Working — simulator-independent, capability-based |
| Collector (buffer + upload) | Working — bounded buffer, batched MessagePack upload |
| Ingest API | Working — API-key auth, versioned contracts |
| PostgreSQL persistence | Working — EF Core + bulk binary-copy writer |
| Analysis / Strategy / ML / AI | Interface scaffolds only — Phases 2-5 |

161 tests. Roughly 30 of them need a container runtime and skip without one.

---

## How it fits together

```
  Gaming PC                              Home Server
  ─────────                              ───────────
  RaceRoom
     │  shared memory
     ▼
  Connector ──► Canonical Model ──► Buffer ──► Upload ──HTTP──► Ingest API
                                                                    │
                                                                    ▼
                                                               PostgreSQL
                                                                    │
                                                                    ▼
                                                    Analysis ─► Strategy ─► ML ─► AI
```

The collector runs on the machine with the simulator. The ingest API and database run on the home
server. The backend never knows which simulator produced the data — connectors translate into a
canonical model, and the strategy engine asks a capability system what the data supports rather than
branching on the game.

---

## Getting started

You need the .NET 10 SDK (see `global.json`), Windows for the RaceRoom connector, and Docker Desktop
if you want the full local stack.

```powershell
# Everything locally — PostgreSQL, ingest API and collector
dotnet user-secrets set "Parameters:ingest-api-key" "dev-local-only-key" --project src/RaceIntelligence.AppHost
dotnet run --project src/RaceIntelligence.AppHost
```

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
  RaceIntelligence.Core                  Canonical telemetry model, capabilities, sessions
  RaceIntelligence.Connectors.RaceRoom   RaceRoom shared-memory connector
  RaceIntelligence.Collector             Buffering and background upload (runs on the gaming PC)
  RaceIntelligence.Ingest.Api            Telemetry ingest endpoints
  RaceIntelligence.Ingest.Contracts      Wire contracts shared by collector and API
  RaceIntelligence.Persistence           EF Core entities, migrations, bulk writer
  RaceIntelligence.Analysis              Analysis engine          (Phase 2)
  RaceIntelligence.Strategy              Strategy engine          (Phase 3)
  RaceIntelligence.ML                    Model training           (Phase 4)
  RaceIntelligence.AI.RaceEngineer       AI race engineer         (Phase 5)
  RaceIntelligence.AppHost               Aspire orchestration for local development only
  RaceIntelligence.ServiceDefaults       Shared telemetry, health checks, resilience
tests/                                   One test project per component
docs/                                    Architecture and development guides
```

---

## Built with

.NET 10 · Aspire · PostgreSQL · EF Core · MessagePack · Serilog · OpenTelemetry · xUnit v3 ·
Testcontainers

---

## Adding a simulator

Every simulator needs only a connector. Implement `ITelemetrySource`, translate the game's telemetry
into the canonical model, and declare what the game exposes through `SimCapabilities`. No backend
changes should be required — see [docs/architecture.md](docs/architecture.md).

---

## Documentation

- **[docs/development.md](docs/development.md)** — running it, configuration, tests, migrations
- **[docs/architecture.md](docs/architecture.md)** — design rationale, data model, roadmap

---

## License

Released under the [MIT License](LICENSE). Use it, fork it, build connectors for it.

The RaceRoom shared-memory structs under `src/RaceIntelligence.Connectors.RaceRoom/Interop/` are an
independent C# port of the layout published by
[r3e-api](https://github.com/kwstudios-sweden/r3e-api); see those files for details.
