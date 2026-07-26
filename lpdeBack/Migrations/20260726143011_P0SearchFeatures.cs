using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace lpdeBack.Migrations
{
    /// <inheritdoc />
    public partial class P0SearchFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Languages",
                table: "JobOffers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkSchedule",
                table: "JobOffers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JobReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobOfferId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReporterEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ReporterUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobReports_JobOffers_JobOfferId",
                        column: x => x.JobOfferId,
                        principalTable: "JobOffers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobReports_JobOfferId",
                table: "JobReports",
                column: "JobOfferId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobReports");

            migrationBuilder.DropColumn(
                name: "Languages",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "WorkSchedule",
                table: "JobOffers");
        }
    }
}
