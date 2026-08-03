using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace lpdeBack.Migrations
{
    /// <inheritdoc />
    public partial class NotesEquipe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotesCandidature",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationId = table.Column<int>(type: "int", nullable: false),
                    AuteurId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    AuteurNom = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Contenu = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreeLe = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotesCandidature", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotesCandidature_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotesCandidature_ApplicationId",
                table: "NotesCandidature",
                column: "ApplicationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotesCandidature");
        }
    }
}
