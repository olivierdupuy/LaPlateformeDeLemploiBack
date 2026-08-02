using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace lpdeBack.Migrations
{
    /// <inheritdoc />
    public partial class Professionnalisation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Empreinte",
                table: "JobOffers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotifFraude",
                table: "JobOffers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScoreFraude",
                table: "JobOffers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VueChezLaSourceLe",
                table: "JobOffers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Abonnements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Entreprise = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Formule = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DebutLe = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinLe = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Statut = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ReferenceExterne = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Abonnements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ErreursNavigateur",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Pile = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Chemin = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Navigateur = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Empreinte = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Occurrences = table.Column<int>(type: "int", nullable: false),
                    PremiereVue = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DerniereVue = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Traitee = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErreursNavigateur", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Factures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Numero = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RaisonSociale = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdresseFacturation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NumeroTva = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Libelle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MontantHtCentimes = table.Column<int>(type: "int", nullable: false),
                    TvaCentimes = table.Column<int>(type: "int", nullable: false),
                    MontantTtcCentimes = table.Column<int>(type: "int", nullable: false),
                    TauxTvaMillimes = table.Column<int>(type: "int", nullable: false),
                    Statut = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmiseLe = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PayeeLe = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReferenceExterne = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Factures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JetonsApi",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Nom = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Prefixe = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Empreinte = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Portees = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreeLe = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DerniereUtilisation = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevoqueLe = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JetonsApi", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LivraisonsWebhook",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WebhookId = table.Column<int>(type: "int", nullable: false),
                    Evenement = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Charge = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CodeReponse = table.Column<int>(type: "int", nullable: true),
                    Erreur = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Tentatives = table.Column<int>(type: "int", nullable: false),
                    CreeLe = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LivreLe = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LivraisonsWebhook", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MisesEnAvant",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobOfferId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DebutLe = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinLe = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MontantCentimes = table.Column<int>(type: "int", nullable: false),
                    Origine = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReferenceExterne = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MisesEnAvant", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PreferencesCourriel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Jeton = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AlertesOffres = table.Column<bool>(type: "bit", nullable: false),
                    SuiviCandidatures = table.Column<bool>(type: "bit", nullable: false),
                    Messages = table.Column<bool>(type: "bit", nullable: false),
                    Entretiens = table.Column<bool>(type: "bit", nullable: false),
                    LettreInformation = table.Column<bool>(type: "bit", nullable: false),
                    Actualites = table.Column<bool>(type: "bit", nullable: false),
                    ToutRefuse = table.Column<bool>(type: "bit", nullable: false),
                    MisAJourLe = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreferencesCourriel", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RetoursCourriel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Motif = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Occurrences = table.Column<int>(type: "int", nullable: false),
                    Bloque = table.Column<bool>(type: "bit", nullable: false),
                    PremierLe = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DernierLe = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetoursCourriel", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SignalementsDsa",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Reference = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TypeContenu = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContenuId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Motif = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Explication = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmailDeclarant = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeclareBonneFoi = table.Column<bool>(type: "bit", nullable: false),
                    Statut = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MesurePrise = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreeLe = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TraiteLe = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TraitePar = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignalementsDsa", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Webhooks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Evenements = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Secret = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Actif = table.Column<bool>(type: "bit", nullable: false),
                    EchecsConsecutifs = table.Column<int>(type: "int", nullable: false),
                    CreeLe = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DerniereLivraison = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DerniereErreur = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Webhooks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobOffers_Empreinte",
                table: "JobOffers",
                column: "Empreinte");

            migrationBuilder.CreateIndex(
                name: "IX_JobOffers_ExternalSource_VueChezLaSourceLe",
                table: "JobOffers",
                columns: new[] { "ExternalSource", "VueChezLaSourceLe" });

            migrationBuilder.CreateIndex(
                name: "IX_Abonnements_Statut",
                table: "Abonnements",
                column: "Statut");

            migrationBuilder.CreateIndex(
                name: "IX_Abonnements_UserId",
                table: "Abonnements",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ErreursNavigateur_DerniereVue",
                table: "ErreursNavigateur",
                column: "DerniereVue");

            migrationBuilder.CreateIndex(
                name: "IX_ErreursNavigateur_Empreinte",
                table: "ErreursNavigateur",
                column: "Empreinte",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Factures_Numero",
                table: "Factures",
                column: "Numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Factures_UserId",
                table: "Factures",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_JetonsApi_Empreinte",
                table: "JetonsApi",
                column: "Empreinte",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JetonsApi_UserId",
                table: "JetonsApi",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LivraisonsWebhook_WebhookId_CreeLe",
                table: "LivraisonsWebhook",
                columns: new[] { "WebhookId", "CreeLe" });

            migrationBuilder.CreateIndex(
                name: "IX_MisesEnAvant_FinLe",
                table: "MisesEnAvant",
                column: "FinLe");

            migrationBuilder.CreateIndex(
                name: "IX_MisesEnAvant_JobOfferId",
                table: "MisesEnAvant",
                column: "JobOfferId");

            migrationBuilder.CreateIndex(
                name: "IX_PreferencesCourriel_Email",
                table: "PreferencesCourriel",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PreferencesCourriel_Jeton",
                table: "PreferencesCourriel",
                column: "Jeton",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RetoursCourriel_Email",
                table: "RetoursCourriel",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignalementsDsa_Reference",
                table: "SignalementsDsa",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignalementsDsa_Statut",
                table: "SignalementsDsa",
                column: "Statut");

            migrationBuilder.CreateIndex(
                name: "IX_Webhooks_UserId",
                table: "Webhooks",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Abonnements");

            migrationBuilder.DropTable(
                name: "ErreursNavigateur");

            migrationBuilder.DropTable(
                name: "Factures");

            migrationBuilder.DropTable(
                name: "JetonsApi");

            migrationBuilder.DropTable(
                name: "LivraisonsWebhook");

            migrationBuilder.DropTable(
                name: "MisesEnAvant");

            migrationBuilder.DropTable(
                name: "PreferencesCourriel");

            migrationBuilder.DropTable(
                name: "RetoursCourriel");

            migrationBuilder.DropTable(
                name: "SignalementsDsa");

            migrationBuilder.DropTable(
                name: "Webhooks");

            migrationBuilder.DropIndex(
                name: "IX_JobOffers_Empreinte",
                table: "JobOffers");

            migrationBuilder.DropIndex(
                name: "IX_JobOffers_ExternalSource_VueChezLaSourceLe",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "Empreinte",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "MotifFraude",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "ScoreFraude",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "VueChezLaSourceLe",
                table: "JobOffers");
        }
    }
}
