## Versioning: no backward compatibility before v1.0.0

This project is pre-release. There are **no git tags**, nothing is deployed, and every process —
collector, ingest API, live hub, dashboard — is built and started from the same commit. There is no
older client in the world for a newer server to be compatible with.

So: **do not write backward-compatibility code, and do not bump schema versions.** This holds while
there are no tags at all, and it keeps holding at `v0.*.*`. Only a `v1.*.*` tag ends it.

Rules:

- `LiveSchemaVersion.Current` and `Ingest.Contracts.SchemaVersion.Current` both stay at **1**.
  Changing a wire shape is not a reason to bump either one.
- Change contracts **in place**. Add, remove, rename, reorder and renumber fields as the design
  wants — including MessagePack `Key(n)` and `Union(n)` numbers. Nothing has to stay where it is.
- Do not add migration shims, tolerant readers, defaulted "for older clients" parameters, or
  `if (version < n)` branches. Do not keep a field alive only so an old payload still parses.
- Do not write changelog-style version-history docs on these constants. The git history is the
  history; a ladder of versions nobody can be running is documentation of a fiction.
- The version **handshakes** themselves stay (`IsSupported`, the hello check, the HTTP 400). They
  are one comparison each and they catch the mismatch that genuinely happens in development — a
  stale process still running from an earlier build — turning it into a named refusal instead of a
  decode error mid-race.
- Databases follow the same rule: prefer editing an existing EF Core migration and recreating the
  local database over stacking a corrective migration, unless there is data worth keeping.
- **The dashboard's exported view file is the one exception**, and it is not really one. This rule
  holds because every process is built from the same commit, so no older client can exist. A view
  file is not a process — it lives on a disk, gets sent to other people, and genuinely can arrive
  from a build that no longer exists. It carries a `version` for that reason. Nothing else on the
  wire or in the database gets to make this argument.

When the first `v1.0.0` tag exists, this section stops applying: from then on, deployed collectors
have to keep talking to the hub, and both schema versions start stepping with real changelogs.

## graphify

This project uses a graphify knowledge graph — god nodes, community structure, and cross-file
relationships — built into graphify-out/. That directory is gitignored because it is derived
entirely from the sources it indexes, so a fresh clone will not have one until it is built.

Rules:
- If graphify-out/graph.json is missing, build it with `graphify update .` before relying on the
  rules below. Code extraction is AST-only, so this needs no API key and costs nothing.
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `python tools/update-graph.py` to keep the graph current (AST-only, no
  API cost). Use it rather than `graphify update .` directly: it runs that, then restores the
  entity-to-table links that the rebuild drops (see below). `--no-update` relinks without rebuilding.
- **There is more than one database, and one per simulator.** RaceRoom's telemetry store is
  `src/RaceIntelligence.Persistence.RaceRoom` with `docs/raceroom-schema.sql`; the cross-simulator
  identity registry is `src/RaceIntelligence.Identity` with `docs/identity-schema.sql`, and it has
  its own because it must outlive any one simulator's store (ADR 0002).
  `tools/update-graph.py` knows about both — see `STORES` there — so a second simulator means
  adding its project and dump to that list rather than teaching the script a special case.
- **Every project's namespace matches its assembly name**, with no `RootNamespace` overrides. A
  `using` should say which assembly a type came from; the split briefly had `Persistence.Core`
  shipping code that called itself `RaceIntelligence.Persistence`, which named a project that no
  longer exists.
- **`src/RaceIntelligence.Persistence.Core` declares no schema, and must not start.** It owns the
  shared entity types, converters and repositories; a simulator owns the `ToTable` calls and the
  migrations. `SchemaOwnershipTests` asserts this rather than trusting it, because a configuration
  put in the obvious-looking project compiles and passes everything else.
- **The telemetry sample is RaceRoom's, and its channels are declared once.**
  `channels/raceroom-telemetry.channels` is the single declaration; a Roslyn source generator
  (`src/RaceIntelligence.RaceRoom.Channels.Generator`) emits the MessagePack DTO, the storage entity,
  its EF configuration, the bulk writer's column list **and the positional order it writes them in**,
  and the read API's channel allowlist. Add or rename a channel *there*, never in the generated
  output. The writer's two lists are the reason: a binary `COPY` checks neither against the other, so
  a mismatch writes camber into ride height and reports success.
- The database schema reaches the graph through `docs/schema.sql`, a generated DDL dump of the EF
  Core migrations — the tables and their foreign keys are real nodes, so no database has to be
  running. After adding a migration, regenerate it in the same commit:
  `dotnet ef migrations script --project src/RaceIntelligence.Persistence.RaceRoom --output docs/raceroom-schema.sql`
  and, for the registry:
  `dotnet ef migrations script --project src/RaceIntelligence.Identity --output docs/identity-schema.sql`
  Otherwise the graph keeps describing the previous schema.
- The C# and SQL extractors do not know they describe the same tables, so a bare `graphify update .`
  leaves the entity configurations and the schema unconnected, and drops any previous links.
  `tools/update-graph.py` restores them, reading each mapping off the `builder.ToTable("...")` call
  that declares it — which is why it is the command to run rather than graphify directly. It exits
  non-zero when a configuration maps to a table the graph does not have, naming which schema dump is
  stale.
