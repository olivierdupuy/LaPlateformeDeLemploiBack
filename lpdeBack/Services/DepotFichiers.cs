using System.Globalization;

namespace lpdeBack.Services;

/// <summary>
/// Le rangement des documents deposes par les membres.
///
/// ── Ou, et pourquoi la ──
///
/// Ils vivaient sous « wwwroot/uploads/resumes », c'est-a-dire dans le
/// dossier que « UseStaticFiles » sert au monde entier : n'importe qui
/// connaissant l'adresse telechargeait un CV, sans jeton ni session. Et
/// l'adresse n'etait pas un secret — le nom du fichier est bati sur
/// l'identifiant du membre, lequel circule dans les reponses de l'API.
///
/// Ils vivent desormais hors du dossier de l'application, dans
/// « C:\Datas\Laplateformedelemploi\Documents ». Deux raisons, et la
/// seconde est imperative : rien ne les sert plus tout seul, et le
/// deploiement se fait par « msdeploy -verb:sync », qui rend la
/// destination identique a la source — tout document ecrit chez
/// l'application disparaitrait a la mise en ligne suivante.
///
/// ── Comment ils sont classes ──
///
///     Documents\CV\2026\08\{identifiant}_{horodatage}.pdf
///
/// Par nature d'abord, par annee et par mois ensuite. L'annee et le
/// mois ne sont pas inventes : ils se lisent dans l'horodatage que porte
/// deja le nom du fichier. Le classement se retrouve donc par le calcul,
/// sans rien stocker de plus, et un dossier ne finit jamais avec cent
/// mille entrees dedans.
///
/// La base continue de stocker « /uploads/resumes/xxx.pdf ». Ce chemin
/// n'est plus une adresse, ce n'est plus un emplacement : c'est un
/// identifiant. Le changer imposerait de reecrire chaque ligne
/// existante pour un gain nul.
/// </summary>
public sealed class DepotFichiers
{
    private readonly ILogger<DepotFichiers> _journal;

    /// <summary>Le prefixe historique, conserve dans les enregistrements.</summary>
    public const string Prefixe = "/uploads/resumes/";

    /// <summary>Ou tout est range, sauf indication contraire.</summary>
    public const string RacineParDefaut = @"C:\Datas\Laplateformedelemploi\Documents";

    /// <summary>Les CV des candidats. La seule nature deposee a ce jour.</summary>
    public const string Cv = "CV";

    /// <summary>La racine des documents.</summary>
    public string Racine { get; }

    /// <summary>Pourquoi le rangement ne fonctionne pas, s'il ne fonctionne pas.</summary>
    public string? Empechement { get; private set; }

    public DepotFichiers(IWebHostEnvironment env, IConfiguration config, ILogger<DepotFichiers> journal)
    {
        _journal = journal;

        // Aucune de ces tentatives ne doit faire echouer le demarrage :
        // un serveur qui refuse d'ecrire quelque part doit donner un site
        // sans consultation de documents, pas un site eteint. La lecon a
        // coute une interruption de service.
        var candidats = new[]
        {
            config["Fichiers:Racine"],
            RacineParDefaut,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                         "LaPlateformeDeLemploi", "Documents"),
            Path.Combine(Path.GetTempPath(), "lpde-documents"),
        };

        var refus = new List<string>();
        foreach (var candidat in candidats)
        {
            if (string.IsNullOrWhiteSpace(candidat)) continue;
            try
            {
                Directory.CreateDirectory(candidat);

                // Creer le dossier ne prouve pas qu'on peut y ecrire.
                var temoin = Path.Combine(candidat, ".ecriture");
                File.WriteAllText(temoin, "");
                File.Delete(temoin);

                Racine = candidat;
                _journal.LogInformation("Depot des documents : {Racine}", Racine);
                return;
            }
            catch (Exception ex)
            {
                refus.Add($"{candidat} ({ex.GetType().Name})");
            }
        }

        Racine = candidats.Last()!;
        Empechement = "aucun dossier inscriptible — " + string.Join(", ", refus);
        _journal.LogError("Depot des documents indisponible : {Empechement}", Empechement);
    }

    /// <summary>
    /// Le nom de fichier porte par un chemin enregistre, ou null si le
    /// chemin ne ressemble a rien de connu.
    ///
    /// Refuse tout ce qui pourrait sortir du dossier : un nom est un nom,
    /// pas un chemin. « ../../appsettings.json » n'a rien a faire ici.
    /// </summary>
    public static string? Nom(string? chemin)
    {
        if (string.IsNullOrWhiteSpace(chemin)) return null;

        var nom = chemin.Split('/', '\\').LastOrDefault();
        if (string.IsNullOrWhiteSpace(nom)) return null;
        if (nom is "." or "..") return null;
        if (nom.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        if (!nom.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return null;

        return nom;
    }

    /// <summary>
    /// Le sous-dossier ou ranger un fichier, deduit de son nom.
    ///
    /// « {identifiant}_{yyyyMMddHHmmss}.pdf » porte sa propre date : le
    /// classement se recalcule a la lecture comme a l'ecriture, sans
    /// rien enregistrer de plus. Un nom qui ne suivrait pas cette forme
    /// atterrit dans « divers » plutot que d'etre refuse — c'est un
    /// document de membre, pas une donnee jetable.
    /// </summary>
    private string Classement(string nom, string nature)
    {
        var separateur = nom.LastIndexOf('_');
        if (separateur > 0 && separateur + 15 <= nom.Length)
        {
            var horodatage = nom.Substring(separateur + 1, 14);
            if (DateTime.TryParseExact(horodatage, "yyyyMMddHHmmss",
                                       CultureInfo.InvariantCulture, DateTimeStyles.None, out var quand))
                return Path.Combine(Racine, nature, quand.ToString("yyyy"), quand.ToString("MM"));
        }
        return Path.Combine(Racine, nature, "divers");
    }

    /// <summary>
    /// Le document sur le disque, ou null s'il n'existe pas.
    ///
    /// L'emplacement calcule d'abord — c'est le cas de tous les depots
    /// recents. La recherche complete ensuite, pour ceux d'avant le
    /// classement : mieux vaut une seconde de recherche qu'un CV
    /// introuvable.
    /// </summary>
    public string? Chemin(string? cheminEnregistre, string nature = Cv)
    {
        var nom = Nom(cheminEnregistre);
        if (nom == null) return null;

        var attendu = Path.Combine(Classement(nom, nature), nom);
        if (File.Exists(attendu)) return attendu;

        try
        {
            return Directory.EnumerateFiles(Racine, nom, SearchOption.AllDirectories).FirstOrDefault();
        }
        catch (Exception ex)
        {
            _journal.LogWarning(ex, "Depot illisible en cherchant « {Nom} »", nom);
            return null;
        }
    }

    /// <summary>Ou ecrire un nouveau depot ; le dossier est cree au besoin.</summary>
    public string Destination(string nomFichier, string nature = Cv)
    {
        var dossier = Classement(nomFichier, nature);
        Directory.CreateDirectory(dossier);
        return Path.Combine(dossier, nomFichier);
    }

    /// <summary>
    /// Efface un document depose. Silencieux s'il a deja disparu : ce qui
    /// compte est qu'il ne soit plus la, pas qu'on l'ait enleve soi-meme.
    /// </summary>
    public bool Effacer(string? cheminEnregistre, string nature = Cv)
    {
        var complet = Chemin(cheminEnregistre, nature);
        if (complet == null) return false;
        try
        {
            File.Delete(complet);
            return true;
        }
        catch (Exception ex)
        {
            _journal.LogWarning(ex, "Document « {Chemin} » non efface", cheminEnregistre);
            return false;
        }
    }

    /// <summary>
    /// Efface tous les documents d'un membre, versions anterieures
    /// comprises, quelle que soit l'annee ou ils sont ranges.
    ///
    /// Un nouveau depot ne remplacait pas l'ancien — l'horodatage change
    /// le nom — si bien qu'un compte accumule ses CV successifs. Effacer
    /// le dernier n'effacerait donc rien de ce qui reste lisible.
    /// </summary>
    public int EffacerTousDe(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return 0;
        if (userId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return 0;

        var efface = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(Racine, userId + "_*.pdf", SearchOption.AllDirectories))
            {
                try { File.Delete(f); efface++; }
                catch (Exception ex) { _journal.LogWarning(ex, "Document « {Fichier} » non efface", f); }
            }
        }
        catch (Exception ex)
        {
            _journal.LogWarning(ex, "Depot illisible pour le membre {Membre}", userId);
        }
        return efface;
    }

    /// <summary>
    /// Range les documents des emplacements precedents, une fois pour
    /// toutes.
    ///
    /// Appele au demarrage : la mise en production doit fermer la fuite
    /// et appliquer le classement sans que personne ait a deplacer des
    /// fichiers a la main sur le serveur.
    /// </summary>
    public void Ranger(IWebHostEnvironment env)
    {
        try { RangerVraiment(env); }
        catch (Exception ex)
        {
            // Le rangement est une commodite, pas une condition de
            // service. Un demarrage ne se perd pas pour cela.
            _journal.LogError(ex, "Rangement des documents impossible");
        }
    }

    private void RangerVraiment(IWebHostEnvironment env)
    {
        // Les emplacements successifs, du plus ancien au plus recent :
        // wwwroot, ou les fichiers etaient servis sans authentification ;
        // le dossier de l'application, que le deploiement effacait ; puis
        // ProgramData. Et la racine elle-meme, dont les fichiers poses a
        // plat attendent leur classement.
        var anciens = new[]
        {
            Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "uploads", "resumes"),
            Path.Combine(env.ContentRootPath, "donnees", "cv"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                         "LaPlateformeDeLemploi", "cv"),
            Racine,
        };

        foreach (var ancien in anciens.Distinct(StringComparer.OrdinalIgnoreCase))
            Vider(ancien);
    }

    private void Vider(string ancien)
    {
        if (!Directory.Exists(ancien)) return;

        var deplaces = 0;

        // « TopDirectoryOnly » : les fichiers deja ranges dans leur
        // annee n'ont rien a faire ici, et se reparcourir soi-meme en
        // profondeur reviendrait a les deplacer sur eux-memes.
        foreach (var source in Directory.EnumerateFiles(ancien, "*.pdf", SearchOption.TopDirectoryOnly))
        {
            var nom = Path.GetFileName(source);
            var cible = Destination(nom);
            if (string.Equals(source, cible, StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                // Si les deux existent, le fichier a deja ete range lors
                // d'un demarrage precedent : on enleve la copie restee en
                // arriere, c'est elle le probleme.
                if (File.Exists(cible)) File.Delete(source);
                else File.Move(source, cible);
                deplaces++;
            }
            catch (Exception ex)
            {
                _journal.LogError(ex, "Document « {Fichier} » non range depuis {Ancien}", source, ancien);
            }
        }

        if (deplaces > 0)
            _journal.LogWarning("{Nombre} document(s) rangés depuis {Ancien}", deplaces, ancien);

        try
        {
            if (!string.Equals(ancien, Racine, StringComparison.OrdinalIgnoreCase)
                && !Directory.EnumerateFileSystemEntries(ancien).Any())
                Directory.Delete(ancien);
        }
        catch { /* le dossier vide qui subsiste ne gene personne */ }
    }
}
