using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace lpdeBack.Migrations
{
    /// <inheritdoc />
    public partial class PreferencesEmploi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PreferencesEmploi",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SalaireAnnuelMinimum = table.Column<int>(type: "int", nullable: true),
                    Contrat = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Distanciel = table.Column<bool>(type: "bit", nullable: true),
                    RayonKm = table.Column<int>(type: "int", nullable: true),
                    MisAJourLe = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreferencesEmploi", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PreferencesEmploi_UserId",
                table: "PreferencesEmploi",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PreferencesEmploi");
        }
    }
}
