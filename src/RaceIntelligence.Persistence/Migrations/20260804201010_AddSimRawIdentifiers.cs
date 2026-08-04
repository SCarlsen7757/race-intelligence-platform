using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceIntelligence.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSimRawIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sim_car_class_id",
                table: "sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sim_car_id",
                table: "sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sim_manufacturer_id",
                table: "sessions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sim_car_class_id",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "sim_car_id",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "sim_manufacturer_id",
                table: "sessions");
        }
    }
}
