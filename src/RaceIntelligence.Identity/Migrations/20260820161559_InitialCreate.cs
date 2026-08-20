using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RaceIntelligence.Identity.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "person",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_person", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "person_sim_alias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sim_key = table.Column<string>(type: "text", nullable: false),
                    sim_driver_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_person_sim_alias", x => x.id);
                    table.ForeignKey(
                        name: "FK_person_sim_alias_person_person_id",
                        column: x => x.person_id,
                        principalTable: "person",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_person_display_name",
                table: "person",
                column: "display_name");

            migrationBuilder.CreateIndex(
                name: "IX_person_sim_alias_person_id",
                table: "person_sim_alias",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "IX_person_sim_alias_sim_key_sim_driver_id",
                table: "person_sim_alias",
                columns: new[] { "sim_key", "sim_driver_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "person_sim_alias");

            migrationBuilder.DropTable(
                name: "person");
        }
    }
}
