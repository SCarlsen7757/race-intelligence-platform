using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceIntelligence.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropUnusedSimTrackIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Both columns have been NULL in every row ever written: TrackRepository accepted them
            // as optional parameters no caller passed, and no wire contract carried a value to pass.
            // Nothing is lost by dropping them, and re-adding them the day a connector actually
            // reports track/layout ids is a two-line migration -- whereas leaving them in place
            // costs every reader of this schema the question of what they are for.
            migrationBuilder.DropColumn(
                name: "sim_track_id",
                table: "tracks");

            migrationBuilder.DropColumn(
                name: "sim_layout_id",
                table: "track_layouts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sim_track_id",
                table: "tracks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sim_layout_id",
                table: "track_layouts",
                type: "text",
                nullable: true);
        }
    }
}
