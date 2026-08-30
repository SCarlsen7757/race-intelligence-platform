# 0001 — Per-simulator storage

**Status:** Accepted, and **amended in place by #109**, which extended the decision from storage to
the wire. All four steps of the migration path are built and the per-simulator project split has
landed: `Persistence.Core` holds the shared entities and declares no schema, `Persistence.RaceRoom`
owns RaceRoom's tables, migrations, bulk writer and — since #109 — the telemetry sample itself, and
`Ingest.RaceRoom` hosts the endpoints against it. Every RaceRoom channel is now a typed column;
`telemetry_samples.extras` is gone. **See "The wire, reconsidered" below**, which reverses this
decision's central "the wire does not become per-simulator" and says why.
**Supersedes:** the "one database holds every simulator" assumption in [architecture.md](../architecture.md).
**Depends on:** [0002 — the cross-simulator translator](0002-cross-sim-translator.md), which is what makes this decision survivable.

---

## Context

Storage originally used one PostgreSQL database for every simulator. `games` was a reference table,
and `drivers`, `tracks`, `cars` and `game_versions` all carried a `game_id` foreign key. Anything a
simulator exposes that the canonical model does not name — push-to-pass, tyre subtype, cut-track
warnings, car damage — crossed the shared wire in a JSONB `extras` document and remained there in
storage.

That design bought one thing above all: a driver is `(game_id, sim_driver_id)`, so one database can
hold the same human's laps from several simulators and tell them apart from each other and from
everyone else.

It cost something too. A channel that is *first-class* to a simulator is still a JSON blob, which
means it cannot be indexed, cannot be constrained, and cannot be joined without unnesting. For
RaceRoom, push-to-pass is not exotic metadata — it is a strategy input.

## Decision

**Storage becomes per-simulator: one ingest API image and one PostgreSQL database per simulator,
each free to shape its schema to what that simulator actually exposes.**

~~The wire does **not** become per-simulator. The collector keeps posting the canonical model plus its
raw `extras` document, and each simulator's ingest API decides how to store what arrives. One
collector, one contract, N storage shapes.~~

~~That boundary is the whole reason this is affordable. Were the wire per-sim, adding a simulator would
mean a collector build, an ingest contract, a schema and a read API. As drawn, it means a connector
and a storage image — and the connector was always going to be per-simulator.~~

**Reversed by #109 — see "The wire, reconsidered".** The bill this paragraph names is real and has
been accepted; it was not overlooked.

## What becomes per-simulator

| | Per-sim | Shared |
|---|---|---|
| PostgreSQL database | ✅ one per sim | |
| Ingest API image + migrations | ✅ | |
| Read/query API | ✅ | |
| Analysis plugins | ✅ (mirroring the collector's plugin model) | |
| Reference tables (`drivers`, `tracks`, `cars`) | ✅ duplicated | |
| `RaceIntelligence.Core` (sessions, laps, capabilities, analysis) | | ✅ |
| The telemetry sample (`RaceRoom.Telemetry`) | ✅ — since #109 | |
| `Ingest.Contracts` (the collector→ingest wire) | ✅ — since #109 | |
| `Live.Contracts` (the publisher and viewer wires) | ✅ — since #109 | |
| The live hub itself (rooms, viewers, fan-out) | | ✅ |
| Collector, connectors, collector plugins | | ✅ (one connector per sim, as always) |
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
`extras` into typed, indexable columns. For RaceRoom that began as push-to-pass, tyre subtype,
cut-track warnings and car damage, and **#109 finished the job**: every channel is a column and
`telemetry_samples.extras` no longer exists. A session's `extras` does — it is written once per
session and nothing queries by it, which is the case a jsonb column is actually for.

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

## The wire, reconsidered (#109)

**The wire is per-simulator too.** Both of them: `Ingest.Contracts` and `Live.Contracts` are
RaceRoom's, and every channel on each is a typed member rather than a JSON string.

### What was actually costing

The shared wire carried thirty-seven typed fields plus `Extras`, a raw JSON *string*, and
twenty-nine RaceRoom channels travelled inside it. Measured on the deployed test database — five
sessions, 357,152 samples:

| | Size | Share |
|---|---|---|
| `telemetry_samples` total | 724 MB | |
| `extras` (jsonb) | 396 MB | **55%** |
| `tyre_temperature` (jsonb) | 95 MB | 13% |

**68% of the telemetry table was JSON** — about 1,164 bytes of `extras` per row carrying perhaps 300
bytes of values, the same twenty-nine key names repeated 357,152 times. Reading a channel meant
`extras->'tyreGrip'->>0`: unindexable, untypeable, and wrong only at runtime.

Three things followed from typing it that were not visible before:

- **The operating-window values were constants.** Across 122,562 samples of one session the tyre and
  brake `optimal`/`cold`/`hot` bounds each had exactly one distinct value, against 119,146 for the
  reading they bound. They are their own table now, keyed `(session, corner, compound)` so a
  mid-session tyre change stays correct.
- **`wheel_speed` carried RaceRoom's raw sign** — every sample negative, magnitude matching road
  speed — so wheel slip was uncomputable without knowing that. Normalised at the connector.
- **An entire struct was never read.** `R3EPlayerData` had been transcribed field by field and
  nothing touched it, so acceleration, camber, ride height, suspension velocity, downforce and world
  position had been described and left on the floor (#104). They are columns now.

### The cost, accepted rather than avoided

The paragraph struck through above is right about the bill: a second simulator now needs a collector
build, an ingest contract, a schema and a read API rather than a connector and a storage image. That
is accepted, for two reasons.

**Only RaceRoom ships.** `ITelemetrySource` has exactly one implementation and `Program.cs`
constructs the RaceRoom source directly. Much of #109 acknowledges what was already true rather than
changing it.

**The generalisation is better designed against two simulators than one.** A shared abstraction with
one implementation is a guess; the second simulator is when it can be drawn against something real.

### What makes it affordable in practice

The hundred and seventy-five channels are declared once, in
`channels/raceroom-telemetry.channels`, and a Roslyn source generator emits the MessagePack DTO, the
storage entity, its EF configuration, the bulk writer's column list **and the positional order it
writes them in**, and the read API's channel allowlist.

That last pair is the load-bearing one. A binary `COPY` takes a column list and a stream of
positional values and checks neither against the other: a mismatch writes camber into ride height and
reports success. Two hand-kept lists of a hundred and seventy-five entries would be a matter of time.
One loop over one list makes the mismatch unexpressible.

### What was removed rather than filled

`sessions.weather` and `sessions.setup` are gone. Both were `NULL` on every row ever written and
always would have been: RaceRoom has no dynamic weather and exports none, and there is no readable
setup export in any form a connector could persist. They were not channels waiting to be captured —
they were capabilities the simulator does not offer, and a column that can only ever be null
documents a feature that does not exist.

## Migration path

Today's database is RaceRoom-only in practice, which makes the first step cheap:

1. ~~Treat the existing database as the RaceRoom instance.~~ **Done.** Which simulator that is comes
   from `Ingest:GameKey`; a session claiming another one is refused with a 400 naming both keys,
   rather than stored.
2. ~~Drop `games`, drop the `game_id` columns and re-point the unique indexes.~~ **Done.**
3. ~~Promote RaceRoom's `extras` channels into typed columns.~~ **Done.** Push-to-pass, tyre subtype,
   cut-track warnings and damage are projected once at the storage boundary. Negative simulator
   sentinels become `NULL`, zero remains zero, and promoted leaves are removed from stored `extras`.
   The pre-v1 local database was recreated instead of backfilled because it held no data worth
   preserving.
4. ~~Stand up the identity registry (0002) *before* the second simulator, not after — retrofitting
   identity across two populated databases is materially harder than seeding it with one.~~
   **Done.** `person` and `person_sim_alias` live in their own database, with a small service in
   front of them for the hand-curation — see 0002 §1.

Step 4 was the one with a deadline, and it was met while there was still exactly one simulator to
seed from. Step 3 completes the simulator-owned storage shape that makes the rest of this decision
worth its bill.

## The read API, as built

**Per simulator, as its own deployable: `RaceIntelligence.Read.Api` is the shared endpoint library
and `RaceIntelligence.Read.RaceRoom` is the first host** — the same library-plus-host split the
ingest side already makes, and for the same reason. Reading a session, its laps and a lap's
telemetry is one question in every simulator, because it is asked of the canonical entities in
`Persistence.Core`; only the `DbContext` registration names a schema.

**It is a separate service from the ingest host, and that is forced rather than tidy.** The two have
opposite exposure. [0003](0003-deployment-topology.md) keeps the ingest API off the tunnel because
`Ingest.Api/Auth/ApiKeyFilter.cs` documents its own key check as a non-constant-time Phase-1
compromise; a dashboard on its own origin has to reach *something*, and it cannot be that. The other
candidate was the live hub, which is already exposed — but the hub holds no database credentials by
design, that being the property `AppHost.cs` and `Ingest.RaceRoom/Program.cs` both state explicitly,
and it is shared across simulators where this decision says storage is not.

So: the ingest host keeps a key and stays on the LAN; the read host holds no key, serves only GETs,
writes nothing, applies no migrations, and is published. Its guard is an origin allowlist that must
be non-empty for it to start. An API key was considered and rejected: to be useful it would have to
ship inside the browser bundle, where it is not a secret and only reads as one — the same argument
the hub already makes for its open viewing socket.

This is the third instance of the "N deployments" bill: a second simulator means another database,
another ingest API, another migration bundle, and now another read API.

## Open questions

- Whether the cross-simulator read path is this API fanned out per simulator or a read model built
  by the translator. Unchanged by the above, which deliberately answers only the single-simulator
  case: `Read.Api` is per-sim by construction and knows nothing of any other. Still leaning to the
  translator, since it has to produce a canonical dataset anyway.
- **What the second simulator's sample type shares with RaceRoom's, if anything.** #109 deliberately
  declined to guess: the manifest and its generator are RaceRoom's, and whether a second simulator
  gets its own manifest, its own generator, or a shared one with a per-sim manifest is a question to
  answer when there is a second manifest to look at.
- Whether the analysis warehouse in 0002 is PostgreSQL or a columnar format. Not urgent; it does not
  change anything here.
