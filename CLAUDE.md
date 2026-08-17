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
- The database schema reaches the graph through `docs/schema.sql`, a generated DDL dump of the EF
  Core migrations — the tables and their foreign keys are real nodes, so no database has to be
  running. After adding a migration, regenerate it in the same commit:
  `dotnet ef migrations script --project src/RaceIntelligence.Persistence --output docs/schema.sql`
  Otherwise the graph keeps describing the previous schema.
- The C# and SQL extractors do not know they describe the same tables, so a bare `graphify update .`
  leaves the entity configurations and the schema unconnected, and drops any previous links.
  `tools/update-graph.py` restores them, reading each mapping off the `builder.ToTable("...")` call
  that declares it — which is why it is the command to run rather than graphify directly. It exits
  non-zero when a configuration maps to a table the graph does not have, meaning docs/schema.sql is
  stale.
