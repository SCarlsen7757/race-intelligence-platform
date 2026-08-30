-- Backfill for #109: the JSON telemetry table becomes the typed one.
--
-- DISPOSABLE. This is a one-off for the only real telemetry that exists — five sessions and 357,152
-- samples recorded at Brands Hatch on 2026-08-24. It is not a migration, it will never run twice,
-- and it should be deleted once it has been run and the result checked. A migration would imply the
-- platform expects to meet this shape again; it does not, and pre-v1 (see CLAUDE.md) it never will.
--
--   pg_dump FIRST. There is no other copy of this data.
--
--     pg_dump --format=custom --file=raceroom-pre-109.dump <connection>
--
-- Run it against a database still on the pre-#109 schema. The migration was edited in place, so EF
-- will not migrate this database — its __EFMigrationsHistory already names the migration whose
-- contents changed underneath it. That is why this script does the schema change itself.
--
-- Everything below is server-side. None of the 724 MB crosses the network.
--
-- WHAT DOES NOT BACKFILL
--
-- The ~44 vehicle-dynamics columns — acceleration, camber, ride height, suspension velocity,
-- downforce, world position — stay NULL, and no script can do better. The connector never read
-- R3EPlayerData, so those channels were never captured (#104). The traction circle, camber against
-- lateral G and the speed-coloured track map need a newly recorded session, not a backfill.
--
-- THE TREAD CORRECTION IS ALREADY APPLIED. DO NOT REPEAT IT.
--
-- On 2026-08-30 an UPDATE swapped Inner/Outer inside tyre_temperature for FrontLeft and RearLeft
-- across all 357,152 rows, so every wheel now reads inner-hotter as negative camber requires (#107,
-- #108). This script copies the stored values straight across. Swapping again would put both
-- left-side tyres back the way #107 found them, and the symptom — a camber story told backwards on
-- one side of the car — is exactly the kind of wrong that looks plausible.

BEGIN;

-- 1. Put the old table aside rather than dropping it. Nothing is deleted until the counts at the
--    bottom have been read by a human.
ALTER TABLE telemetry_samples RENAME TO telemetry_samples_pre_109;
ALTER INDEX ix_telemetry_session_lap RENAME TO ix_telemetry_session_lap_pre_109;
ALTER INDEX ix_telemetry_timestamp_brin RENAME TO ix_telemetry_timestamp_brin_pre_109;

-- 2. Create the new shape, taken verbatim from the generated schema dump. Run this from tools/, or
--    adjust the path: psql resolves \i relative to the working directory, not to this file.
\i backfill-109-tables.sql

-- 3. Move the rows.
--
--    Sentinels die here, exactly as they now die in the connector: NULLIF(x, -1) for the channels
--    whose -1 means "not available", and nothing at all for the channels where a negative is a real
--    reading (steering, camber, gear). A column left out of the list below is one nothing can fill.
INSERT INTO telemetry_samples (
    session_id, timestamp, sequence_number, simulation_time,
    lap_number, sector, speed, throttle, brake, clutch, steering, gear, engine_rpm, fuel_left,
    position, track_position_fraction,
    tyre_grip_fl, tyre_grip_fr, tyre_grip_rl, tyre_grip_rr,
    tyre_load_newtons_fl, tyre_load_newtons_fr, tyre_load_newtons_rl, tyre_load_newtons_rr,
    tyre_dirt_fl, tyre_dirt_fr, tyre_dirt_rl, tyre_dirt_rr,
    tyre_flatspot_fl, tyre_flatspot_fr, tyre_flatspot_rl, tyre_flatspot_rr,
    tyre_surface_material_fl, tyre_surface_material_fr, tyre_surface_material_rl, tyre_surface_material_rr,
    tyre_rotation_radians_per_second_fl, tyre_rotation_radians_per_second_fr,
    tyre_rotation_radians_per_second_rl, tyre_rotation_radians_per_second_rr,
    wheel_speed_fl, wheel_speed_fr, wheel_speed_rl, wheel_speed_rr,
    tyre_pressure_fl, tyre_pressure_fr, tyre_pressure_rl, tyre_pressure_rr,
    tyre_wear_fl, tyre_wear_fr, tyre_wear_rl, tyre_wear_rr,
    tyre_temp_fl_inner, tyre_temp_fl_middle, tyre_temp_fl_outer,
    tyre_temp_fr_inner, tyre_temp_fr_middle, tyre_temp_fr_outer,
    tyre_temp_rl_inner, tyre_temp_rl_middle, tyre_temp_rl_outer,
    tyre_temp_rr_inner, tyre_temp_rr_middle, tyre_temp_rr_outer,
    tyre_type_front, tyre_type_rear, tyre_subtype_front, tyre_subtype_rear,
    brake_temp_fl, brake_temp_fr, brake_temp_rl, brake_temp_rr,
    suspension_travel_fl, suspension_travel_fr, suspension_travel_rl, suspension_travel_rr,
    engine_temp_celsius, engine_oil_temp_celsius, engine_oil_pressure_kpa,
    fuel_pressure_kpa, turbo_pressure_bar, engine_map_setting, engine_brake_setting,
    battery_state_of_charge_percent, virtual_energy_left_mj,
    virtual_energy_capacity_mj, virtual_energy_per_lap_mj,
    abs_setting, traction_control_setting, traction_control_percent, control_type,
    push_to_pass_available, push_to_pass_engaged, push_to_pass_amount_left,
    push_to_pass_engaged_time_left_seconds, push_to_pass_wait_time_left_seconds,
    drs_equipped, drs_available, drs_engaged,
    drs_activations_left, drs_activations_unlimited, drs_activations_total,
    pit_window_status, pit_window_start, pit_window_end, pit_state, pit_action,
    pit_stops_performed, pit_total_duration_seconds, pit_elapsed_time_seconds,
    flag_yellow, flag_blue, flag_black, flag_green, flag_checkered, flag_white, flag_black_and_white,
    damage_engine, damage_transmission, damage_aerodynamics, damage_suspension,
    incident_points, max_incident_points, cut_track_warnings
)
SELECT
    s.session_id, s.timestamp, s.sequence_number, s.simulation_time,
    s.lap_number, s.sector, s.speed, s.throttle, s.brake, s.clutch, s.steering, s.gear,
    s.engine_rpm, s.fuel_left, s.position, s.track_position_fraction,

    -- Per-tyre channels out of the extras document. jsonb arrays are 0-indexed with ->, unlike the
    -- real[] columns further down, which are 1-indexed. Both appear in this statement.
    NULLIF((s.extras -> 'tyreGrip' ->> 0)::real, -1),
    NULLIF((s.extras -> 'tyreGrip' ->> 1)::real, -1),
    NULLIF((s.extras -> 'tyreGrip' ->> 2)::real, -1),
    NULLIF((s.extras -> 'tyreGrip' ->> 3)::real, -1),
    NULLIF((s.extras -> 'tyreLoadNewtons' ->> 0)::real, -1),
    NULLIF((s.extras -> 'tyreLoadNewtons' ->> 1)::real, -1),
    NULLIF((s.extras -> 'tyreLoadNewtons' ->> 2)::real, -1),
    NULLIF((s.extras -> 'tyreLoadNewtons' ->> 3)::real, -1),
    NULLIF((s.extras -> 'tyreDirt' ->> 0)::real, -1),
    NULLIF((s.extras -> 'tyreDirt' ->> 1)::real, -1),
    NULLIF((s.extras -> 'tyreDirt' ->> 2)::real, -1),
    NULLIF((s.extras -> 'tyreDirt' ->> 3)::real, -1),

    -- A tri-state, not a fraction: -1 N/A, 0 false, 1 true.
    NULLIF((s.extras -> 'tyreFlatspot' ->> 0)::smallint, -1),
    NULLIF((s.extras -> 'tyreFlatspot' ->> 1)::smallint, -1),
    NULLIF((s.extras -> 'tyreFlatspot' ->> 2)::smallint, -1),
    NULLIF((s.extras -> 'tyreFlatspot' ->> 3)::smallint, -1),
    NULLIF((s.extras -> 'tyreSurfaceMaterial' ->> 0)::smallint, -1),
    NULLIF((s.extras -> 'tyreSurfaceMaterial' ->> 1)::smallint, -1),
    NULLIF((s.extras -> 'tyreSurfaceMaterial' ->> 2)::smallint, -1),
    NULLIF((s.extras -> 'tyreSurfaceMaterial' ->> 3)::smallint, -1),

    -- Rotation is left unfiltered, as the connector leaves it: the header documents no sentinel and
    -- a negative is a legitimate reading.
    (s.extras -> 'tyreRotationRadiansPerSecond' ->> 0)::real,
    (s.extras -> 'tyreRotationRadiansPerSecond' ->> 1)::real,
    (s.extras -> 'tyreRotationRadiansPerSecond' ->> 2)::real,
    (s.extras -> 'tyreRotationRadiansPerSecond' ->> 3)::real,

    -- **Negated.** Every stored wheel speed is negative for a car driving forwards — 120,918 rows
    -- negative and none positive, magnitude matching road speed. Left alone, the archive would
    -- disagree with everything recorded after #109 and wheel slip would stay uncomputable across the
    -- join. No sentinel filter, for the reason the connector gives: -1 is a real wheel speed.
    -s.wheel_speed[1], -s.wheel_speed[2], -s.wheel_speed[3], -s.wheel_speed[4],

    s.tyre_pressure[1], s.tyre_pressure[2], s.tyre_pressure[3], s.tyre_pressure[4],
    s.tyre_wear[1], s.tyre_wear[2], s.tyre_wear[3], s.tyre_wear[4],

    -- Tread temperatures, copied straight across. The Inner/Outer correction is ALREADY IN THE
    -- STORED DATA — see the header. Nothing here swaps anything.
    (s.tyre_temperature -> 'FrontLeft' ->> 'Inner')::real,
    (s.tyre_temperature -> 'FrontLeft' ->> 'Middle')::real,
    (s.tyre_temperature -> 'FrontLeft' ->> 'Outer')::real,
    (s.tyre_temperature -> 'FrontRight' ->> 'Inner')::real,
    (s.tyre_temperature -> 'FrontRight' ->> 'Middle')::real,
    (s.tyre_temperature -> 'FrontRight' ->> 'Outer')::real,
    (s.tyre_temperature -> 'RearLeft' ->> 'Inner')::real,
    (s.tyre_temperature -> 'RearLeft' ->> 'Middle')::real,
    (s.tyre_temperature -> 'RearLeft' ->> 'Outer')::real,
    (s.tyre_temperature -> 'RearRight' ->> 'Inner')::real,
    (s.tyre_temperature -> 'RearRight' ->> 'Middle')::real,
    (s.tyre_temperature -> 'RearRight' ->> 'Outer')::real,

    NULLIF((s.extras ->> 'tireTypeFront')::int, -1),
    NULLIF((s.extras ->> 'tireTypeRear')::int, -1),
    -- Already promoted columns, already sentinel-translated by RaceRoomExtrasProjector when they
    -- were written. They are not in the residual document, which is why they come from here.
    s.tyre_subtype_front, s.tyre_subtype_rear,

    NULLIF((s.extras -> 'brakeTemperatureCelsius' -> 0 ->> 'current')::real, -1),
    NULLIF((s.extras -> 'brakeTemperatureCelsius' -> 1 ->> 'current')::real, -1),
    NULLIF((s.extras -> 'brakeTemperatureCelsius' -> 2 ->> 'current')::real, -1),
    NULLIF((s.extras -> 'brakeTemperatureCelsius' -> 3 ->> 'current')::real, -1),

    s.suspension_travel[1], s.suspension_travel[2], s.suspension_travel[3], s.suspension_travel[4],

    NULLIF((s.extras ->> 'engineTempCelsius')::real, -1),
    NULLIF((s.extras ->> 'engineOilTempCelsius')::real, -1),
    NULLIF((s.extras ->> 'engineOilPressureKpa')::real, -1),
    NULLIF((s.extras ->> 'fuelPressureKpa')::real, -1),
    NULLIF((s.extras ->> 'turboPressureBar')::real, -1),
    NULLIF((s.extras ->> 'engineMapSetting')::int, -1),
    NULLIF((s.extras ->> 'engineBrakeSetting')::int, -1),
    NULLIF((s.extras ->> 'batteryStateOfChargePercent')::real, -1),
    NULLIF((s.extras ->> 'virtualEnergyLeftMj')::real, -1),
    NULLIF((s.extras ->> 'virtualEnergyCapacityMj')::real, -1),
    NULLIF((s.extras ->> 'virtualEnergyPerLapMj')::real, -1),

    NULLIF((s.extras ->> 'absSetting')::int, -1),
    NULLIF((s.extras ->> 'tractionControlSetting')::int, -1),
    NULLIF((s.extras ->> 'tractionControlPercent')::real, -1),
    NULLIF((s.extras ->> 'controlType')::smallint, -1),

    -- Six channels are absent from this list and stay NULL: abs_active, traction_control_active,
    -- brake_bias and brake_pressure per corner. The connector populated all of them and the live
    -- wire carried them, but the old ingest DTO had no members for them — so they reached a race
    -- engineer's screen and never reached a database. Nothing can backfill what was never sent.

    s.push_to_pass_available, s.push_to_pass_engaged, s.push_to_pass_amount_left,
    s.push_to_pass_engaged_time_left_seconds, s.push_to_pass_wait_time_left_seconds,

    NULLIF((s.extras -> 'drs' ->> 'equipped')::smallint, -1),
    NULLIF((s.extras -> 'drs' ->> 'available')::smallint, -1),
    NULLIF((s.extras -> 'drs' ->> 'engaged')::smallint, -1),
    -- int32::max means *endless* activations, not unavailable, and must not go through the -1 rule:
    -- a strategy screen counting down from 2,147,483,647 is worse than one that says nothing. Both
    -- become NULL, and the flag beside them is what tells them apart.
    CASE
        WHEN (s.extras -> 'drs' ->> 'numActivationsLeft')::bigint IN (-1, 2147483647) THEN NULL
        ELSE (s.extras -> 'drs' ->> 'numActivationsLeft')::int
    END,
    CASE
        WHEN (s.extras -> 'drs' ->> 'numActivationsLeft')::bigint = -1 THEN NULL
        ELSE (s.extras -> 'drs' ->> 'numActivationsLeft')::bigint = 2147483647
    END,
    NULLIF((s.extras -> 'drs' ->> 'numActivationsTotal')::int, -1),

    NULLIF((s.extras -> 'pit' ->> 'windowStatus')::smallint, -1),
    NULLIF((s.extras -> 'pit' ->> 'windowStart')::int, -1),
    NULLIF((s.extras -> 'pit' ->> 'windowEnd')::int, -1),
    NULLIF((s.extras -> 'pit' ->> 'state')::smallint, -1),
    NULLIF((s.extras -> 'pit' ->> 'action')::int, -1),
    NULLIF((s.extras -> 'pit' ->> 'numPitstopsPerformed')::int, -1),
    NULLIF((s.extras -> 'pit' ->> 'totalDurationSeconds')::real, -1),
    NULLIF((s.extras -> 'pit' ->> 'elapsedTimeSeconds')::real, -1),

    NULLIF((s.extras -> 'flags' ->> 'yellow')::smallint, -1),
    NULLIF((s.extras -> 'flags' ->> 'blue')::smallint, -1),
    NULLIF((s.extras -> 'flags' ->> 'black')::smallint, -1),
    NULLIF((s.extras -> 'flags' ->> 'green')::smallint, -1),
    NULLIF((s.extras -> 'flags' ->> 'checkered')::smallint, -1),
    NULLIF((s.extras -> 'flags' ->> 'white')::smallint, -1),
    NULLIF((s.extras -> 'flags' ->> 'blackAndWhite')::smallint, -1),

    s.damage_engine, s.damage_transmission, s.damage_aerodynamics, s.damage_suspension,

    NULLIF((s.extras ->> 'incidentPoints')::int, -1),
    NULLIF((s.extras ->> 'maxIncidentPoints')::int, -1),
    s.cut_track_warnings
FROM telemetry_samples_pre_109 AS s;

-- 4. The operating windows, one row per session, corner and compound.
--
--    DISTINCT rather than a GROUP BY with an aggregate, because there is nothing to aggregate: the
--    bounds were measured to have exactly one distinct value each per session, which is the finding
--    that moved them out of the sample in the first place. If this inserts more rows than
--    4 x sessions, a compound changed mid-session and the extra rows are correct.
INSERT INTO operating_windows (
    session_id, corner, compound,
    tyre_optimal_celsius, tyre_cold_celsius, tyre_hot_celsius,
    brake_optimal_celsius, brake_cold_celsius, brake_hot_celsius
)
SELECT DISTINCT
    s.session_id,
    corner.ordinal,
    CASE WHEN corner.ordinal < 2 THEN s.tyre_subtype_front ELSE s.tyre_subtype_rear END,
    NULLIF((s.tyre_temperature -> corner.name ->> 'Optimal')::real, -1),
    NULLIF((s.tyre_temperature -> corner.name ->> 'Cold')::real, -1),
    NULLIF((s.tyre_temperature -> corner.name ->> 'Hot')::real, -1),
    NULLIF((s.extras -> 'brakeTemperatureCelsius' -> corner.ordinal ->> 'optimal')::real, -1),
    NULLIF((s.extras -> 'brakeTemperatureCelsius' -> corner.ordinal ->> 'cold')::real, -1),
    NULLIF((s.extras -> 'brakeTemperatureCelsius' -> corner.ordinal ->> 'hot')::real, -1)
FROM telemetry_samples_pre_109 AS s
CROSS JOIN (VALUES
    (0, 'FrontLeft'), (1, 'FrontRight'), (2, 'RearLeft'), (3, 'RearRight')
) AS corner(ordinal, name);

-- 5. Weather and setup were NULL on every row and the columns are gone.
ALTER TABLE sessions DROP COLUMN IF EXISTS weather;
ALTER TABLE sessions DROP COLUMN IF EXISTS setup;

COMMIT;

-- 6. Read these before dropping anything.
--
--    The first two must match. The third should be about a third of the fourth — the row got
--    smaller by roughly 3x, which is the whole point of the exercise.
SELECT
    (SELECT count(*) FROM telemetry_samples_pre_109) AS rows_before,
    (SELECT count(*) FROM telemetry_samples)         AS rows_after,
    pg_size_pretty(pg_total_relation_size('telemetry_samples'))         AS size_after,
    pg_size_pretty(pg_total_relation_size('telemetry_samples_pre_109')) AS size_before,
    (SELECT count(*) FROM operating_windows)         AS window_rows;

-- Sanity checks worth running by eye before the DROP:
--
--   -- Wheel speed is now positive and close to road speed.
--   SELECT min(wheel_speed_fl), max(wheel_speed_fl), avg(wheel_speed_fl - speed)
--   FROM telemetry_samples WHERE speed > 20;
--
--   -- Every tyre reads inner-hotter, on every corner, as negative camber requires (#107).
--   SELECT avg(tyre_temp_fl_inner - tyre_temp_fl_outer), avg(tyre_temp_fr_inner - tyre_temp_fr_outer),
--          avg(tyre_temp_rl_inner - tyre_temp_rl_outer), avg(tyre_temp_rr_inner - tyre_temp_rr_outer)
--   FROM telemetry_samples;
--
--   -- The dynamics columns are the ones that cannot be backfilled, and should be entirely null.
--   SELECT count(camber_fl), count(acceleration_lateral), count(world_position_x) FROM telemetry_samples;
--
-- Then, and only then:
--
--   DROP TABLE telemetry_samples_pre_109;
