-- Recent sessions with readable track/car/driver names and lap/sample counts.
-- Run against the raceintel database (see docs/development.md for connection info).
SELECT
    s.id                                                AS session_id,
    s.started_at,
    s.ended_at,
    d.display_name                                      AS driver,
    -- The sim's own stable driver id, scoped by game. This, not display_name, is what ties a
    -- person's sessions together after they rename themselves.
    d.sim_driver_id,
    -- The name reported for this session specifically; d.display_name tracks the latest one.
    s.player_name,
    -- Session rules, as the sim's raw rate code: -1 = N/A, 0 = off, 1-4 = 1x-4x for RaceRoom.
    -- Independent of each other. Note -1 sorts below 0 — use "> 0" to mean "the rate was on".
    s.fuel_usage_rate,
    s.tyre_wear_rate,
    t.name                                               AS track,
    tl.name                                              AS layout,
    c.name                                               AS car,
    -- session_type is the sim's raw session-type value, untranslated (the collector performs no
    -- analysis and doesn't know the canonical mapping) — a later analysis pass, which does know
    -- the sim, is expected to rewrite it to the canonical numbering.
    s.session_type,
    s.sim_car_id,
    s.sim_car_class_id,
    s.sim_manufacturer_id,
    lap_counts.lap_count,
    sample_counts.sample_count,
    sample_counts.latest_sample
FROM sessions s
LEFT JOIN drivers d ON d.id = s.driver_id
LEFT JOIN track_layouts tl ON tl.id = s.track_layout_id
LEFT JOIN tracks t ON t.id = tl.track_id
LEFT JOIN cars c ON c.id = s.car_id
LEFT JOIN LATERAL (
    SELECT count(*) AS lap_count
    FROM laps l
    WHERE l.session_id = s.id
) lap_counts ON true
LEFT JOIN LATERAL (
    SELECT count(*) AS sample_count, max(ts.timestamp) AS latest_sample
    FROM telemetry_samples ts
    WHERE ts.session_id = s.id
) sample_counts ON true
ORDER BY s.started_at DESC
LIMIT 25;
