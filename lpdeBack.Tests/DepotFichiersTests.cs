using lpdeBack.Services;

namespace lpdeBack.Tests;

/// <summary>
/// Le filtrage des noms de fichiers.
///
/// C'est la seule chose qui separe « donnez-moi ce CV » de « donnez-moi
/// ce fichier du serveur ». Elle merite d'etre eprouvee a chaque
/// livraison, et non relue de temps en temps.
/// </summary>
public class DepotFichiersTests
{
    [Theory]
    [InlineData("abc_20260412161541.pdf")]
    [InlineData("/uploads/resumes/abc_20260412161541.pdf")]
    [InlineData("uploads/resumes/abc.PDF")]
    public void Un_nom_de_CV_valide_est_accepte(string chemin)
    {
        Assert.NotNull(DepotFichiers.Nom(chemin));
        Assert.EndsWith(".pdf", DepotFichiers.Nom(chemin)!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../../appsettings.json")]
    [InlineData("..\\..\\appsettings.json")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("cv.pdf.exe")]
    [InlineData("cv.exe")]
    [InlineData("cv")]
    [InlineData("cv.pdf\0.exe")]               // octet nul : vieille ruse
    public void Tout_ce_qui_sort_du_dossier_est_refuse(string? chemin)
    {
        Assert.Null(DepotFichiers.Nom(chemin));
    }

    [Fact]
    public void Le_nom_retenu_ne_garde_aucun_chemin()
    {
        var nom = DepotFichiers.Nom("/uploads/resumes/sous/dossier/abc.pdf");
        Assert.Equal("abc.pdf", nom);
    }

    [Theory]
    [InlineData("../../appsettings.pdf", "appsettings.pdf")]
    [InlineData("..\\..\\secret.pdf", "secret.pdf")]
    [InlineData("/etc/passwd.pdf", "passwd.pdf")]
    public void Les_segments_de_remontee_sont_jetes_et_non_suivis(string entree, string attendu)
    {
        // Ce qui protege n'est pas de refuser « .. », c'est de n'en rien
        // garder : le nom retenu est cherche dans le seul dossier des
        // depots, ou « appsettings.pdf » n'existe pas. Un refus pur et
        // simple donnerait la meme securite et un moins bon message.
        var nom = DepotFichiers.Nom(entree);
        Assert.Equal(attendu, nom);
        Assert.DoesNotContain("..", nom);
        Assert.DoesNotContain("/", nom);
        Assert.DoesNotContain("\\", nom);
    }

    [Fact]
    public void Le_prefixe_enregistre_ne_change_pas()
    {
        // La base contient des milliers de chemins batis sur ce prefixe.
        // Le modifier sans migration rendrait tous les CV introuvables.
        Assert.Equal("/uploads/resumes/", DepotFichiers.Prefixe);
    }
}
