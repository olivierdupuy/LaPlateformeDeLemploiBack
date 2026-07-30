using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace lpdeBack.Migrations
{
    /// <inheritdoc />
    public partial class ApplyAndPostFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ScreeningQuestions",
                table: "JobOffers",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "JobOffers",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicationEmail",
                table: "JobOffers",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContractDuration",
                table: "JobOffers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HoursPerWeek",
                table: "JobOffers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDraft",
                table: "JobOffers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Les offres deja publiees valent un poste a pourvoir, pas zero.
            migrationBuilder.AddColumn<int>(
                name: "Openings",
                table: "JobOffers",
                type: "int",
                nullable: false,
                defaultValue: 1);

            // Faux pour les offres deja en ligne : jusqu'ici on postulait sans CV,
            // exiger le CV retroactivement bloquerait des candidatures en cours.
            // Les nouvelles offres, elles, arrivent avec l'option a vrai.
            migrationBuilder.AddColumn<bool>(
                name: "RequireResume",
                table: "JobOffers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SalaryPeriod",
                table: "JobOffers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartDate",
                table: "JobOffers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplementalPay",
                table: "JobOffers",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkplaceType",
                table: "JobOffers",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Applications",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QualificationScore",
                table: "Applications",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "ApplicationEmail",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "ContractDuration",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "HoursPerWeek",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "IsDraft",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "Openings",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "RequireResume",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "SalaryPeriod",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "SupplementalPay",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "WorkplaceType",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "QualificationScore",
                table: "Applications");

            migrationBuilder.AlterColumn<string>(
                name: "ScreeningQuestions",
                table: "JobOffers",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(4000)",
                oldMaxLength: 4000,
                oldNullable: true);
        }
    }
}
