using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceIntelligence.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverIdentityAndSessionWearRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Not a lost index: ix_sessions_driver_wear_rates below leads with driver_id, so it
            // serves every lookup this one did. EF drops the now-redundant single-column index
            // automatically, which is why this appears as a deletion with no matching addition.
            migrationBuilder.DropIndex(
                name: "IX_sessions_driver_id",
                table: "sessions");

            migrationBuilder.AddColumn<short>(
                name: "fuel_usage_rate",
                table: "sessions",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "player_name",
                table: "sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "tyre_wear_rate",
                table: "sessions",
                type: "smallint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_driver_wear_rates",
                table: "sessions",
                columns: new[] { "driver_id", "tyre_wear_rate", "fuel_usage_rate" });

            // drivers.game_id is NOT NULL in the model, but this migration runs against databases
            // that already hold driver rows from live collection. Adding it non-nullable in one
            // step would either fail outright or silently stamp every existing driver with a
            // sentinel game that no games row matches, breaking the foreign key. Hence the
            // three-step dance below — add nullable, backfill from real data, then tighten.
            // Do not "simplify" this back into a single AddColumn.

            // Step 1: add nullable so existing rows are accepted.
            migrationBuilder.AddColumn<Guid>(
                name: "game_id",
                table: "drivers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sim_driver_id",
                table: "drivers",
                type: "text",
                nullable: true);

            // Step 2: backfill each driver's game from its own sessions. Ordered by the session's
            // start time rather than left to the planner, so re-running this migration on a copy of
            // the same database produces the same assignment. A driver whose sessions span two
            // games — possible, because driver resolution before this migration matched on display
            // name alone with no game scoping — is assigned its earliest game here; sessions of the
            // other game keep pointing at it until that game's next session resolves a row of its
            // own. Nothing is lost, but such a driver is worth reviewing by hand.
            migrationBuilder.Sql(
                """
                UPDATE drivers d
                SET game_id = (
                    SELECT gv.game_id
                    FROM sessions s
                    JOIN game_versions gv ON gv.id = s.game_version_id
                    WHERE s.driver_id = d.id
                    ORDER BY s.started_at
                    LIMIT 1);
                """);

            // Step 2b: a driver with no sessions at all has no game to inherit and would be left
            // NULL, aborting the AlterColumn below and taking the whole migration with it. Such a
            // row carries no telemetry — it is the residue of a session create that committed the
            // driver (its own SaveChanges) and then failed before the session row landed, which a
            // dropped connection or a restart mid-testing can produce. Deleting it discards no
            // recorded data; the FK from sessions guarantees nothing references it.
            migrationBuilder.Sql(
                """
                DELETE FROM drivers d
                WHERE NOT EXISTS (SELECT 1 FROM sessions s WHERE s.driver_id = d.id);
                """);

            // Step 3: now that every row carries a value, tighten to NOT NULL and add the
            // constraints that depend on it.
            //
            // IX_drivers_game_id_display_name below is UNIQUE over rows with a null sim_driver_id —
            // which, immediately after this migration, is every row. `drivers` has never had a
            // uniqueness constraint on display_name, and the resolve-or-create it replaces was
            // explicitly not race-safe, so two rows sharing a (game_id, display_name) can exist.
            // Creating the index then fails with SQLSTATE 23505 naming the duplicate, and the
            // migration rolls back whole rather than corrupting anything. To check beforehand:
            //   SELECT game_id, display_name, count(*) FROM drivers
            //   GROUP BY 1, 2 HAVING count(*) > 1;
            // Duplicates must be merged by hand (repoint sessions.driver_id, delete the loser) —
            // deliberately not automated here, because two rows sharing a name may well be two
            // different people, and only a human knows which.
            migrationBuilder.AlterColumn<Guid>(
                name: "game_id",
                table: "drivers",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_drivers_game_id_display_name",
                table: "drivers",
                columns: new[] { "game_id", "display_name" },
                unique: true,
                filter: "sim_driver_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_drivers_game_id_sim_driver_id",
                table: "drivers",
                columns: new[] { "game_id", "sim_driver_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_drivers_games_game_id",
                table: "drivers",
                column: "game_id",
                principalTable: "games",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_drivers_games_game_id",
                table: "drivers");

            migrationBuilder.DropIndex(
                name: "ix_sessions_driver_wear_rates",
                table: "sessions");

            migrationBuilder.DropIndex(
                name: "IX_drivers_game_id_display_name",
                table: "drivers");

            migrationBuilder.DropIndex(
                name: "IX_drivers_game_id_sim_driver_id",
                table: "drivers");

            migrationBuilder.DropColumn(
                name: "fuel_usage_rate",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "player_name",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "tyre_wear_rate",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "game_id",
                table: "drivers");

            migrationBuilder.DropColumn(
                name: "sim_driver_id",
                table: "drivers");

            migrationBuilder.CreateIndex(
                name: "IX_sessions_driver_id",
                table: "sessions",
                column: "driver_id");
        }
    }
}
