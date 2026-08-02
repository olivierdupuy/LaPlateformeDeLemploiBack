using Microsoft.Extensions.Configuration;
using lpdeBack.Models;
using lpdeBack.Services;

namespace lpdeBack.Tests;

/// <summary>
/// La lettre faite de blocs.
///
/// Deux choses se cassent en silence ici. Le HTML de courriel d'abord :
/// une grille CSS ou un « div » a la place d'un tableau ne se voit pas
/// dans un navigateur et s'effondre dans Outlook — c'est-a-dire chez une
/// partie des destinataires, et jamais chez celui qui a redige.
///
/// Les replis ensuite. Un abonne dont le profil ne ramene aucune offre
/// recevrait une lettre trouee : un intertitre « Les offres pres de chez
/// vous » suivi de blanc. Personne ne s'en apercoit avant que trois mille
/// messages soient partis.
/// </summary>
public class LettreEnBlocsTests
{
    private const string Site = "https://exemple.fr";

    private static LettreEnBlocs Rendu()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:PublicUrl"] = Site })
            .Build();

        // « Rendre » ne touche jamais la base : les offres lui arrivent
        // deja resolues dans le contexte. C'est ce qui permet de tester le
        // rendu sans monter quoi que ce soit.
        return new LettreEnBlocs(null!, config);
    }

    private static NewsletterSubscriber Abonne(
        string? ville = "66 - Perpignan", string? categories = "Santé", string? departement = "66") =>
        new()
        {
            Email = "marie@exemple.fr",
            FirstName = "Marie",
            City = ville,
            Categories = categories,
            Department = departement,
            UnsubscribeToken = "jeton",
        };

    private static JobOffer Offre(
        int id = 1, string titre = "Infirmier en EHPAD", string lieu = "66 - Perpignan",
        string categorie = "Santé", double? lat = 42.6887, double? lng = 2.8948) =>
        new()
        {
            Id = id,
            Title = titre,
            Company = "Résidence Les Jardins",
            Location = lieu,
            Category = categorie,
            ContractType = "CDI",
            Latitude = lat,
            Longitude = lng,
            Description = "Description assez longue pour ne rien declencher d'autre.",
            CreatedAt = DateTime.UtcNow.AddDays(-2),
        };

    // ══════════════════════════════════════
    //  Le JSON
    // ══════════════════════════════════════

    [Fact]
    public void Les_blocs_font_l_aller_retour_en_json()
    {
        var avant = new List<BlocLettre>
        {
            new() { Type = "titre", Texte = "Bonjour" },
            new() { Type = "offres", Offres = new BlocOffres { Source = "abonne", Nombre = 3 } },
        };

        var apres = LettreEnBlocs.Lire(LettreEnBlocs.Ecrire(avant));

        Assert.Equal(2, apres.Count);
        Assert.Equal("titre", apres[0].Type);
        Assert.Equal("abonne", apres[1].Offres!.Source);
        Assert.Equal(3, apres[1].Offres!.Nombre);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pas du json")]
    [InlineData("{\"ceci\":\"n'est pas un tableau\"}")]
    public void Un_json_illisible_rend_une_lettre_vide_et_non_une_exception(string? json)
    {
        // La colonne peut avoir ete abimee. Ouvrir la campagne pour la
        // reparer doit rester possible ; lever ici l'en empecherait.
        Assert.Empty(LettreEnBlocs.Lire(json));
    }

    // ══════════════════════════════════════
    //  Le HTML de courriel
    // ══════════════════════════════════════

    [Fact]
    public void Le_texte_est_echappe_et_decoupe_en_paragraphes()
    {
        var html = Rendu().Rendre(
            new[] { new BlocLettre { Type = "texte", Texte = "Premier.\n\nSecond & <b>gras</b>." } },
            ContexteOffres.Vide, Abonne());

        Assert.Contains("<p style=", html);
        // Ce qu'on colle depuis un traitement de texte ne doit pas devenir
        // du balisage actif dans la boite de trois mille personnes.
        Assert.Contains("&lt;b&gt;gras&lt;/b&gt;", html);
        Assert.DoesNotContain("<b>gras</b>", html);
        Assert.Equal(2, html.Split("<p style=").Length - 1);
    }

    [Fact]
    public void Le_bouton_est_un_tableau_et_non_un_div()
    {
        // Outlook ignore les bordures arrondies et le remplissage sur un
        // « div » : le bouton s'y afficherait comme un lien nu.
        var html = Rendu().Rendre(
            new[] { new BlocLettre { Type = "bouton", Texte = "Voir les offres", Url = "https://exemple.fr/offres" } },
            ContexteOffres.Vide, Abonne());

        Assert.Contains("<table role=\"presentation\"", html);
        Assert.Contains("https://exemple.fr/offres", html);
        Assert.Contains("Voir les offres", html);
    }

    [Fact]
    public void Une_image_sort_toujours_avec_son_texte_de_remplacement()
    {
        // Une messagerie sur deux bloque les images : le « alt » est alors
        // tout ce qui reste au destinataire.
        var html = Rendu().Rendre(
            new[] { new BlocLettre { Type = "image", Url = "https://exemple.fr/i.png", Alt = "Un chantier" } },
            ContexteOffres.Vide, Abonne());

        Assert.Contains("alt=\"Un chantier\"", html);
    }

    [Fact]
    public void Un_bloc_vide_ne_produit_rien()
    {
        // Un bloc ajoute puis laisse en blanc ne doit pas creuser un trou
        // dans la lettre.
        var html = Rendu().Rendre(
            new[]
            {
                new BlocLettre { Type = "titre", Texte = "  " },
                new BlocLettre { Type = "texte", Texte = null },
                new BlocLettre { Type = "bouton", Texte = "Sans adresse" },
            },
            ContexteOffres.Vide, Abonne());

        Assert.Equal("", html);
    }

    [Fact]
    public void Un_type_de_bloc_inconnu_est_ignore_sans_casser_la_lettre()
    {
        // Un JSON venu d'une version plus recente de la console ne doit
        // pas empecher la campagne de partir.
        var html = Rendu().Rendre(
            new[]
            {
                new BlocLettre { Type = "sondage", Texte = "Un jour peut-être" },
                new BlocLettre { Type = "titre", Texte = "Le reste passe" },
            },
            ContexteOffres.Vide, Abonne());

        Assert.Contains("Le reste passe", html);
        Assert.DoesNotContain("Un jour peut-être", html);
    }

    // ══════════════════════════════════════
    //  Le bloc d'offres
    // ══════════════════════════════════════

    [Fact]
    public void Une_offre_sort_avec_son_lien_son_lieu_et_son_contrat()
    {
        var ctx = new ContexteOffres { ParBloc = { [0] = new List<JobOffer> { Offre() } } };

        var html = Rendu().Rendre(
            new[] { new BlocLettre { Type = "offres", Offres = new BlocOffres { Source = "choisies" } } },
            ctx, Abonne());

        Assert.Contains($"{Site}/offres/1", html);
        Assert.Contains("Infirmier en EHPAD", html);
        Assert.Contains("66 - Perpignan", html);
        Assert.Contains("CDI", html);
    }

    [Fact]
    public void Le_mode_abonne_choisit_selon_la_ville_et_les_centres_d_interet()
    {
        // C'est ce qui separe une lettre d'un site d'emploi d'un
        // publipostage : la personne de Perpignan qui suit « Santé »
        // recoit du soin pres de chez elle, pas les memes six annonces que
        // tout le monde.
        var ctx = new ContexteOffres
        {
            Vivier =
            {
                Offre(1, "Infirmier en EHPAD", "66 - Perpignan", "Santé", 42.6887, 2.8948),
                Offre(2, "Développeur web", "59 - Lille", "Informatique", 50.6292, 3.0573),
            },
        };

        var html = Rendu().Rendre(
            new[] { new BlocLettre { Type = "offres", Offres = new BlocOffres { Source = "abonne", Nombre = 5 } } },
            ctx, Abonne(ville: "66 - Perpignan", categories: "Santé"));

        Assert.Contains("Infirmier en EHPAD", html);
        Assert.DoesNotContain("Développeur web", html);
    }

    [Fact]
    public void Deux_abonnes_differents_recoivent_deux_lettres_differentes()
    {
        var ctx = new ContexteOffres
        {
            Vivier =
            {
                Offre(1, "Infirmier en EHPAD", "66 - Perpignan", "Santé", 42.6887, 2.8948),
                Offre(2, "Développeur web", "59 - Lille", "Informatique", 50.6292, 3.0573),
            },
        };

        var blocs = new[]
        {
            new BlocLettre { Type = "offres", Offres = new BlocOffres { Source = "abonne", Nombre = 5 } },
        };

        var pourMarie = Rendu().Rendre(blocs, ctx, Abonne("66 - Perpignan", "Santé", "66"));
        var pourPaul = Rendu().Rendre(blocs, ctx, Abonne("59 - Lille", "Informatique", "59"));

        Assert.Contains("Infirmier", pourMarie);
        Assert.Contains("Développeur", pourPaul);
        Assert.NotEqual(pourMarie, pourPaul);
    }

    // ══════════════════════════════════════
    //  Les replis
    // ══════════════════════════════════════

    [Fact]
    public void Un_abonne_sans_correspondance_recoit_les_offres_de_son_departement()
    {
        // Sans repli, cet abonne recevrait un intertitre suivi de blanc.
        var ctx = new ContexteOffres
        {
            Vivier =
            {
                Offre(1, "Couvreur", "66 - Perpignan", "Bâtiment", 42.6887, 2.8948),
                Offre(2, "Développeur web", "59 - Lille", "Informatique", 50.6292, 3.0573),
            },
        };

        var html = Rendu().Rendre(
            new[]
            {
                new BlocLettre
                {
                    Type = "offres",
                    Offres = new BlocOffres { Source = "abonne", Repli = "region", Nombre = 3 },
                },
            },
            ctx, Abonne(ville: null, categories: null, departement: "66"));

        Assert.Contains("Couvreur", html);
        Assert.DoesNotContain("Développeur web", html);
    }

    [Fact]
    public void Un_departement_sans_offre_elargit_plutot_que_de_renoncer()
    {
        var ctx = new ContexteOffres { Vivier = { Offre(2, "Développeur web", "59 - Lille", "Informatique") } };

        var html = Rendu().Rendre(
            new[]
            {
                new BlocLettre
                {
                    Type = "offres",
                    Offres = new BlocOffres { Source = "abonne", Repli = "region", Nombre = 3 },
                },
            },
            ctx, Abonne(ville: null, categories: null, departement: "12"));

        Assert.Contains("Développeur web", html);
    }

    [Fact]
    public void Le_repli_masquer_fait_disparaitre_le_bloc_entier()
    {
        // Y compris son intertitre : « Les offres près de chez vous »
        // suivi de rien est pire qu'aucun bloc.
        var ctx = new ContexteOffres { Vivier = { Offre(2, "Développeur web", "59 - Lille", "Informatique") } };

        var html = Rendu().Rendre(
            new[]
            {
                new BlocLettre
                {
                    Type = "offres",
                    Offres = new BlocOffres
                    {
                        Source = "abonne", Repli = "masquer",
                        Titre = "Les offres près de chez vous",
                    },
                },
            },
            ctx, Abonne(ville: null, categories: null, departement: null));

        Assert.Equal("", html);
        Assert.DoesNotContain("près de chez vous", html);
    }

    [Fact]
    public void Le_nombre_d_offres_est_borne()
    {
        // Au-dela, la lettre devient un catalogue et personne ne la lit.
        var ctx = new ContexteOffres
        {
            ParBloc =
            {
                [0] = Enumerable.Range(1, 30).Select(i => Offre(i, $"Poste {i}")).ToList(),
            },
        };

        var html = Rendu().Rendre(
            new[]
            {
                new BlocLettre
                {
                    Type = "offres",
                    Offres = new BlocOffres { Source = "choisies", Nombre = 99 },
                },
            },
            ctx, Abonne());

        var cartes = html.Split($"{Site}/offres/").Length - 1;
        Assert.Equal(LettreEnBlocs.MaxOffresParBloc, cartes);
    }
}
