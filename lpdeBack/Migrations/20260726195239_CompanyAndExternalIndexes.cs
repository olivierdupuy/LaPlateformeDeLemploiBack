using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace lpdeBack.Migrations
{
    /// <inheritdoc />
    public partial class CompanyAndExternalIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_JobOffers_Company",
                table: "JobOffers",
                column: "Company");

            migrationBuilder.CreateIndex(
                name: "IX_JobOffers_ExternalId",
                table: "JobOffers",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_JobOffers_IsActive_ModerationStatus_Company",
                table: "JobOffers",
                columns: new[] { "IsActive", "ModerationStatus", "Company" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobOffers_Company",
                table: "JobOffers");

            migrationBuilder.DropIndex(
                name: "IX_JobOffers_ExternalId",
                table: "JobOffers");

            migrationBuilder.DropIndex(
                name: "IX_JobOffers_IsActive_ModerationStatus_Company",
                table: "JobOffers");
        }
    }
}
