using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RaceIntelligence.Persistence.RaceRoom.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "car_classes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_car_classes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "drivers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sim_driver_id = table.Column<string>(type: "text", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drivers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "game_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_version = table.Column<string>(type: "text", nullable: true),
                    api_version_major = table.Column<int>(type: "integer", nullable: false),
                    api_version_minor = table.Column<int>(type: "integer", nullable: false),
                    connector_version = table.Column<string>(type: "text", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "manufacturers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manufacturers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tracks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tracks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cars",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    manufacturer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    car_class_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sim_car_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cars", x => x.id);
                    table.ForeignKey(
                        name: "FK_cars_car_classes_car_class_id",
                        column: x => x.car_class_id,
                        principalTable: "car_classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cars_manufacturers_manufacturer_id",
                        column: x => x.manufacturer_id,
                        principalTable: "manufacturers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "track_layouts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    length_meters = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_track_layouts", x => x.id);
                    table.ForeignKey(
                        name: "FK_track_layouts_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: true),
                    player_name = table.Column<string>(type: "text", nullable: true),
                    track_layout_id = table.Column<Guid>(type: "uuid", nullable: true),
                    car_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sim_car_id = table.Column<string>(type: "text", nullable: true),
                    sim_car_class_id = table.Column<string>(type: "text", nullable: true),
                    sim_manufacturer_id = table.Column<string>(type: "text", nullable: true),
                    session_type = table.Column<short>(type: "smallint", nullable: false),
                    fuel_usage_rate = table.Column<short>(type: "smallint", nullable: true),
                    tyre_wear_rate = table.Column<short>(type: "smallint", nullable: true),
                    capabilities = table.Column<long>(type: "bigint", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    extras = table.Column<string>(type: "jsonb", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_sessions_cars_car_id",
                        column: x => x.car_id,
                        principalTable: "cars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sessions_drivers_driver_id",
                        column: x => x.driver_id,
                        principalTable: "drivers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sessions_game_versions_game_version_id",
                        column: x => x.game_version_id,
                        principalTable: "game_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sessions_track_layouts_track_layout_id",
                        column: x => x.track_layout_id,
                        principalTable: "track_layouts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "laps",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lap_number = table.Column<int>(type: "integer", nullable: false),
                    lap_time = table.Column<TimeSpan>(type: "interval", nullable: true),
                    fuel_used = table.Column<float>(type: "real", nullable: true),
                    avg_speed = table.Column<float>(type: "real", nullable: true),
                    max_speed = table.Column<float>(type: "real", nullable: true),
                    quality_score = table.Column<float>(type: "real", nullable: true),
                    is_valid = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_laps", x => new { x.session_id, x.lap_number });
                    table.ForeignKey(
                        name: "FK_laps_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "operating_windows",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    corner = table.Column<short>(type: "smallint", nullable: false),
                    compound = table.Column<int>(type: "integer", nullable: true),
                    tyre_optimal_celsius = table.Column<float>(type: "real", nullable: true),
                    tyre_cold_celsius = table.Column<float>(type: "real", nullable: true),
                    tyre_hot_celsius = table.Column<float>(type: "real", nullable: true),
                    brake_optimal_celsius = table.Column<float>(type: "real", nullable: true),
                    brake_cold_celsius = table.Column<float>(type: "real", nullable: true),
                    brake_hot_celsius = table.Column<float>(type: "real", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operating_windows", x => x.id);
                    table.ForeignKey(
                        name: "FK_operating_windows_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "telemetry_samples",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sequence_number = table.Column<long>(type: "bigint", nullable: false),
                    simulation_time = table.Column<double>(type: "double precision", nullable: false),
                    lap_number = table.Column<int>(type: "integer", nullable: false),
                    sector = table.Column<int>(type: "integer", nullable: false),
                    speed = table.Column<float>(type: "real", nullable: false),
                    throttle = table.Column<float>(type: "real", nullable: true),
                    brake = table.Column<float>(type: "real", nullable: true),
                    clutch = table.Column<float>(type: "real", nullable: true),
                    steering = table.Column<float>(type: "real", nullable: false),
                    gear = table.Column<short>(type: "smallint", nullable: true),
                    engine_rpm = table.Column<float>(type: "real", nullable: false),
                    fuel_left = table.Column<float>(type: "real", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: true),
                    track_position_fraction = table.Column<float>(type: "real", nullable: true),
                    tyre_grip_fl = table.Column<float>(type: "real", nullable: true),
                    tyre_grip_fr = table.Column<float>(type: "real", nullable: true),
                    tyre_grip_rl = table.Column<float>(type: "real", nullable: true),
                    tyre_grip_rr = table.Column<float>(type: "real", nullable: true),
                    tyre_load_newtons_fl = table.Column<float>(type: "real", nullable: true),
                    tyre_load_newtons_fr = table.Column<float>(type: "real", nullable: true),
                    tyre_load_newtons_rl = table.Column<float>(type: "real", nullable: true),
                    tyre_load_newtons_rr = table.Column<float>(type: "real", nullable: true),
                    tyre_dirt_fl = table.Column<float>(type: "real", nullable: true),
                    tyre_dirt_fr = table.Column<float>(type: "real", nullable: true),
                    tyre_dirt_rl = table.Column<float>(type: "real", nullable: true),
                    tyre_dirt_rr = table.Column<float>(type: "real", nullable: true),
                    tyre_flatspot_fl = table.Column<short>(type: "smallint", nullable: true),
                    tyre_flatspot_fr = table.Column<short>(type: "smallint", nullable: true),
                    tyre_flatspot_rl = table.Column<short>(type: "smallint", nullable: true),
                    tyre_flatspot_rr = table.Column<short>(type: "smallint", nullable: true),
                    tyre_surface_material_fl = table.Column<short>(type: "smallint", nullable: true),
                    tyre_surface_material_fr = table.Column<short>(type: "smallint", nullable: true),
                    tyre_surface_material_rl = table.Column<short>(type: "smallint", nullable: true),
                    tyre_surface_material_rr = table.Column<short>(type: "smallint", nullable: true),
                    tyre_rotation_radians_per_second_fl = table.Column<float>(type: "real", nullable: true),
                    tyre_rotation_radians_per_second_fr = table.Column<float>(type: "real", nullable: true),
                    tyre_rotation_radians_per_second_rl = table.Column<float>(type: "real", nullable: true),
                    tyre_rotation_radians_per_second_rr = table.Column<float>(type: "real", nullable: true),
                    wheel_speed_fl = table.Column<float>(type: "real", nullable: true),
                    wheel_speed_fr = table.Column<float>(type: "real", nullable: true),
                    wheel_speed_rl = table.Column<float>(type: "real", nullable: true),
                    wheel_speed_rr = table.Column<float>(type: "real", nullable: true),
                    tyre_pressure_fl = table.Column<float>(type: "real", nullable: true),
                    tyre_pressure_fr = table.Column<float>(type: "real", nullable: true),
                    tyre_pressure_rl = table.Column<float>(type: "real", nullable: true),
                    tyre_pressure_rr = table.Column<float>(type: "real", nullable: true),
                    tyre_wear_fl = table.Column<float>(type: "real", nullable: true),
                    tyre_wear_fr = table.Column<float>(type: "real", nullable: true),
                    tyre_wear_rl = table.Column<float>(type: "real", nullable: true),
                    tyre_wear_rr = table.Column<float>(type: "real", nullable: true),
                    tyre_temp_fl_inner = table.Column<float>(type: "real", nullable: true),
                    tyre_temp_fl_middle = table.Column<float>(type: "real", nullable: true),
                    tyre_temp_fl_outer = table.Column<float>(type: "real", nullable: true),
                    tyre_temp_fr_inner = table.Column<float>(type: "real", nullable: true),
                    tyre_temp_fr_middle = table.Column<float>(type: "real", nullable: true),
                    tyre_temp_fr_outer = table.Column<float>(type: "real", nullable: true),
                    tyre_temp_rl_inner = table.Column<float>(type: "real", nullable: true),
                    tyre_temp_rl_middle = table.Column<float>(type: "real", nullable: true),
                    tyre_temp_rl_outer = table.Column<float>(type: "real", nullable: true),
                    tyre_temp_rr_inner = table.Column<float>(type: "real", nullable: true),
                    tyre_temp_rr_middle = table.Column<float>(type: "real", nullable: true),
                    tyre_temp_rr_outer = table.Column<float>(type: "real", nullable: true),
                    tyre_type_front = table.Column<int>(type: "integer", nullable: true),
                    tyre_type_rear = table.Column<int>(type: "integer", nullable: true),
                    tyre_subtype_front = table.Column<int>(type: "integer", nullable: true),
                    tyre_subtype_rear = table.Column<int>(type: "integer", nullable: true),
                    brake_temp_fl = table.Column<float>(type: "real", nullable: true),
                    brake_temp_fr = table.Column<float>(type: "real", nullable: true),
                    brake_temp_rl = table.Column<float>(type: "real", nullable: true),
                    brake_temp_rr = table.Column<float>(type: "real", nullable: true),
                    brake_pressure_fl = table.Column<float>(type: "real", nullable: true),
                    brake_pressure_fr = table.Column<float>(type: "real", nullable: true),
                    brake_pressure_rl = table.Column<float>(type: "real", nullable: true),
                    brake_pressure_rr = table.Column<float>(type: "real", nullable: true),
                    brake_bias = table.Column<float>(type: "real", nullable: true),
                    suspension_travel_fl = table.Column<float>(type: "real", nullable: true),
                    suspension_travel_fr = table.Column<float>(type: "real", nullable: true),
                    suspension_travel_rl = table.Column<float>(type: "real", nullable: true),
                    suspension_travel_rr = table.Column<float>(type: "real", nullable: true),
                    suspension_velocity_fl = table.Column<float>(type: "real", nullable: true),
                    suspension_velocity_fr = table.Column<float>(type: "real", nullable: true),
                    suspension_velocity_rl = table.Column<float>(type: "real", nullable: true),
                    suspension_velocity_rr = table.Column<float>(type: "real", nullable: true),
                    ride_height_fl = table.Column<float>(type: "real", nullable: true),
                    ride_height_fr = table.Column<float>(type: "real", nullable: true),
                    ride_height_rl = table.Column<float>(type: "real", nullable: true),
                    ride_height_rr = table.Column<float>(type: "real", nullable: true),
                    camber_fl = table.Column<float>(type: "real", nullable: true),
                    camber_fr = table.Column<float>(type: "real", nullable: true),
                    camber_rl = table.Column<float>(type: "real", nullable: true),
                    camber_rr = table.Column<float>(type: "real", nullable: true),
                    third_spring_travel_front = table.Column<float>(type: "real", nullable: true),
                    third_spring_travel_rear = table.Column<float>(type: "real", nullable: true),
                    third_spring_velocity_front = table.Column<float>(type: "real", nullable: true),
                    third_spring_velocity_rear = table.Column<float>(type: "real", nullable: true),
                    front_roll_angle = table.Column<float>(type: "real", nullable: true),
                    rear_roll_angle = table.Column<float>(type: "real", nullable: true),
                    front_wing_height = table.Column<float>(type: "real", nullable: true),
                    world_position_x = table.Column<double>(type: "double precision", nullable: true),
                    world_position_y = table.Column<double>(type: "double precision", nullable: true),
                    world_position_z = table.Column<double>(type: "double precision", nullable: true),
                    local_velocity_longitudinal = table.Column<float>(type: "real", nullable: true),
                    local_velocity_lateral = table.Column<float>(type: "real", nullable: true),
                    local_velocity_vertical = table.Column<float>(type: "real", nullable: true),
                    acceleration_longitudinal = table.Column<float>(type: "real", nullable: true),
                    acceleration_lateral = table.Column<float>(type: "real", nullable: true),
                    acceleration_vertical = table.Column<float>(type: "real", nullable: true),
                    gforce_longitudinal = table.Column<float>(type: "real", nullable: true),
                    gforce_lateral = table.Column<float>(type: "real", nullable: true),
                    gforce_vertical = table.Column<float>(type: "real", nullable: true),
                    orientation_pitch = table.Column<float>(type: "real", nullable: true),
                    orientation_yaw = table.Column<float>(type: "real", nullable: true),
                    orientation_roll = table.Column<float>(type: "real", nullable: true),
                    angular_acceleration_pitch = table.Column<float>(type: "real", nullable: true),
                    angular_acceleration_yaw = table.Column<float>(type: "real", nullable: true),
                    angular_acceleration_roll = table.Column<float>(type: "real", nullable: true),
                    pitch_rate = table.Column<float>(type: "real", nullable: true),
                    yaw_rate = table.Column<float>(type: "real", nullable: true),
                    roll_rate = table.Column<float>(type: "real", nullable: true),
                    downforce_newtons = table.Column<float>(type: "real", nullable: true),
                    engine_torque_newton_metres = table.Column<float>(type: "real", nullable: true),
                    steering_force = table.Column<float>(type: "real", nullable: true),
                    steering_force_percent = table.Column<float>(type: "real", nullable: true),
                    engine_temp_celsius = table.Column<float>(type: "real", nullable: true),
                    engine_oil_temp_celsius = table.Column<float>(type: "real", nullable: true),
                    engine_oil_pressure_kpa = table.Column<float>(type: "real", nullable: true),
                    fuel_pressure_kpa = table.Column<float>(type: "real", nullable: true),
                    turbo_pressure_bar = table.Column<float>(type: "real", nullable: true),
                    engine_map_setting = table.Column<int>(type: "integer", nullable: true),
                    engine_brake_setting = table.Column<int>(type: "integer", nullable: true),
                    battery_state_of_charge_percent = table.Column<float>(type: "real", nullable: true),
                    virtual_energy_left_mj = table.Column<float>(type: "real", nullable: true),
                    virtual_energy_capacity_mj = table.Column<float>(type: "real", nullable: true),
                    virtual_energy_per_lap_mj = table.Column<float>(type: "real", nullable: true),
                    abs_setting = table.Column<int>(type: "integer", nullable: true),
                    abs_active = table.Column<bool>(type: "boolean", nullable: true),
                    traction_control_setting = table.Column<int>(type: "integer", nullable: true),
                    traction_control_active = table.Column<bool>(type: "boolean", nullable: true),
                    traction_control_percent = table.Column<float>(type: "real", nullable: true),
                    control_type = table.Column<short>(type: "smallint", nullable: true),
                    push_to_pass_available = table.Column<int>(type: "integer", nullable: true),
                    push_to_pass_engaged = table.Column<int>(type: "integer", nullable: true),
                    push_to_pass_amount_left = table.Column<int>(type: "integer", nullable: true),
                    push_to_pass_engaged_time_left_seconds = table.Column<float>(type: "real", nullable: true),
                    push_to_pass_wait_time_left_seconds = table.Column<float>(type: "real", nullable: true),
                    drs_equipped = table.Column<short>(type: "smallint", nullable: true),
                    drs_available = table.Column<short>(type: "smallint", nullable: true),
                    drs_engaged = table.Column<short>(type: "smallint", nullable: true),
                    drs_activations_left = table.Column<int>(type: "integer", nullable: true),
                    drs_activations_unlimited = table.Column<bool>(type: "boolean", nullable: true),
                    drs_activations_total = table.Column<int>(type: "integer", nullable: true),
                    pit_window_status = table.Column<short>(type: "smallint", nullable: true),
                    pit_window_start = table.Column<int>(type: "integer", nullable: true),
                    pit_window_end = table.Column<int>(type: "integer", nullable: true),
                    pit_state = table.Column<short>(type: "smallint", nullable: true),
                    pit_action = table.Column<int>(type: "integer", nullable: true),
                    pit_stops_performed = table.Column<int>(type: "integer", nullable: true),
                    pit_total_duration_seconds = table.Column<float>(type: "real", nullable: true),
                    pit_elapsed_time_seconds = table.Column<float>(type: "real", nullable: true),
                    flag_yellow = table.Column<short>(type: "smallint", nullable: true),
                    flag_blue = table.Column<short>(type: "smallint", nullable: true),
                    flag_black = table.Column<short>(type: "smallint", nullable: true),
                    flag_green = table.Column<short>(type: "smallint", nullable: true),
                    flag_checkered = table.Column<short>(type: "smallint", nullable: true),
                    flag_white = table.Column<short>(type: "smallint", nullable: true),
                    flag_black_and_white = table.Column<short>(type: "smallint", nullable: true),
                    damage_engine = table.Column<float>(type: "real", nullable: true),
                    damage_transmission = table.Column<float>(type: "real", nullable: true),
                    damage_aerodynamics = table.Column<float>(type: "real", nullable: true),
                    damage_suspension = table.Column<float>(type: "real", nullable: true),
                    incident_points = table.Column<int>(type: "integer", nullable: true),
                    max_incident_points = table.Column<int>(type: "integer", nullable: true),
                    cut_track_warnings = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telemetry_samples", x => new { x.session_id, x.timestamp, x.sequence_number });
                    table.ForeignKey(
                        name: "FK_telemetry_samples_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_car_classes_name",
                table: "car_classes",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cars_car_class_id",
                table: "cars",
                column: "car_class_id");

            migrationBuilder.CreateIndex(
                name: "IX_cars_manufacturer_id",
                table: "cars",
                column: "manufacturer_id");

            migrationBuilder.CreateIndex(
                name: "IX_cars_sim_car_id",
                table: "cars",
                column: "sim_car_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_drivers_display_name",
                table: "drivers",
                column: "display_name",
                unique: true,
                filter: "sim_driver_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_drivers_sim_driver_id",
                table: "drivers",
                column: "sim_driver_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_game_versions_game_version_api_version_major_api_version_mi~",
                table: "game_versions",
                columns: new[] { "game_version", "api_version_major", "api_version_minor", "connector_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_manufacturers_name",
                table: "manufacturers",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_operating_windows_session_corner_compound",
                table: "operating_windows",
                columns: new[] { "session_id", "corner", "compound" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_sessions_car_id",
                table: "sessions",
                column: "car_id");

            migrationBuilder.CreateIndex(
                name: "IX_sessions_track_layout_id",
                table: "sessions",
                column: "track_layout_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_driver_wear_rates",
                table: "sessions",
                columns: new[] { "driver_id", "tyre_wear_rate", "fuel_usage_rate" });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_game_version",
                table: "sessions",
                column: "game_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_telemetry_session_lap",
                table: "telemetry_samples",
                columns: new[] { "session_id", "lap_number" });

            migrationBuilder.CreateIndex(
                name: "ix_telemetry_timestamp_brin",
                table: "telemetry_samples",
                column: "timestamp")
                .Annotation("Npgsql:IndexMethod", "brin");

            migrationBuilder.CreateIndex(
                name: "IX_track_layouts_track_id_name",
                table: "track_layouts",
                columns: new[] { "track_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tracks_name",
                table: "tracks",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "laps");

            migrationBuilder.DropTable(
                name: "operating_windows");

            migrationBuilder.DropTable(
                name: "telemetry_samples");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropTable(
                name: "cars");

            migrationBuilder.DropTable(
                name: "drivers");

            migrationBuilder.DropTable(
                name: "game_versions");

            migrationBuilder.DropTable(
                name: "track_layouts");

            migrationBuilder.DropTable(
                name: "car_classes");

            migrationBuilder.DropTable(
                name: "manufacturers");

            migrationBuilder.DropTable(
                name: "tracks");
        }
    }
}
