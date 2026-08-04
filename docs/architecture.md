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

The backend should never know which simulator produced the data.

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

This makes the system future-proof.

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

Recommended database:

**PostgreSQL**

Reason:

- Mature
- Reliable
- Fast
- Excellent indexing
- JSON support
- Widely supported
- Easy cloud deployment
- Can migrate to TimescaleDB later if needed

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
- Compare data across game updates instead of silently mixing it.

This applies the same principle as algorithm versioning:

> Know which code produced which number.

---

### Drivers

```
Driver
```

---

### Tracks

```
Track
Layout
Length
```

---

### Cars

```
Car
Class
Manufacturer
```

---

### Sessions

```
Session
Game Version
Track
Car
Weather
Setup
Duration
```

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

Raw telemetry should never be modified.

It is the source of truth.

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

Machine Learning is **not** required initially.

Phase 1 uses deterministic algorithms.

Examples:

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

### Phase 1 - Telemetry Collection

- RaceRoom connector
- Canonical telemetry model
- PostgreSQL database
- Background upload
- Session storage

---

### Phase 2 - Analysis

- Fuel model
- Tire degradation model
- Lap quality detection
- Driver consistency
- Traffic detection

---

### Phase 3 - Strategy

- Pit simulator
- Tire strategy
- Undercut prediction
- Overcut prediction
- Race simulations

---

### Phase 4 - Machine Learning

- Historical training
- Model comparison
- Prediction accuracy
- Continuous retraining

---

### Phase 5 - Multi-Simulator Support

Add connectors for:

- Assetto Corsa Competizione
- iRacing
- Automobilista 2
- Le Mans Ultimate
- rFactor 2
- Future simulators

No backend changes should be required.

Only new connectors.

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
