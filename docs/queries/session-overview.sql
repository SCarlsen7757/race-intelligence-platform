-- Recent sessions with readable track/car/driver names and lap/sample counts.
-- Run against the raceintel database (see docs/development.md for connection info).
SELECT
    s.id                                                AS session_id,
    s.started_at,
    s.ended_at,
    d.display_name                                      AS driver,
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
