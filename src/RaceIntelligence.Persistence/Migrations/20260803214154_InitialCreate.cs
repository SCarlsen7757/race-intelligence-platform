using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceIntelligence.Persistence.Migrations
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
                    display_name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drivers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "games",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_games", x => x.id);
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
                name: "game_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_version = table.Column<string>(type: "text", nullable: true),
                    api_version_major = table.Column<int>(type: "integer", nullable: false),
                    api_version_minor = table.Column<int>(type: "integer", nullable: false),
                    connector_version = table.Column<string>(type: "text", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_game_versions_games_game_id",
                        column: x => x.game_id,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tracks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    sim_track_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tracks", x => x.id);
                    table.ForeignKey(
                        name: "FK_tracks_games_game_id",
                        column: x => x.game_id,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cars",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    manufacturer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    car_class_id = table.Column<Guid>(type: "uuid", nullable: true),
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                        name: "FK_cars_games_game_id",
                        column: x => x.game_id,
                        principalTable: "games",
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
                    length_meters = table.Column<double>(type: "double precision", nullable: false),
                    sim_layout_id = table.Column<string>(type: "text", nullable: true)
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
                    track_layout_id = table.Column<Guid>(type: "uuid", nullable: true),
                    car_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_type = table.Column<short>(type: "smallint", nullable: false),
                    capabilities = table.Column<long>(type: "bigint", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    weather = table.Column<string>(type: "jsonb", nullable: true),
                    setup = table.Column<string>(type: "jsonb", nullable: true),
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
                    steering = table.Column<float>(type: "real", nullable: false),
                    gear = table.Column<short>(type: "smallint", nullable: false),
                    engine_rpm = table.Column<float>(type: "real", nullable: false),
                    fuel_left = table.Column<float>(type: "real", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: true),
                    track_position_fraction = table.Column<float>(type: "real", nullable: true),
                    wheel_speed = table.Column<float[]>(type: "real[]", nullable: false),
                    suspension_travel = table.Column<float[]>(type: "real[]", nullable: false),
                    tyre_pressure = table.Column<float?[]>(type: "real[]", nullable: true),
                    tyre_wear = table.Column<float?[]>(type: "real[]", nullable: true),
                    tyre_temperature = table.Column<string>(type: "jsonb", nullable: false),
                    extras = table.Column<string>(type: "jsonb", nullable: false)
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
                name: "IX_cars_game_id_sim_car_id",
                table: "cars",
                columns: new[] { "game_id", "sim_car_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cars_manufacturer_id",
                table: "cars",
                column: "manufacturer_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_versions_game_id_game_version_api_version_major_api_ve~",
                table: "game_versions",
                columns: new[] { "game_id", "game_version", "api_version_major", "api_version_minor", "connector_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_games_key",
                table: "games",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_manufacturers_name",
                table: "manufacturers",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sessions_car_id",
                table: "sessions",
                column: "car_id");

            migrationBuilder.CreateIndex(
                name: "IX_sessions_driver_id",
                table: "sessions",
                column: "driver_id");

            migrationBuilder.CreateIndex(
                name: "IX_sessions_track_layout_id",
                table: "sessions",
                column: "track_layout_id");

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
                name: "IX_tracks_game_id_name",
                table: "tracks",
                columns: new[] { "game_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "laps");

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

            migrationBuilder.DropTable(
                name: "games");
        }
    }
}
