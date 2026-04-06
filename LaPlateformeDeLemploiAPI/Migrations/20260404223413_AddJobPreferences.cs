using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LaPlateformeDeLemploiAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddJobPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    DesiredContractTypes = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DesiredLocations = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DesiredCategoryIds = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MinSalary = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    PreferRemote = table.Column<bool>(type: "bit", nullable: true),
                    Keywords = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobPreferences_UserId",
                table: "JobPreferences",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobPreferences");
        }
    }
}
