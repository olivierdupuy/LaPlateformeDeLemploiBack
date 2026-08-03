using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace lpdeBack.Migrations
{
    /// <summary>
    /// L'etat de publication d'une offre : ouverte, suspendue ou fermee.
    ///
    /// « IsActive » disait si le public voit l'annonce, jamais pourquoi il
    /// ne la voit plus. Le recruteur n'avait donc que deux gestes :
    /// supprimer l'offre — en emportant ses candidatures — ou la laisser
    /// tourner et recevoir des dossiers qu'il ne traiterait pas.
    ///
    /// LA REPRISE DE L'EXISTANT
    /// EF generait « defaultValue: "" », ce qui aurait pose une chaine
    /// vide sur les cent vingt mille offres du catalogue : l'invariant
    /// « IsActive vaut vrai si et seulement si l'etat est ouverte » aurait
    /// ete faux des le premier jour, sans que rien ne le signale — aucune
    /// requete publique ne lit ce champ. La valeur par defaut devient
    /// « ouverte », et une reprise deduit l'etat de la visibilite reelle.
    ///
    /// Les offres deja invisibles deviennent « fermees » et non
    /// « suspendues » : personne ne les a mises en pause, on ne peut donc
    /// pas le leur preter apres coup.
    /// </summary>
    public partial class EtatPublicationOffre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EtatPublication",
                table: "JobOffers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "ouverte");

            migrationBuilder.Sql(
                "UPDATE JobOffers SET EtatPublication = " +
                "CASE WHEN IsActive = 1 THEN 'ouverte' ELSE 'fermee' END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // « IsActive » a suivi l'etat a chaque ecriture : le retour en
            // arriere ne perd que la distinction entre suspendue et fermee,
            // et cette distinction n'existait pas avant.
            migrationBuilder.DropColumn(
                name: "EtatPublication",
                table: "JobOffers");
        }
    }
}
