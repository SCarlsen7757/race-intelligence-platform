# 0001 — Per-simulator storage

**Status:** Accepted. **Steps 1, 2 and 4 of the migration path are built**, and the per-simulator
project split they imply has landed: `Persistence.Core` holds the canonical types and declares no
schema, `Persistence.RaceRoom` owns RaceRoom's tables, migrations and bulk writer, and
`Ingest.RaceRoom` hosts the shared endpoints against it. **Step 3 — promoting a simulator's
first-class channels out of `extras` — is not built.**
**Supersedes:** the "one database holds every simulator" assumption in [architecture.md](../architecture.md).
**Depends on:** [0002 — the cross-simulator translator](0002-cross-sim-translator.md), which is what makes this decision survivable.

---

## Context

Storage today is one PostgreSQL database for every simulator. `games` is a reference table, and
`drivers`, `tracks`, `cars` and `game_versions` all carry a `game_id` foreign key. Anything a
simulator exposes that the canonical model does not name — push-to-pass, tyre subtype, cut-track
warnings, car damage — goes into a JSONB `extras` column.

That design bought one thing above all: a driver is `(game_id, sim_driver_id)`, so one database can
hold the same human's laps from several simulators and tell them apart from each other and from
everyone else.

It cost something too. A channel that is *first-class* to a simulator is still a JSON blob, which
means it cannot be indexed, cannot be constrained, and cannot be joined without unnesting. For
RaceRoom, push-to-pass is not exotic metadata — it is a strategy input.

## Decision

**Storage becomes per-simulator: one ingest API image and one PostgreSQL database per simulator,
each free to shape its schema to what that simulator actually exposes.**

The wire does **not** become per-simulator. The collector keeps posting the canonical model plus its
raw `extras` document, and each simulator's ingest API decides how to store what arrives. One
collector, one contract, N storage shapes.

That boundary is the whole reason this is affordable. Were the wire per-sim, adding a simulator would
mean a collector build, an ingest contract, a schema and a read API. As drawn, it means a connector
and a storage image — and the connector was always going to be per-simulator.

## What becomes per-simulator

| | Per-sim | Shared |
|---|---|---|
| PostgreSQL database | ✅ one per sim | |
| Ingest API image + migrations | ✅ | |
| Read/query API | ✅ | |
| Analysis plugins | ✅ (mirroring the collector's plugin model) | |
| Reference tables (`drivers`, `tracks`, `cars`) | ✅ duplicated | |
| `RaceIntelligence.Core` canonical model | | ✅ |
| `Ingest.Contracts` (the collector→ingest wire) | | ✅ |
| Collector, connectors, collector plugins | | ✅ |
| Live hub and `Live.Contracts` | | ✅ (in memory; unaffected) |
| Cross-sim driver identity registry | | ✅ — see 0002 |

## What collapses inside a per-sim schema

The database *is* the game, so the scoping disappears:

- `games` goes away entirely.
- `game_id` drops from `drivers`, `tracks`, `cars` and `game_versions`.
- `IX_drivers_game_id_sim_driver_id` becomes `IX_drivers_sim_driver_id`; likewise for
  `IX_tracks_game_id_name` and `IX_cars_game_id_sim_car_id`.
- `game_versions` **stays**. Dropping `game_id` does not make versioning less necessary — the game
  build, telemetry API version and connector version are exactly what keeps old rows interpretable,
  and that argument is unchanged.

And the point of the exercise: channels a simulator treats as first-class get promoted out of
`extras` into typed, indexable columns. For RaceRoom that is push-to-pass, tyre subtype, cut-track
warnings and car damage. **`extras` stays** as the escape hatch for anything not yet promoted — a new
channel should still cost a connector, not a migration.

Two conventions survive unchanged, because they are already right:

- `session_type`, `tyre_wear_rate` and `fuel_usage_rate` remain the **simulator's own raw codes**,
  untranslated. Normalising them belongs to the translator, which knows which simulator produced the
  value. Note `-1` sorts below `0`: `> 0` means "the rate was on".
- Absent is not zero. A nullable column means "not reported", and that stays distinct from a real
  zero at every layer.

## Consequences

**What this buys**

- A schema that fits the simulator, with its real channels queryable rather than buried in JSON.
- Deploy only what you run. A simulator you do not play costs nothing — no tables, no image, no
  migrations.
- Blast radius of one. A schema change for one simulator cannot break another, and a corrupt or
  restored database affects one simulator's history.

**What this costs, stated plainly**

- **N sets of migrations, N read APIs, N deployments.** This is the real bill, and it grows linearly
  with simulators.
- **No cross-simulator SQL.** A query spanning two simulators is impossible at the storage layer, by
  construction. Everything cross-sim moves to the translator.
- **Reference data is duplicated.** Spa exists in RaceRoom's database and again in ACC's, as
  unrelated rows. So does a driver.
- **`(game, sim_driver_id)` identity is gone**, and with it the ability to ask "how does this driver's
  tyre usage compare across simulators" directly. This is the load-bearing loss, and 0002 exists
  solely to answer it. **If the translator is not built, this decision costs the platform its
  cross-simulator analysis outright.**

## Migration path

Today's database is RaceRoom-only in practice, which makes the first step cheap:

1. ~~Treat the existing database as the RaceRoom instance.~~ **Done.** Which simulator that is comes
   from `Ingest:GameKey`; a session claiming another one is refused with a 400 naming both keys,
   rather than stored.
2. ~~Drop `games`, drop the `game_id` columns and re-point the unique indexes.~~ **Done.**
3. Promote RaceRoom's `extras` channels into typed columns, backfilling from the JSON already stored.
4. ~~Stand up the identity registry (0002) *before* the second simulator, not after — retrofitting
   identity across two populated databases is materially harder than seeding it with one.~~
   **Done.** `person` and `person_sim_alias` live in their own database, with a small service in
   front of them for the hand-curation — see 0002 §1.

Step 4 was the one with a deadline, and it has been met while there is still exactly one simulator
to seed from. Step 3 is what remains, and it is the one that makes the rest of this decision worth
its bill.

## Open questions

- Where the read API for a multi-sim dashboard lives: one gateway that fans out per simulator, or a
  read model built by the translator. Leaning to the latter, since the translator has to produce a
  canonical dataset anyway.
- Whether the analysis warehouse in 0002 is PostgreSQL or a columnar format. Not urgent; it does not
  change anything here.
