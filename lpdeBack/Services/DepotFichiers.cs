namespace lpdeBack.Services;

/// <summary>
/// Le rangement des fichiers deposes par les membres.
///
/// Ils vivaient sous « wwwroot/uploads/resumes », c'est-a-dire dans le
/// dossier que « UseStaticFiles » sert au monde entier : n'importe qui
/// connaissant l'adresse telechargeait le CV, sans jeton ni session. Et
/// l'adresse n'etait pas un secret — le nom du fichier est bati sur
/// l'identifiant du membre, lequel circule dans les reponses de l'API.
///
/// Ils vivent desormais hors de wwwroot. Rien ne les sert plus tout
/// seul : seul « FichiersController » y donne acces, apres avoir verifie
/// qui demande quoi.
///
/// La base continue de stocker « /uploads/resumes/xxx.pdf ». Ce chemin
/// n'est plus une adresse, c'est un identifiant : le changer imposerait
/// de reecrire chaque ligne existante pour un gain nul.
/// </summary>
public sealed class DepotFichiers
{
    private readonly ILogger<DepotFichiers> _journal;

    /// <summary>Le prefixe historique, conserve dans les enregistrements.</summary>
    public const string Prefixe = "/uploads/resumes/";

    public string Racine { get; }

    public DepotFichiers(IWebHostEnvironment env, IConfiguration config, ILogger<DepotFichiers> journal)
    {
        _journal = journal;

        // Configurable, car sur le serveur les donnees ont interet a
        // survivre a un redeploiement qui remplace le dossier de
        // l'application.
        var configure = config["Fichiers:Racine"];
        Racine = string.IsNullOrWhiteSpace(configure)
            ? Path.Combine(env.ContentRootPath, "donnees", "cv")
            : configure;

        Directory.CreateDirectory(Racine);
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

    /// <summary>Le fichier sur le disque, ou null s'il n'existe pas.</summary>
    public string? Chemin(string? cheminEnregistre)
    {
        var nom = Nom(cheminEnregistre);
        if (nom == null) return null;

        var complet = Path.Combine(Racine, nom);

        // Ceinture et bretelles : meme apres le filtrage du nom, on
        // verifie que le resultat est bien dans le dossier.
        if (!complet.StartsWith(Racine, StringComparison.OrdinalIgnoreCase)) return null;

        return File.Exists(complet) ? complet : null;
    }

    /// <summary>Ou ecrire un nouveau depot.</summary>
    public string Destination(string nomFichier) => Path.Combine(Racine, nomFichier);

    /// <summary>
    /// Efface un fichier depose. Silencieux s'il a deja disparu : ce qui
    /// compte est qu'il ne soit plus la, pas qu'on l'ait enleve soi-meme.
    /// </summary>
    public bool Effacer(string? cheminEnregistre)
    {
        var complet = Chemin(cheminEnregistre);
        if (complet == null) return false;
        try
        {
            File.Delete(complet);
            return true;
        }
        catch (Exception ex)
        {
            _journal.LogWarning(ex, "Fichier « {Chemin} » non efface", cheminEnregistre);
            return false;
        }
    }

    /// <summary>
    /// Efface tous les fichiers d'un membre, versions anterieures
    /// comprises.
    ///
    /// Un nouveau depot ne remplacait pas l'ancien — l'horodatage change
    /// le nom — si bien qu'un compte accumule ses CV successifs. Effacer
    /// le dernier n'effacerait donc rien de ce qui reste lisible.
    /// </summary>
    public int EffacerTousDe(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return 0;

        var efface = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(Racine, userId + "_*.pdf"))
            {
                try { File.Delete(f); efface++; }
                catch (Exception ex) { _journal.LogWarning(ex, "Fichier « {Fichier} » non efface", f); }
            }
        }
        catch (Exception ex)
        {
            _journal.LogWarning(ex, "Depot illisible pour le membre {Membre}", userId);
        }
        return efface;
    }

    /// <summary>
    /// Sort les fichiers de wwwroot, une fois pour toutes.
    ///
    /// Appele au demarrage : la mise en production doit fermer la fuite
    /// sans que personne ait a se souvenir de deplacer des fichiers a la
    /// main sur le serveur.
    /// </summary>
    public void RapatrierDepuisWwwroot(IWebHostEnvironment env)
    {
        var ancien = Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"),
                                  "uploads", "resumes");
        if (!Directory.Exists(ancien)) return;

        var deplaces = 0;
        foreach (var source in Directory.EnumerateFiles(ancien, "*.pdf"))
        {
            var cible = Path.Combine(Racine, Path.GetFileName(source));
            try
            {
                // Si les deux existent, le fichier a deja ete rapatrie
                // lors d'un demarrage precedent : on enleve la copie
                // publique, c'est elle le probleme.
                if (File.Exists(cible)) File.Delete(source);
                else File.Move(source, cible);
                deplaces++;
            }
            catch (Exception ex)
            {
                _journal.LogError(ex, "Fichier « {Fichier} » toujours exposé dans wwwroot", source);
            }
        }

        if (deplaces > 0)
            _journal.LogWarning("{Nombre} fichier(s) sortis de wwwroot : ils n'étaient servis sans authentification", deplaces);

        try
        {
            if (!Directory.EnumerateFileSystemEntries(ancien).Any()) Directory.Delete(ancien);
        }
        catch { /* le dossier vide qui subsiste ne gene personne */ }
    }
}
