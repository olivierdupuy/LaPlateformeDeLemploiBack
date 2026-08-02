using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace lpdeBack.Migrations
{
    /// <summary>
    /// L'index de la recherche plein texte.
    ///
    /// Il ne sera jamais parcouru par recherche : « LIKE '%mot%' »
    /// commence par un joker, aucune structure ne peut situer la valeur.
    /// Ce qu'il fait, c'est reduire ce qu'il y a a lire — les trois
    /// colonnes cherchees au lieu de la table entiere, description en
    /// « nvarchar(max) » comprise.
    ///
    /// Sur les cent dix-neuf mille offres du catalogue de developpement :
    /// 165 ms au lieu de 478, pour dix-huit megaoctets.
    /// </summary>
    public partial class IndexRecherche : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_JobOffers_Recherche",
                table: "JobOffers",
                columns: new[] { "IsActive", "IsDraft", "ModerationStatus", "CreatedAt" },
                descending: new[] { false, false, false, true })
                .Annotation("SqlServer:Include", new[] { "Title", "Company", "Tags" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobOffers_Recherche",
                table: "JobOffers");
        }
    }
}
