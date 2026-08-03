using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace lpdeBack.Migrations
{
    /// <inheritdoc />
    public partial class OffresEcarteesEtMetiersExclus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MetiersExclus",
                table: "PreferencesEmploi",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OffresEcartees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    JobOfferId = table.Column<int>(type: "int", nullable: false),
                    CreeLe = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OffresEcartees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OffresEcartees_JobOffers_JobOfferId",
                        column: x => x.JobOfferId,
                        principalTable: "JobOffers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OffresEcartees_JobOfferId",
                table: "OffresEcartees",
                column: "JobOfferId");

            migrationBuilder.CreateIndex(
                name: "IX_OffresEcartees_UserId_JobOfferId",
                table: "OffresEcartees",
                columns: new[] { "UserId", "JobOfferId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OffresEcartees");

            migrationBuilder.DropColumn(
                name: "MetiersExclus",
                table: "PreferencesEmploi");
        }
    }
}
