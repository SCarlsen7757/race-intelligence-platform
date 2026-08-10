# Race Intelligence Platform

A simulator-agnostic telemetry and race strategy platform for sim racing. It collects raw telemetry
from a racing simulator, stores it permanently, and builds analysis, strategy and eventually machine
learning on top of that history.

The guiding rule: **raw data is permanent, intelligence is replaceable.** Telemetry is never
modified, so any future algorithm can be re-run against the complete history.

---

## Status

**Telemetry collection works end to end** — simulator to database. RaceRoom is the only connector
so far, and analysis has just started. The roadmap beyond that is in
[docs/architecture.md](docs/architecture.md).

| Area | State |
|---|---|
| RaceRoom connector | Working — shared memory, 60 Hz, layout-gated across game versions |
| Canonical telemetry model | Working — simulator-independent, capability-based |
| Collector (buffer + upload) | Working — bounded buffer, batched MessagePack upload |
| Ingest API | Working — API-key auth, versioned contracts |
| PostgreSQL persistence | Working — EF Core + bulk binary-copy writer |
| Analysis | Started — one deterministic model implemented and covered by tests |
| Strategy / ML / AI race engineer | Interfaces only — no implementation behind them yet |

With Docker running, `dotnet test RaceIntelligence.slnx` should come back fully green. The Aspire
and PostgreSQL integration suites need a container runtime and skip themselves when one isn't
available — that's the only reason a test here skips.

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
[r3e-api](https://github.com/kwstudios-sweden/r3e-api), taken from
[`sample-csharp/src/R3E.cs`](https://github.com/kwstudios-sweden/r3e-api/raw/refs/heads/master/sample-csharp/src/R3E.cs);
see those files and [docs/development.md](docs/development.md) for details.
