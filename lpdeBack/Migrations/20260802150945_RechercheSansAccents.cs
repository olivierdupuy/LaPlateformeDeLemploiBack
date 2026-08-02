using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace lpdeBack.Migrations
{
    /// <summary>
    /// La recherche cesse d'exiger les accents.
    ///
    /// La base est collationnee en « SQL_Latin1_General_CP1_CI_AS » :
    /// insensible a la casse, sensible aux accents. Sur un site d'emploi
    /// francais, cela veut dire qu'un candidat qui tape « developpeur »
    /// au clavier ne trouve pas les offres intitulees « Developpeur »
    /// avec l'accent. Sur le catalogue de developpement : neuf offres
    /// contre soixante-deux, pour le meme mot. Quatre-vingt-sept pour
    /// cent du resultat disparaissaient sur une touche.
    ///
    /// On passe donc les trois colonnes cherchees en plein texte a la
    /// collation soeur, « CP1_CI_AI » : meme page de codes, meme ordre de
    /// tri, meme insensibilite a la casse — seuls les accents cessent de
    /// compter. Les deux orthographes rendent desormais les memes
    /// soixante et onze offres.
    ///
    /// « Company » n'y figure pas volontairement : contrairement aux
    /// trois autres, elle est comparee par egalite dans une vingtaine
    /// d'endroits — page entreprise, abonnements, avis — et changer sa
    /// collation changerait le regroupement des entreprises, ce qui n'est
    /// pas la question posee ici.
    ///
    /// Les deux index qui portent « Title » et « Tags » doivent tomber le
    /// temps de la conversion : on ne retype pas une colonne indexee. Ils
    /// sont reposes a l'identique juste apres.
    /// </summary>
    public partial class RechercheSansAccents : Migration
    {
        private const string Sans = "SQL_Latin1_General_CP1_CI_AI";
        private const string Avec = "SQL_Latin1_General_CP1_CI_AS";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => Basculer(migrationBuilder, Sans);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => Basculer(migrationBuilder, Avec);

        /// <summary>
        /// Le meme mouvement dans les deux sens : deposer les index,
        /// retyper les colonnes, reposer les index. Le « IF EXISTS » sur
        /// la depose evite qu'une base a moitie migree bloque la suite.
        /// </summary>
        private static void Basculer(MigrationBuilder migrationBuilder, string collation)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_JobOffers_Recherche ON JobOffers;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_JobOffers_Title ON JobOffers;");

            migrationBuilder.Sql($"ALTER TABLE JobOffers ALTER COLUMN Title nvarchar(200) COLLATE {collation} NOT NULL;");
            migrationBuilder.Sql($"ALTER TABLE JobOffers ALTER COLUMN Tags nvarchar(500) COLLATE {collation} NULL;");
            migrationBuilder.Sql($"ALTER TABLE JobOffers ALTER COLUMN Description nvarchar(max) COLLATE {collation} NOT NULL;");

            migrationBuilder.Sql("CREATE INDEX IX_JobOffers_Title ON JobOffers (Title);");
            migrationBuilder.Sql(
                "CREATE NONCLUSTERED INDEX IX_JobOffers_Recherche ON JobOffers " +
                "(IsActive, IsDraft, ModerationStatus, CreatedAt DESC) " +
                "INCLUDE (Title, Company, Tags);");
        }
    }
}
