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

Responsibilities:

- Read simulator telemetry.
- Convert to canonical model.
- Buffer locally.
- Upload continuously in the background.
- Handle temporary network outages.
- Resume uploads automatically.

The collector should perform **no analysis**.

Its only responsibility is collecting reliable data.

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
whatever comes next. No backend changes should be required — only new connectors — so this can
happen at any point rather than waiting its turn.

---

## Guiding Principles

1. **Collect first, analyse later.**
2. **Raw telemetry is immutable.**
3. **The database is the single source of truth.**
4. **Algorithms are replaceable and versioned.**
5. **Every session records the game, game version, telemetry API version and connector version that produced it.**
6. **Machine learning is an enhancement, not a requirement.**
7. **The backend is simulator-agnostic.**
8. **The AI explains decisions instead of making opaque calculations.**
9. **Design for extensibility without overengineering the first implementation.**
