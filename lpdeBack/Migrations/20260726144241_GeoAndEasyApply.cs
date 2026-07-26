using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace lpdeBack.Migrations
{
    /// <inheritdoc />
    public partial class GeoAndEasyApply : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EasyApply",
                table: "JobOffers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "JobOffers",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "JobOffers",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EasyApply",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "JobOffers");
        }
    }
}
