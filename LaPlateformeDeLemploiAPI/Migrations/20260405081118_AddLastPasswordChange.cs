using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LaPlateformeDeLemploiAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddLastPasswordChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastPasswordChange",
                table: "Users",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastPasswordChange",
                table: "Users");
        }
    }
}
