# 0002 — The cross-simulator translator

**Status:** Accepted. **§1, the driver-identity registry, is built** — `person` and
`person_sim_alias` in their own database (`src/RaceIntelligence.Identity`), with a service in front
of them for the hand-curation. **The translator itself is not**: §2 raw codes, §3 sentinels and §4
units remain design only, and nothing yet writes an analysis store.
**Exists because of:** [0001 — per-simulator storage](0001-per-sim-storage.md). Splitting storage
per simulator removes the one thing that made cross-simulator comparison possible. This is the
replacement, and it is a deliverable rather than an aspiration: without it, 0001 is a net loss.

---

## Context

With storage split per simulator, `(game_id, sim_driver_id)` no longer exists. Nothing knows that
RaceRoom driver `4242` and iRacing customer `881109` are the same person. Nothing knows that
RaceRoom's `session_type = 10` and ACC's session enum mean the same thing. And machine learning —
the reason the platform stores raw telemetry forever — wants one flat, stable training set, not N
differently-shaped databases.

## Decision

**A batch translator reads each simulator's database, normalises what it finds, and writes a single
canonical analysis store.** Cross-simulator analysis and all model training read from that store,
never from a simulator's database directly.

**Batch, not query-time federation.** Federating a query across N PostgreSQL instances is slow,
fragile, and — decisively — unversionable. The platform already holds that algorithms are versioned
and results record the version that produced them; a training set assembled fresh on every query
cannot honour that. A batch output can be rebuilt, diffed, and pinned.

The translator is therefore itself versioned, and every row it writes records the translator version
that produced it. Raw telemetry stays immutable in the per-sim databases; the analysis store is
derived and disposable, and can always be rebuilt from history.

## The four things it must get right

### 1. Driver identity — the shared state that must exist *(built)*

This is the only piece of genuinely shared, hand-curated state in the whole platform, and the only
part of 0001 that cannot be per-simulator.

```
person            (id, display_name)
person_sim_alias  (person_id, sim_key, sim_driver_id)   -- unique on (sim_key, sim_driver_id)
```

It lives in its own small database, separate from every simulator's, because it must outlive any of
them — restoring one simulator's database must not lose the mapping for the others.

It cannot be inferred reliably. A simulator's driver id is stable *within* that simulator and shares
a numeric namespace with every other simulator's, so ids collide across sims and mean nothing to
each other. Display-name matching is exactly the failure the original design avoided: players
rename themselves, and two people can pick the same name. So the registry is **asserted, not
derived** — seeded by hand, assisted at most by "this name appeared in both, is it the same person?"
prompts that a human answers.

Unmapped drivers are not an error. A driver with no alias row is simply absent from cross-sim
analysis while remaining fully present in their own simulator's data.

### 2. Raw simulator codes

Codes are stored raw and untranslated by design, because the collector does not know which encoding
is canonical. The translator is the pass that does.

| Concept | RaceRoom stores | Canonical |
|---|---|---|
| `session_type` | its own `r3e_session` enum | `SessionType` |
| `tyre_wear_rate` | `-1` N/A, `0` off, `1`–`4` = 1×–4× | `double?` multiplier: `null`, `0.0`, `1.0`–`4.0` |
| `fuel_usage_rate` | same encoding | same |
| `pit_stop_status` | `r3e` pit enum | `PitStopStatus` |

Each simulator brings its own table of these. A code the translator does not recognise becomes
`null` with a warning — never a guess, and never the numerically nearest neighbour.

### 3. Sentinels, per field — and the trap in them

RaceRoom writes `-1` for "not available". The rule is that `-1` becomes `NULL` and **never** `0`:
rendering an unavailable damage reading as zero tells a race engineer the car is fine when the truth
is that nobody knows.

But the rule is **per field, not global**, and this is the trap worth writing down:

> `SteerInputRaw` is legitimately `-1.0` at full left lock. `Throttle` and `Brake` are legitimately
> `0.0`. A blanket "map -1 to null" pass would silently delete every full-left-lock steering sample
> in the archive.

So the mapping is declared per column, and a column with no declared sentinel gets none.

### 4. Units

The canonical set, which the analysis store always uses: seconds for durations, metres for distance,
m·s⁻¹ for speed, kPa for pressure, °C for temperature, litres for fuel, and `0.0`–`1.0` for
fractions such as tyre wear and pedal input. Each simulator's mapping records only its deviations.

## A worked example

The same lap, as two simulators store it and as the translator emits it. ACC's column names are
illustrative — no ACC connector exists yet, and this record does not assume one.

| Field | RaceRoom database | ACC database | Analysis store |
|---|---|---|---|
| driver | `drivers.sim_driver_id = '4242'` | `drivers.sim_driver_id = 'S8821'` | `person_id = 7` (via `person_sim_alias`) |
| session | `session_type = 10` (raw code) | `session_kind = 3` | `session_type = 'Race'` |
| lap time | `lap_time = 102.5` s | `lap_time_ms = 102500` | `lap_time_s = 102.5` |
| tyre wear rate | `tyre_wear_rate = 3` | `tyre_wear = 'x3'` | `tyre_wear_multiplier = 3.0` |
| tyre wear rate off | `tyre_wear_rate = 0` | `tyre_wear = 'off'` | `tyre_wear_multiplier = 0.0` |
| tyre wear rate N/A | `tyre_wear_rate = -1` | *(column absent)* | `tyre_wear_multiplier = NULL` |
| damage, unreported | `extras.damage.engine = -1` | *(not exposed)* | `engine_damage = NULL` |
| steering, full left | `steer_input_raw = -1.0` | `steer = -1.0` | `steering = -1.0` ← **not null** |
| track | `tracks.name = 'Spa'` | `tracks.name = 'Spa-Francorchamps'` | `track_id = 12` (curated alias) |

Two rows in that table carry the whole design. `tyre_wear_rate = -1` becomes `NULL` because `-1` is
that field's declared sentinel; `steer_input_raw = -1.0` stays `-1.0` because steering has none. A
global rule gets one of them wrong, and it is not obvious which until someone trains a model on it.

Track and car aliasing has the same shape as driver identity — asserted, not inferred — but is
lower-stakes and can start empty, with unmapped tracks simply absent from cross-sim comparison.

## Consequences

- Cross-simulator analysis and ML get a single, flat, versioned dataset — better for training than
  the federated alternative would have been, which is the one genuine upside of arriving here.
- The analysis store is derived, so a translator bug is repaired by rebuilding rather than by
  correcting rows. Raw telemetry stays immutable.
- Cross-sim results lag ingest by one translator run. Acceptable: nothing cross-simulator is a live
  concern, and the live path never touches this.
- **The identity registry is manual.** That is a real ongoing cost and the most likely thing to rot.
  It is also the only honest option — the alternative is guessing which humans are the same person.

## Open questions

- Storage format: PostgreSQL or a columnar format (Parquet) for the analysis store. Training favours
  columnar; ad-hoc SQL favours PostgreSQL.
- Whether the translator runs on a schedule or on demand after a session is archived.
- Whether the live dashboard's future historical read API reads the analysis store or each
  simulator's own read API. Per-sim is simpler and always current; the analysis store is the only
  one that can answer a cross-sim question.
