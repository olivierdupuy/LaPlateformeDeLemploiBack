using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LaPlateformeDeLemploiAPI.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LogoUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Website = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobOffers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContractType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SalaryMin = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    SalaryMax = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    IsRemote = table.Column<bool>(type: "bit", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobOffers_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JobOffers_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "Icon", "Name" },
                values: new object[,]
                {
                    { 1, "bi-code-slash", "Développement Web" },
                    { 2, "bi-palette", "Design & UX" },
                    { 3, "bi-graph-up", "Data & IA" },
                    { 4, "bi-cloud", "DevOps & Cloud" },
                    { 5, "bi-shield-lock", "Cybersécurité" },
                    { 6, "bi-megaphone", "Marketing Digital" },
                    { 7, "bi-kanban", "Gestion de Projet" },
                    { 8, "bi-headset", "Support & IT" }
                });

            migrationBuilder.InsertData(
                table: "Companies",
                columns: new[] { "Id", "CreatedAt", "Description", "Location", "LogoUrl", "Name", "Website" },
                values: new object[,]
                {
                    { 1, new DateTime(2024, 1, 15, 0, 0, 0, 0, DateTimeKind.Utc), "Leader français du développement logiciel, spécialisé dans les solutions cloud innovantes.", "Paris", "https://ui-avatars.com/api/?name=TC&background=4f46e5&color=fff&size=128", "TechCorp France", "https://techcorp.fr" },
                    { 2, new DateTime(2024, 2, 20, 0, 0, 0, 0, DateTimeKind.Utc), "Startup spécialisée en intelligence artificielle et analyse de données massives.", "Lyon", "https://ui-avatars.com/api/?name=DV&background=059669&color=fff&size=128", "DataVision", "https://datavision.io" },
                    { 3, new DateTime(2024, 3, 10, 0, 0, 0, 0, DateTimeKind.Utc), "Expert en solutions d'infrastructure cloud et DevOps pour entreprises.", "Bordeaux", "https://ui-avatars.com/api/?name=C9&background=0891b2&color=fff&size=128", "CloudNine", "https://cloudnine.eu" },
                    { 4, new DateTime(2024, 4, 5, 0, 0, 0, 0, DateTimeKind.Utc), "Agence de design UX/UI primée, créant des expériences numériques mémorables.", "Nantes", "https://ui-avatars.com/api/?name=DH&background=e11d48&color=fff&size=128", "DesignHub", "https://designhub.fr" },
                    { 5, new DateTime(2024, 5, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Cabinet de conseil en cybersécurité protégeant les entreprises du CAC 40.", "Toulouse", "https://ui-avatars.com/api/?name=CS&background=7c3aed&color=fff&size=128", "CyberShield", "https://cybershield.fr" }
                });

            migrationBuilder.InsertData(
                table: "JobOffers",
                columns: new[] { "Id", "CategoryId", "CompanyId", "ContractType", "Description", "ExpiresAt", "IsActive", "IsRemote", "Location", "PublishedAt", "SalaryMax", "SalaryMin", "Title" },
                values: new object[,]
                {
                    { 1, 1, 1, "CDI", "Nous recherchons un développeur Full Stack passionné pour rejoindre notre équipe produit. Vous travaillerez sur notre plateforme SaaS utilisée par plus de 10 000 entreprises. Stack technique : Angular 19, .NET 8, SQL Server, Azure.", null, true, true, "Paris", new DateTime(2026, 3, 15, 0, 0, 0, 0, DateTimeKind.Utc), 60000m, 45000m, "Développeur Full Stack Angular / .NET" },
                    { 2, 3, 2, "CDI", "Rejoignez notre équipe Data pour concevoir et déployer des modèles de machine learning en production. Vous travaillerez sur des problématiques de NLP et de computer vision avec des datasets à grande échelle.", null, true, true, "Lyon", new DateTime(2026, 3, 20, 0, 0, 0, 0, DateTimeKind.Utc), 75000m, 55000m, "Data Scientist Senior" },
                    { 3, 4, 3, "CDI", "Vous serez responsable de la fiabilité et de la scalabilité de notre infrastructure cloud. Kubernetes, Terraform, CI/CD, monitoring et automatisation seront votre quotidien.", null, true, false, "Bordeaux", new DateTime(2026, 3, 25, 0, 0, 0, 0, DateTimeKind.Utc), 65000m, 50000m, "Ingénieur DevOps / SRE" },
                    { 4, 2, 4, "CDD", "Nous cherchons un designer créatif pour concevoir des interfaces utilisateur exceptionnelles. Vous mènerez des recherches utilisateurs, créerez des wireframes et prototypes, et collaborerez étroitement avec les développeurs.", null, true, true, "Nantes", new DateTime(2026, 3, 28, 0, 0, 0, 0, DateTimeKind.Utc), 48000m, 38000m, "UX/UI Designer" },
                    { 5, 5, 5, "CDI", "Intégrez notre équipe de pentesters et auditeurs sécurité. Vous réaliserez des tests d'intrusion, des audits de code et accompagnerez nos clients dans leur stratégie de sécurité.", null, true, false, "Toulouse", new DateTime(2026, 4, 1, 0, 0, 0, 0, DateTimeKind.Utc), 68000m, 48000m, "Consultant Cybersécurité" },
                    { 6, 1, 1, "CDI", "Participez au développement de nos applications web modernes. Vous serez en charge de créer des composants réutilisables, d'optimiser les performances et d'assurer l'accessibilité.", null, true, true, "Paris", new DateTime(2026, 4, 2, 0, 0, 0, 0, DateTimeKind.Utc), 55000m, 42000m, "Développeur Frontend React / Vue.js" },
                    { 7, 3, 2, "Stage", "Stage de 6 mois au sein de l'équipe Data. Vous participerez à l'analyse de données, la création de dashboards et l'automatisation de rapports avec Python et Power BI.", null, true, false, "Lyon", new DateTime(2026, 4, 3, 0, 0, 0, 0, DateTimeKind.Utc), 1200m, 800m, "Stage Data Analyst" },
                    { 8, 7, 1, "CDI", "Pilotez des projets de transformation digitale pour nos clients grands comptes. Méthodologie Agile, gestion budgétaire et coordination d'équipes pluridisciplinaires.", null, true, true, "Paris", new DateTime(2026, 4, 4, 0, 0, 0, 0, DateTimeKind.Utc), 65000m, 50000m, "Chef de Projet Digital" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobOffers_CategoryId",
                table: "JobOffers",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_JobOffers_CompanyId",
                table: "JobOffers",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobOffers");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Companies");
        }
    }
}
