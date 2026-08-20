# Architecture and Design

> **Vision:**
> Build a simulator-agnostic telemetry and race strategy platform that continuously improves through
> collected data. The platform is **data-driven first**, **machine learning second**, and
> **AI-assisted third**.

This document is the design rationale for the platform — what it is meant to become and why it is
shaped the way it is. For how to actually run it, see [development.md](development.md).

---

## Project Goals

The platform should:

- Support multiple racing simulators.
- Collect and store raw telemetry.
- Analyze historical races.
- Predict tire degradation.
- Predict optimal pit windows.
- Estimate undercut/overcut opportunities.
- Compare different strategy algorithms.
- Eventually train Machine Learning models.
- Provide live strategy recommendations through an AI race engineer.

The important design philosophy is:

> **Raw data is permanent. Intelligence is replaceable.**

---

## High-Level Architecture

```
                +----------------------+
                |   Racing Simulator   |
                | (RaceRoom, ACC, etc) |
                +----------+-----------+
                           |
                     Telemetry API
               (Shared Memory / UDP)
                           |
                           |
                +----------v-----------+
                |   Sim Connector      |
                | (RaceRoom Client)    |
                +----------+-----------+
                           |
                 Canonical Telemetry Model
                           |
                           |
               +-----------v------------+
               | Local Telemetry Buffer |
               +-----------+------------+
                           |
                     Background Upload
                           |
                           |
          =====================================
                    Home Server (Proxmox)
          =====================================

                +-----------------------+
                | PostgreSQL Database   |
                +-----------+-----------+
                            |
                +-----------v-----------+
                | Analysis Engine       |
                +-----------+-----------+
                            |
                +-----------v-----------+
                | Strategy Engine       |
                +-----------+-----------+
                            |
                +-----------v-----------+
                | Machine Learning      |
                +-----------+-----------+
                            |
                +-----------v-----------+
                | AI Race Engineer      |
                +-----------------------+
```

### The live path

Everything above is the archive path: permanent, batched, and read long after the session. Alongside
it runs a second path with the opposite priorities, feeding a dashboard a race engineer watches
while the race happens.

```
   Gaming PC                          Home Server                     Anywhere
   ---------                          -----------                     --------

   Sim Connector
     |  60 Hz local car ---> Buffer ---> Upload --HTTP+key--> Ingest.Api --> PostgreSQL
     |
     |  10 Hz whole field
     |   1 Hz extras (damage)
     +--------------------> LiveOutbox --WS+key--> +----------------------+
                            (conflating)           | Web (live hub)       |
                                                   |  room registry       |--WS (open)--> Dashboard
   Second gaming PC ------------WS+key------------>|  tower + focus       |
                                                   |  lap accumulator     |
                                                   |  latest extras       |
                                                   +----------------------+
```

The collector publishes three rates, each sized for what it carries. The tower runs at a tenth of
the focus stream's rate because positions and gaps do not change between two 60 Hz frames. Extras —
the simulator's own JSON document, which is where car damage lives — run slower again, because their
contents move on the scale of a race and every consumer of them parses JSON. In both the collector's
outbox and each viewer's queue, extras sit at the *bottom* of the priority ladder: a once-a-second
document must never interrupt the traces a race engineer is reading.

The two paths share only the connector that feeds them, and neither can stall the other. Three
properties define the live one, and each is the opposite of what the archive path does:

- **Conflating, not buffered.** Every hop keeps the newest frame and drops the rest. A live value
  is worthless the moment a fresher one exists, so a recovering socket must deliver the current
  race rather than a replay of where the cars used to be.
- **In memory, and mostly momentary.** No tables, no migrations, and a hub restart mid-race costs a
  reconnect rather than data — the archive path already keeps what is worth keeping forever. One
  thing does accumulate: `LapHistoryAccumulator` retains every completed lap per driver for the life
  of the room, because a stint only means anything as a sequence and a race engineer reading tyre
  degradation off five laps needs all five. It is bounded (512 laps per driver, drop-oldest, with a
  `truncated` flag on the wire) and forgotten when the room expires, so it is retention, not storage.
- **Open to read, keyed to write.** Anyone with the link can watch. Publishing needs a key, because
  a fabricated timing tower is indistinguishable from a real one to the person making a pit call
  from it.

Two hosts on the server rather than one: the auth postures are opposites, and bulk-COPY transactions
should not share a process with a latency-sensitive fan-out loop.

Opponent data is scoring-granularity only — position, gaps, lap and sector times, pit state. Pedals,
tyre pressure, tyre wear and the extras document exist solely for the car a machine is driving. That
asymmetry is why several collectors in one session merge into one enriched view rather than
competing.

Lap history is the exception to that asymmetry, and deliberately so: it is accumulated from the
standings snapshot, which describes every car in the session, so a viewer can expand any driver's
row — not only one whose own machine is publishing. The accumulator is fed from the snapshot the
hub *selected*, not from whichever frame arrived, and records idempotently by lap number. That is
what keeps it correct when the selection switches between two publishers mid-race: a snapshot from a
client a lap behind reports a lap already recorded and produces nothing.

---

## Core Design Principles

### 1. Multi-Simulator First

Although development starts with RaceRoom, the backend should never depend on a specific simulator.

Instead:

```
RaceRoom
      \
ACC
       \
iRacing ---> Canonical Telemetry Model
       /
AMS2
     /
LMU
```

Every simulator only needs a connector.

---

## Canonical Telemetry Model

Every connector converts simulator-specific telemetry into a common format.

Example:

```text
Timestamp
Speed
Throttle
Brake
Steering
Gear
RPM
Fuel
Lap Number
Sector
Position
Wheel Speeds
Suspension Travel
Tyre Temperatures
Tyre Pressures
Tyre Wear
Track Position
```

These are the fields the analysis engine understands.

---

## Simulator Specific Data

Every simulator exposes unique telemetry.

RaceRoom may expose:

```
Push-to-pass
```

Another simulator may expose:

```
ERS
Hybrid Battery
```

Instead of changing the database every time:

```
Canonical Fields

+

Flexible Metadata (JSON)
```

Example

```json
{
    "ERSMode": 3,
    "HybridBattery": 0.72
}
```

A new simulator with fields nobody anticipated should cost a connector, not a migration.

That still holds for the **wire**: the collector posts the canonical model plus this JSON, whatever
the simulator is. What is changing is the far side of it. With storage moving per simulator
([0001](decisions/0001-per-sim-storage.md)), a channel that is first-class to a simulator gets
promoted out of this blob into a typed column in *that simulator's* schema — push-to-pass is not
exotic metadata to RaceRoom, it is a strategy input, and buried in JSON it can be neither indexed
nor constrained. The JSON stays as the escape hatch for everything not yet promoted, so a brand-new
channel still costs nothing.

---

## Capability System

Each connector should expose capabilities.

Example:

```text
Supports Tyre Wear
Supports Brake Temperatures
Supports Fuel Flow
Supports ERS
Supports Damage
Supports Track Rubber
```

The strategy engine checks capabilities before using an algorithm.

Instead of:

```
if simulator == RaceRoom
```

Use:

```
if SupportsTyreWear
```

This keeps the system simulator-independent.

---

## Database Design

**PostgreSQL**, for three reasons specific to this workload:

- Native JSON columns, which the simulator-specific metadata above depends on.
- Indexing good enough to slice a large telemetry table by session, lap and time.
- TimescaleDB is an extension rather than a different database, so the time-series upgrade path
  stays open without a rewrite.

At the current estimated data rate (~20 MB for a 30-minute race), plain PostgreSQL should comfortably handle the workload for a long time.

The schema should be designed so that adopting TimescaleDB later is an optimization rather than a redesign.

---

## Database Tables

### Games

```
Game
Key
Name
```

Every session records **which simulator produced it**.

This is reference data, not a branch in the code. The backend still never asks
"is this RaceRoom?" — it asks the capability system what the data supports.

---

### Game Versions

```
Game
Game Version
Telemetry API Version
Connector Version
```

Simulators change. A game update can silently alter units, add fields, or change
the meaning of an existing value.

Because **raw telemetry is immutable and permanent**, old data must remain
interpretable years later. That is only possible if every session records the
exact versions that produced it:

- The **game version** — the simulator build.
- The **telemetry API version** — e.g. RaceRoom's shared memory reports major/minor.
- The **connector version** — our own code that did the translation.

Benefits:

- Detect when a game update changed telemetry semantics.
- Exclude sessions collected by a connector with a known bug.
- Replay and recompute history correctly per version.
- Keep sessions from several game versions distinguishable, since one connector
  deliberately spans more than one — see
  [The RaceRoom shared-memory layout](development.md#the-raceroom-shared-memory-layout) for how a
  connector decides which game versions it can safely read.
- Compare data across game updates instead of silently mixing it.

This applies the same principle as algorithm versioning:

> Know which code produced which number.

---

### Drivers, Tracks and Cars

Plain reference tables that sessions point at, so a name is stored once and can be corrected in one
place:

```
Driver
Game
Sim Driver Id
Display Name

Track / Layout / Length

Car / Class / Manufacturer
```

One database holds sessions from several people, and their driving signatures are
exactly what the analysis layer exists to tell apart — one driver is hard on tyres
but light on fuel, another is the reverse. Attribution has to survive the obvious
failure: **players rename themselves.**

So identity is the **sim's own stable driver id**, not the display name:

- The **sim driver id** — a durable account id issued by the simulator. RaceRoom
  exposes one over shared memory; a rename does not change it.
- The **game** — scoping the id, because the id is only unique within the sim that
  issued it. A RaceRoom user id and a future iRacing customer id share a numeric
  namespace and would otherwise collide.
- The **display name** — a mutable label, tracking the most recently seen name.
  The name used during a given session is recorded on the session itself, so
  renaming loses nothing.

> **Changing.** Storage is moving to one database per simulator, so the **game** scope above
> disappears — inside RaceRoom's database, a RaceRoom driver id is already unique. What that scope
> bought, one human recognisable across simulators, moves to a separately held identity registry.
> See [0001](decisions/0001-per-sim-storage.md) and [0002](decisions/0002-cross-sim-translator.md).
> The other two bullets are unaffected.

Sims that expose no driver id fall back to name matching within a game — worse, but
the only option available for that source.

---

### Sessions

```
Session
Game Version
Driver
Player Name
Track
Car
Weather
Setup
Duration
Fuel Usage Rate
Tyre Wear Rate
```

**Fuel usage rate** and **tyre wear rate** are session rules, configured
independently — a session can run 3x tyre wear with fuel consumption switched off
entirely. They are recorded because they change what the data *means*: a lap at 4x
burns four times the fuel of the same lap at 1x, so two sessions run under different
rates are not comparable inputs to a fuel or degradation model. Buried in a JSON
blob they cannot be filtered or indexed on, which is what a training set needs.

Both are stored as the **sim's own raw rate code, untranslated** — the same
convention as `session_type` below. RaceRoom encodes them as `-1` = not available,
`0` = off, `1`–`4` = 1x–4x. The collector performs no analysis and does not know
which encoding is canonical; normalizing belongs to a later pass that knows which
sim produced the value. When querying, note that `-1` sorts below `0`: use
`> 0` to mean "the rate was on", not `>= 0`.

**Player name** is the display name reported for this session specifically. Since
`Driver` tracks the latest name, this is what keeps the historical one.

---

### Laps

```
Lap Number
Lap Time
Fuel Used
Average Speed
Maximum Speed
Quality Score
```

---

### Telemetry

High-frequency telemetry samples.

Example:

```
Timestamp
Throttle
Brake
Steering
Speed
RPM
Fuel
etc.
```

This is the largest table.

---

## Raw Telemetry Philosophy

Store **all raw telemetry**.

Reasons:

- Storage is inexpensive.
- New algorithms may need old data.
- Machine learning benefits from large datasets.
- Historical comparisons become possible.
- Bugs in algorithms can be fixed by replaying history.

---

## Collector Design

The collector reads the simulator and dispatches what it reads. Everything that *sends* data
anywhere is a **plugin**:

```
   Simulator ──▶ Connector ──▶ Collect loop ──┬──▶ Ingest plugin ──▶ Ingest API ──▶ PostgreSQL
                (ITelemetrySource)            │     buffered, ordered, retried
                                              │
                                              └──▶ Live plugin ────▶ Live hub ──▶ Dashboard
                                                    conflating, newest-wins
```

Responsibilities of the collector itself:

- Read simulator telemetry.
- Convert to canonical model.
- Dispatch to whichever plugins consume each event.
- Keep those plugins from being able to affect each other.

The collector should perform **no analysis**, and should not know where anything ends up. Adding a
destination costs a plugin, not a change to the collect loop.

### Why plugins share a lifecycle, not a sink interface

There is deliberately no uniform `ITelemetrySink`. The two paths have opposite definitions of
correct behaviour:

| | Archive | Live |
|---|---|---|
| Under pressure | Buffers, applies backpressure | Drops the older frame |
| A lost message | Data gone forever | Correct — a newer one exists |
| After an outage | Resumes and uploads the backlog | Resumes with the current race |

One interface spanning both would force one path to adopt the other's failure mode: either the live
path starts buffering stale frames, or the archive path starts dropping telemetry. Plugins therefore
share only a lifecycle, and implement whichever of the four observer interfaces they consume —
sessions, samples, standings, extras — each bringing its own delivery semantics.

The same argument sets the shape of those interfaces. Sessions and laps are rare enough that a plugin
may do real work inline; samples at 60 Hz, standings at 10 Hz and extras at ~1 Hz run on the collect
loop, where time spent is time the simulator is not being read — for every plugin, not just the one
spending it.

### Composition is config-driven, not dynamic

The set of plugins the collector *can* run is fixed when it is built; configuration chooses which of
them actually run. That keeps the build trimmable and AOT-safe, avoids a public plugin API to keep
compatible across versions, and makes a misconfigured plugin a startup failure rather than a
load-time surprise mid-race. A genuinely new destination needs a rebuild — which, for a platform
deployed by the person developing it, is not a cost.

---

## Analysis Engine

Runs after races or on demand.

Responsibilities:

- Fuel usage
- Tire degradation
- Driver consistency
- Track evolution
- Traffic detection
- Pit stop simulation
- Undercut analysis
- Overcut analysis
- Stint analysis

Outputs are stored separately from the raw telemetry.

---

## Quality Scoring

Not every lap should influence the models equally.

Examples of low-quality laps:

- Heavy traffic
- Yellow flags
- Lockups
- Spins
- Off-track excursions
- Damage

Instead of deleting data:

Store observations.

Calculate quality later.

This allows better quality algorithms to be introduced in the future.

---

## Strategy Engine

Consumes analysis results.

Produces:

- Pit recommendations
- Fuel recommendations
- Tire recommendations
- Undercut opportunities
- Overcut opportunities
- Expected race outcome

Example:

```
Pit now

Expected gain:
+2.4 s
```

---

## Algorithm Versioning

Every strategy algorithm should have a version.

Example:

```
Tyre Model v1
Tyre Model v2
Tyre Model v3
```

Results should include the version that produced them.

Benefits:

- Compare algorithms.
- Roll back poor versions.
- Benchmark improvements.
- Measure prediction accuracy.

---

## Machine Learning Roadmap

Machine Learning is **not** required initially. The first implementations should be deterministic
algorithms, for example:

- Linear regression
- Tire degradation curves
- Fuel calculations
- Pit stop simulations

Once sufficient historical data exists:

Machine Learning can be introduced.

Possible models:

- Tire degradation prediction
- Pit window prediction
- Opponent strategy prediction
- Driver consistency prediction
- Fuel consumption prediction
- Setup recommendation

The models are trained using historical telemetry.

The raw database remains unchanged.

New models can always be retrained using the complete history.

---

## AI Race Engineer

The AI should **not** calculate telemetry.

Instead it explains the outputs.

Example:

> "Stay out for two more laps. Tire degradation is still acceptable, and pitting now would rejoin behind slower traffic. Waiting until lap 18 is estimated to gain 1.8 seconds."

This keeps AI focused on reasoning and communication rather than numerical calculations.

---

## Development Roadmap

Deliberately unnumbered. Earlier drafts of this document and the README used phase numbers that
disagreed with each other, and the ordering below is a preference rather than a schedule — analysis
work can start before multi-simulator support is finished, and probably will.

### Built

- RaceRoom connector
- Canonical telemetry model
- PostgreSQL storage
- Background upload and session storage

### In progress — the platform

- Collector plugin host: the collect loop dispatches, plugins deliver *(built)*
- Per-sim storage images and the translator layer that restores cross-sim comparison *(designed —
  [0001](decisions/0001-per-sim-storage.md), [0002](decisions/0002-cross-sim-translator.md))*
- The cross-simulator identity registry, which had to exist **before** the second simulator's
  database rather than after it *(built — `person` and `person_sim_alias` in their own database,
  with a small service for the hand-curation; [0002 §1](decisions/0002-cross-sim-translator.md))*

### In progress — the live dashboard

- Whole-field standings from the connector, and the live wire contracts *(built)*
- Collector live publishing, independently switchable from archiving *(built)*
- Server-side lap history, so a race engineer sees a whole stint rather than the last lap *(built)*
- A low-rate channel for slow-moving sim-specific values such as car damage *(built)*
- Live hub: publisher and viewer sockets, room registry, timing tower *(built)*
- Dashboard: TanStack Start on its own origin, the room in the URL, timing tower with expandable
  per-lap rows *(built)*
- The pit wall: one page the engineer arranges, out of a capability-gated widget catalogue, saved
  per simulator and exportable as a file *(built)*
- Three rate classes on the wire — the focus frame, a typed stint frame, and the connector's own
  extras — each carrying what changes at its rate *(built)*
- Charts whose channels can be toggled per tile, so one widget can be narrowed to a single corner
  *(built)*
- Merging several collectors in one session into one enriched view
- Comparing several cars at once: the wall draws the selected car today, and the widgets are keyed
  by driver so the overlay is additive. Needs the hub's focus cap raised and, at that point,
  probably a viewer that asks for only the channels its wall shows
- A historical read API, which is what the second half of the telemetry chart backlog waits on —
  every scatter, histogram and cross-session view needs stored telemetry rather than a rolling
  window
- RaceRoom-specific channels not yet on the live wire: cut-track warnings, tyre subtype, pit menu
  state

### In progress — analysis

- Lap-time trend over a stint
- Fuel model
- Lap quality detection
- Driver consistency
- Traffic detection

### Planned — strategy

Once analysis produces enough per-stint numbers to reason over:

- Pit simulator
- Tyre strategy
- Undercut and overcut prediction
- Race simulations

### Planned — machine learning

Once enough history exists to train on:

- Historical training
- Model comparison
- Prediction accuracy
- Continuous retraining

### Planned — AI race engineer

Explaining the strategy engine's output in plain language, as described above. It depends on the
strategy engine existing, so it comes after it — not because of a fixed slot in a sequence.

### Ongoing — more simulators

Connectors for Assetto Corsa Competizione, iRacing, Automobilista 2, Le Mans Ultimate, rFactor 2 and
whatever comes next. A new simulator costs a connector and a storage image; the collector, the wire
and the live hub are unchanged, so this can happen at any point rather than waiting its turn.

---

## Decision records

Design decisions that changed something written above, kept as their own records rather than folded
in silently:

| | |
|---|---|
| [0001](decisions/0001-per-sim-storage.md) | Storage becomes one database per simulator |
| [0002](decisions/0002-cross-sim-translator.md) | The translator that restores cross-simulator comparison |

---

## Guiding Principles

1. **Collect first, analyse later.**
2. **Raw telemetry is immutable.**
3. **The per-simulator databases are the single source of truth.** Everything cross-simulator is
   derived from them and can be rebuilt.
4. **Algorithms are replaceable and versioned** — including the translator.
5. **Every session records the game version, telemetry API version and connector version that produced it.**
6. **Machine learning is an enhancement, not a requirement.**
7. **The collector, the wire and the live hub are simulator-agnostic.** Storage is deliberately not:
   it is shaped to the simulator it holds, and the translator is what puts the pieces back together.
8. **The AI explains decisions instead of making opaque calculations.**
9. **Design for extensibility without overengineering the first implementation.**
