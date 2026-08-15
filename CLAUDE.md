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
