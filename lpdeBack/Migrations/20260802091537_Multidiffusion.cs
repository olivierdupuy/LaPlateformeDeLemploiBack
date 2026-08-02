using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace lpdeBack.Migrations
{
    /// <inheritdoc />
    public partial class Multidiffusion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Diffusions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobOfferId = table.Column<int>(type: "int", nullable: false),
                    DemandeeParUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Destination = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Statut = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReferenceExterne = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UrlExterne = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Motif = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DemandeeLe = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DiffuseeLe = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetireeLe = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Tentatives = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Diffusions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Diffusions_JobOffers_JobOfferId",
                        column: x => x.JobOfferId,
                        principalTable: "JobOffers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Diffusions_JobOfferId",
                table: "Diffusions",
                column: "JobOfferId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Diffusions");
        }
    }
}
