using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace lpdeBack.Migrations
{
    /// <inheritdoc />
    public partial class Invitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Invitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobOfferId = table.Column<int>(type: "int", nullable: false),
                    CandidatId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RecruteurId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EnvoyeeLe = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VueLe = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Reponse = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ReponduLe = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invitations_JobOffers_JobOfferId",
                        column: x => x.JobOfferId,
                        principalTable: "JobOffers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_CandidatId",
                table: "Invitations",
                column: "CandidatId");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_JobOfferId_CandidatId",
                table: "Invitations",
                columns: new[] { "JobOfferId", "CandidatId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Invitations");
        }
    }
}
