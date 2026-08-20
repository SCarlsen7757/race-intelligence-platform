#!/usr/bin/env python3
"""Rebuild the graphify graph, then join the EF entity configurations to the schema tables.

This is the one command to run after changing code:

    python tools/update-graph.py

It runs `graphify update .` and then repairs a gap that update leaves behind. graphify extracts C#
and SQL with separate parsers, so `SessionConfiguration` and the `sessions` table land in the graph
as two unconnected subgraphs even though the C# states the mapping outright:

    builder.ToTable("sessions");

That call is the mapping, so the edge is read off the source rather than guessed -- it is emitted as
EXTRACTED with confidence 1.0, the same standing as any other edge graphify parsed. The linking has
to happen here rather than inside graphify because `graphify update` rewrites graph.json from the
extractors, which know nothing about this join, and so drops the links every time.

Re-running is safe: existing links are replaced rather than duplicated.

Every database gets this treatment, not just the telemetry store: see STORES. The identity registry
has a schema dump of its own because it has a database of its own (ADR 0002), and its tables land
under their own node prefix.

Exits non-zero if a configuration names a table that is absent from the graph, which means that
store's schema dump is stale -- regenerate it with `dotnet ef migrations script` (see CLAUDE.md).

    --no-update   skip `graphify update .` and only relink an existing graph
"""

from __future__ import annotations

import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
GRAPH = REPO / "graphify-out" / "graph.json"


class Store:
    """One database: where its EF configurations live, and which DDL dump describes it.

    There is more than one because storage is not one database. The telemetry store is the
    simulator's, and the identity registry deliberately has its own so that it outlives any one of
    them (ADR 0002). They are extracted from separate schema dumps and their tables therefore land
    under separate node prefixes, so the join has to know which dump a configuration belongs to
    rather than assuming there is only one.
    """

    def __init__(self, project: str, schema: str) -> None:
        self.project = project
        self.config_dir = REPO / "src" / project / "Configurations"
        self.schema = schema
        # graphify names a SQL node after the file that declared it, so `docs/schema.sql` gives
        # `docs_schema_<table>` and `docs/identity-schema.sql` gives `docs_identity_schema_<table>`.
        self.table_prefix = re.sub(r"[^a-z0-9]+", "_", schema.lower().removesuffix(".sql"))
        self.namespace = f"{project.lower().replace('.', '_')}_configurations"

    def config_node_id(self, stem: str) -> str:
        """Node id graphify's C# extractor gives the configuration class in `stem`.cs."""
        lower = stem.lower()
        return (
            f"src_{self.project.lower().replace('.', '_')}_configurations_{lower}"
            f"_{self.namespace}_{lower}"
        )

    def table_node_id(self, table: str) -> str:
        """Node id graphify's SQL extractor gives a table declared in this store's DDL dump."""
        return f"{self.table_prefix}_{re.sub(r'[^a-z0-9]+', '_', table.lower())}"

    @property
    def script_command(self) -> str:
        return (
            f"  dotnet ef migrations script --project src/{self.project} "
            f"--output {self.schema}"
        )


STORES = [
    Store("RaceIntelligence.Persistence", "docs/schema.sql"),
    Store("RaceIntelligence.Identity", "docs/identity-schema.sql"),
]

# `builder.ToTable("sessions");` -- the only form used in this repo. A schema-qualified or
# variable-named overload would not match, and is reported as unmapped rather than guessed at.
TO_TABLE = re.compile(r'\.ToTable\(\s*"([^"]+)"', re.MULTILINE)

RELATION = "implements"
MARKER = "ef-table-mapping"


def run_graphify_update() -> int:
    """Run `graphify update .`, streaming its output. Returns its exit code."""
    exe = shutil.which("graphify")
    if exe is None:
        print(
            "error: `graphify` is not on PATH. Install it with `uv tool install graphifyy`\n"
            '       (add the SQL parser too: `uv tool install --with tree-sitter-sql graphifyy`),\n'
            "       or pass --no-update to only relink an existing graph.",
            file=sys.stderr,
        )
        return 1
    print("$ graphify update .", flush=True)
    return subprocess.run([exe, "update", str(REPO)], cwd=REPO).returncode


def main() -> int:
    if "--no-update" not in sys.argv:
        code = run_graphify_update()
        if code != 0:
            print(f"error: `graphify update` failed (exit {code}); not relinking", file=sys.stderr)
            return code

    if not GRAPH.exists():
        print(f"error: {GRAPH} not found -- run `graphify update .` first", file=sys.stderr)
        return 1

    graph = json.loads(GRAPH.read_text(encoding="utf-8"))
    edge_key = "links" if "links" in graph else "edges"
    node_ids = {n["id"] for n in graph["nodes"]}

    mappings: list[tuple[Store, str, str]] = []
    for store in STORES:
        found = False
        for path in sorted(store.config_dir.glob("*Configuration.cs")):
            for table in TO_TABLE.findall(path.read_text(encoding="utf-8")):
                mappings.append((store, path.stem, table))
                found = True

        if not found:
            print(
                f"error: no ToTable(\"...\") calls found under {store.config_dir}",
                file=sys.stderr,
            )
            return 1

    # Drop any links from a previous run so re-running replaces rather than accumulates.
    graph[edge_key] = [e for e in graph[edge_key] if e.get("origin") != MARKER]

    added = 0
    missing: list[tuple[Store, str]] = []
    for store, stem, table in mappings:
        source, target = store.config_node_id(stem), store.table_node_id(table)
        if source not in node_ids:
            missing.append((store, f"{stem} (configuration class absent from graph)"))
            continue
        if target not in node_ids:
            missing.append((store, f'{stem} -> "{table}" (absent from {store.schema})'))
            continue
        graph[edge_key].append(
            {
                "source": source,
                "target": target,
                "relation": RELATION,
                "confidence": "EXTRACTED",
                "confidence_score": 1.0,
                "source_file": f"src/{store.project}/Configurations/{stem}.cs",
                "source_location": None,
                "weight": 1.0,
                "origin": MARKER,
            }
        )
        added += 1

    GRAPH.write_text(json.dumps(graph, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"linked {added} configuration(s) to their tables in {GRAPH.relative_to(REPO)}")

    if missing:
        print("\nunmapped:", file=sys.stderr)
        for m in missing:
            print(f"  - {m}", file=sys.stderr)
        print(
            "\ndocs/schema.sql is probably stale -- regenerate it with:\n"
            "  dotnet ef migrations script --project src/RaceIntelligence.Persistence "
            "--output docs/schema.sql",
            file=sys.stderr,
        )
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
