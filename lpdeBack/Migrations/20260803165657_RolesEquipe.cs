using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace lpdeBack.Migrations
{
    /// <summary>
    /// Le role au sein d'une equipe de recrutement.
    ///
    /// Le partage etait binaire : declarer la meme entreprise suffisait a
    /// pouvoir modifier, suspendre et supprimer les offres de tout le
    /// monde. Cela convient a deux associes, pas a une equipe de dix — un
    /// nouvel arrivant avait le catalogue entier a sa main des sa premiere
    /// connexion.
    ///
    /// LA REPRISE DE L'EXISTANT, ET CE QU'ELLE RETIRE
    /// Ce lot restreint des droits que des comptes possedaient. Le faire
    /// sans reprise poserait tout le monde en « membre » : plus aucune
    /// equipe n'aurait de proprietaire, et ses offres deviendraient
    /// ingerables sauf par l'administration du site. La reprise designe
    /// donc, par entreprise, LE COMPTE LE PLUS ANCIEN — celui qui a
    /// vraisemblablement ouvert le compte de la maison.
    ///
    /// Les recruteurs sans entreprise declaree sont proprietaires d'eux
    /// memes : ils n'ont pas d'equipe, et le rester seul ne retire rien.
    ///
    /// Les candidats recoivent « membre » et ne s'en servent jamais : la
    /// colonne est sur « Users » parce que l'equipe l'est aussi, mais elle
    /// n'a de sens que pour un recruteur.
    /// </summary>
    public partial class RolesEquipe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RoleEquipe",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "membre");

            migrationBuilder.Sql("UPDATE Users SET RoleEquipe = 'membre';");

            // Un recruteur sans entreprise n'a pas d'equipe : il est seul,
            // et le laisser « membre » lui retirerait des droits sans que
            // personne ne puisse les lui rendre.
            migrationBuilder.Sql(@"
                UPDATE Users
                   SET RoleEquipe = 'proprietaire'
                 WHERE Role IN ('Recruiter', 'Admin')
                   AND (Company IS NULL OR LTRIM(RTRIM(Company)) = '');");

            // Par entreprise, le compte le plus ancien devient proprietaire.
            migrationBuilder.Sql(@"
                WITH Premiers AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (
                               PARTITION BY LOWER(LTRIM(RTRIM(Company)))
                               ORDER BY CreatedAt, Id) AS Rang
                      FROM Users
                     WHERE Role IN ('Recruiter', 'Admin')
                       AND Company IS NOT NULL
                       AND LTRIM(RTRIM(Company)) <> ''
                )
                UPDATE Users
                   SET RoleEquipe = 'proprietaire'
                  FROM Users u
                  JOIN Premiers p ON p.Id = u.Id
                 WHERE p.Rang = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Le retour en arriere rend a chacun les droits de tous : c'est
            // l'ancien comportement, et il n'y a rien a conserver de la
            // distinction.
            migrationBuilder.DropColumn(
                name: "RoleEquipe",
                table: "Users");
        }
    }
}
