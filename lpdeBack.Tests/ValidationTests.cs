using System.ComponentModel.DataAnnotations;
using lpdeBack.Validation;

namespace lpdeBack.Tests;

/// <summary>
/// Les contraintes de saisie.
///
/// Elles gardent toutes les entrees publiques du site — inscription,
/// candidature, abonnement. Une regression ici ne casse rien de visible :
/// elle laisse simplement passer ce qui ne devrait pas.
/// </summary>
public class ValidationTests
{
    private static bool Accepte(ValidationAttribute regle, object? valeur)
        => regle.GetValidationResult(valeur, new ValidationContext(new object())) == ValidationResult.Success;

    // ── Adresses electroniques ──

    [Theory]
    [InlineData("camille@exemple.fr")]
    [InlineData("prenom.nom+etiquette@sous.domaine.example.com")]
    public void Une_adresse_plausible_passe(string adresse)
        => Assert.True(Accepte(new AdresseCourrielAttribute(), adresse));

    [Theory]
    [InlineData("pas-une-adresse")]
    [InlineData("deux@@arobases.fr")]
    [InlineData("point..double@exemple.fr")]
    [InlineData("@exemple.fr")]
    [InlineData("camille@")]
    [InlineData("camille@exemple")]
    public void Une_adresse_douteuse_est_refusee(string adresse)
        => Assert.False(Accepte(new AdresseCourrielAttribute(), adresse));

    [Fact]
    public void Une_adresse_trop_longue_est_refusee()
    {
        var trop = new string('a', 250) + "@exemple.fr";
        Assert.False(Accepte(new AdresseCourrielAttribute(), trop));
    }

    // ── Telephones ──

    [Theory]
    [InlineData("0612345678")]
    [InlineData("06 12 34 56 78")]
    public void Un_numero_francais_passe(string numero)
        => Assert.True(Accepte(new TelephoneFrAttribute(), numero));

    [Theory]
    [InlineData("0012345678")]   // deuxieme chiffre nul
    [InlineData("123456789")]    // ne commence pas par zero
    [InlineData("06123456789")]  // un chiffre de trop
    [InlineData("abcdefghij")]
    public void Un_numero_invraisemblable_est_refuse(string numero)
        => Assert.False(Accepte(new TelephoneFrAttribute(), numero));

    // ── Adresses web ──

    [Fact]
    public void Un_chemin_interne_reste_accepte()
    {
        // Les CV et les avatars sont enregistres en relatif. Refuser ce
        // format bloquerait toute candidature portant un fichier — ce
        // qui est deja arrive une fois.
        Assert.True(Accepte(new AdresseWebAttribute(), "/uploads/resumes/abc.pdf"));
    }

    [Fact]
    public void Un_chemin_interne_est_refuse_quand_on_exige_l_externe()
        => Assert.False(Accepte(new AdresseWebAttribute { ExterneSeulement = true }, "/uploads/abc.pdf"));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    public void Une_adresse_executable_est_refusee(string adresse)
        => Assert.False(Accepte(new AdresseWebAttribute(), adresse));

    // ── Balisage ──

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("Bonjour <b>vous</b>")]
    public void Le_balisage_est_refuse(string texte)
        => Assert.False(Accepte(new SansBalisageAttribute(), texte));

    [Fact]
    public void Un_texte_ordinaire_passe()
        => Assert.True(Accepte(new SansBalisageAttribute(), "Développeur .NET — 3 ans d'expérience"));

    // ── Listes fermees ──

    [Fact]
    public void Une_valeur_hors_liste_est_refusee()
        => Assert.False(Accepte(new ParmiAttribute("Candidate", "Recruiter"), "Admin"));

    [Fact]
    public void Une_valeur_de_la_liste_passe()
        => Assert.True(Accepte(new ParmiAttribute("Candidate", "Recruiter"), "Recruiter"));
}
