CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE car_classes (
    id uuid NOT NULL,
    name text NOT NULL,
    CONSTRAINT "PK_car_classes" PRIMARY KEY (id)
);

CREATE TABLE drivers (
    id uuid NOT NULL,
    sim_driver_id text,
    display_name text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_drivers" PRIMARY KEY (id)
);

CREATE TABLE game_versions (
    id uuid NOT NULL,
    game_version text,
    api_version_major integer NOT NULL,
    api_version_minor integer NOT NULL,
    connector_version text NOT NULL,
    first_seen_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_game_versions" PRIMARY KEY (id)
);

CREATE TABLE manufacturers (
    id uuid NOT NULL,
    name text NOT NULL,
    CONSTRAINT "PK_manufacturers" PRIMARY KEY (id)
);

CREATE TABLE tracks (
    id uuid NOT NULL,
    name text NOT NULL,
    CONSTRAINT "PK_tracks" PRIMARY KEY (id)
);

CREATE TABLE cars (
    id uuid NOT NULL,
    name text NOT NULL,
    manufacturer_id uuid,
    car_class_id uuid,
    sim_car_id text NOT NULL,
    CONSTRAINT "PK_cars" PRIMARY KEY (id),
    CONSTRAINT "FK_cars_car_classes_car_class_id" FOREIGN KEY (car_class_id) REFERENCES car_classes (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_cars_manufacturers_manufacturer_id" FOREIGN KEY (manufacturer_id) REFERENCES manufacturers (id) ON DELETE RESTRICT
);

CREATE TABLE track_layouts (
    id uuid NOT NULL,
    track_id uuid NOT NULL,
    name text NOT NULL,
    length_meters double precision NOT NULL,
    CONSTRAINT "PK_track_layouts" PRIMARY KEY (id),
    CONSTRAINT "FK_track_layouts_tracks_track_id" FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE RESTRICT
);

CREATE TABLE sessions (
    id uuid NOT NULL,
    game_version_id uuid NOT NULL,
    driver_id uuid,
    player_name text,
    track_layout_id uuid,
    car_id uuid,
    sim_car_id text,
    sim_car_class_id text,
    sim_manufacturer_id text,
    session_type smallint NOT NULL,
    fuel_usage_rate smallint,
    tyre_wear_rate smallint,
    capabilities bigint NOT NULL,
    schema_version integer NOT NULL,
    weather jsonb,
    setup jsonb,
    extras jsonb NOT NULL,
    started_at timestamp with time zone NOT NULL,
    ended_at timestamp with time zone,
    CONSTRAINT "PK_sessions" PRIMARY KEY (id),
    CONSTRAINT "FK_sessions_cars_car_id" FOREIGN KEY (car_id) REFERENCES cars (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_sessions_drivers_driver_id" FOREIGN KEY (driver_id) REFERENCES drivers (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_sessions_game_versions_game_version_id" FOREIGN KEY (game_version_id) REFERENCES game_versions (id) ON DELETE RESTRICT,
    CONSTRAINT "FK_sessions_track_layouts_track_layout_id" FOREIGN KEY (track_layout_id) REFERENCES track_layouts (id) ON DELETE RESTRICT
);

CREATE TABLE laps (
    session_id uuid NOT NULL,
    lap_number integer NOT NULL,
    lap_time interval,
    fuel_used real,
    avg_speed real,
    max_speed real,
    quality_score real,
    is_valid boolean NOT NULL,
    CONSTRAINT "PK_laps" PRIMARY KEY (session_id, lap_number),
    CONSTRAINT "FK_laps_sessions_session_id" FOREIGN KEY (session_id) REFERENCES sessions (id) ON DELETE RESTRICT
);

CREATE TABLE telemetry_samples (
    session_id uuid NOT NULL,
    timestamp timestamp with time zone NOT NULL,
    sequence_number bigint NOT NULL,
    simulation_time double precision NOT NULL,
    lap_number integer NOT NULL,
    sector integer NOT NULL,
    speed real NOT NULL,
    throttle real,
    brake real,
    clutch real,
    steering real NOT NULL,
    gear smallint,
    engine_rpm real NOT NULL,
    fuel_left real NOT NULL,
    position smallint,
    track_position_fraction real,
    wheel_speed real[] NOT NULL,
    suspension_travel real[] NOT NULL,
    tyre_pressure real[],
    tyre_wear real[],
    tyre_temperature jsonb NOT NULL,
    push_to_pass_available integer,
    push_to_pass_engaged integer,
    push_to_pass_amount_left integer,
    push_to_pass_engaged_time_left_seconds real,
    push_to_pass_wait_time_left_seconds real,
    tyre_subtype_front integer,
    tyre_subtype_rear integer,
    cut_track_warnings integer,
    damage_engine real,
    damage_transmission real,
    damage_aerodynamics real,
    damage_suspension real,
    extras jsonb NOT NULL,
    CONSTRAINT "PK_telemetry_samples" PRIMARY KEY (session_id, timestamp, sequence_number),
    CONSTRAINT "FK_telemetry_samples_sessions_session_id" FOREIGN KEY (session_id) REFERENCES sessions (id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_car_classes_name" ON car_classes (name);

CREATE INDEX "IX_cars_car_class_id" ON cars (car_class_id);

CREATE INDEX "IX_cars_manufacturer_id" ON cars (manufacturer_id);

CREATE UNIQUE INDEX "IX_cars_sim_car_id" ON cars (sim_car_id);

CREATE UNIQUE INDEX "IX_drivers_display_name" ON drivers (display_name) WHERE sim_driver_id IS NULL;

CREATE UNIQUE INDEX "IX_drivers_sim_driver_id" ON drivers (sim_driver_id);

CREATE UNIQUE INDEX "IX_game_versions_game_version_api_version_major_api_version_mi~" ON game_versions (game_version, api_version_major, api_version_minor, connector_version);

CREATE UNIQUE INDEX "IX_manufacturers_name" ON manufacturers (name);

CREATE INDEX "IX_sessions_car_id" ON sessions (car_id);

CREATE INDEX "IX_sessions_track_layout_id" ON sessions (track_layout_id);

CREATE INDEX ix_sessions_driver_wear_rates ON sessions (driver_id, tyre_wear_rate, fuel_usage_rate);

CREATE INDEX ix_sessions_game_version ON sessions (game_version_id);

CREATE INDEX ix_telemetry_session_lap ON telemetry_samples (session_id, lap_number);

CREATE INDEX ix_telemetry_timestamp_brin ON telemetry_samples USING brin (timestamp);

CREATE UNIQUE INDEX "IX_track_layouts_track_id_name" ON track_layouts (track_id, name);

CREATE UNIQUE INDEX "IX_tracks_name" ON tracks (name);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260820174838_InitialCreate', '10.0.11');

COMMIT;

