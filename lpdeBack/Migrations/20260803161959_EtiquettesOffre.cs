using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace lpdeBack.Migrations
{
    /// <inheritdoc />
    public partial class EtiquettesOffre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EtiquettesOffre",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobOfferId = table.Column<int>(type: "int", nullable: false),
                    Nom = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Cle = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreeParUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreeLe = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EtiquettesOffre", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EtiquettesOffre_JobOffers_JobOfferId",
                        column: x => x.JobOfferId,
                        principalTable: "JobOffers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EtiquettesOffre_Cle",
                table: "EtiquettesOffre",
                column: "Cle");

            migrationBuilder.CreateIndex(
                name: "IX_EtiquettesOffre_JobOfferId_Cle",
                table: "EtiquettesOffre",
                columns: new[] { "JobOfferId", "Cle" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EtiquettesOffre");
        }
    }
}
