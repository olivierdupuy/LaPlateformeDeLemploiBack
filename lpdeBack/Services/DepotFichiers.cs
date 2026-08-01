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

    /// <summary>Pourquoi le rangement ne fonctionne pas, s'il ne fonctionne pas.</summary>
    public string? Empechement { get; private set; }

    public DepotFichiers(IWebHostEnvironment env, IConfiguration config, ILogger<DepotFichiers> journal)
    {
        _journal = journal;

        // ── Ou ranger, dans l'ordre de preference ──
        //
        // Surtout PAS dans le dossier de l'application. Le deploiement se
        // fait par « msdeploy -verb:sync », qui rend la destination
        // identique a la source : tout ce que l'application a ecrit chez
        // elle disparait a la mise en ligne suivante. Les CV vivent donc
        // a cote, dans un dossier que le deploiement ne regarde pas.
        //
        // Et aucune de ces tentatives ne doit faire echouer le
        // demarrage : un serveur qui refuse d'ecrire quelque part doit
        // donner un site sans consultation de CV, pas un site eteint.
        var candidats = new[]
        {
            config["Fichiers:Racine"],
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                         "LaPlateformeDeLemploi", "cv"),
            Path.Combine(env.ContentRootPath, "donnees", "cv"),
            Path.Combine(Path.GetTempPath(), "lpde-cv"),
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
                _journal.LogInformation("Depot des fichiers : {Racine}", Racine);
                return;
            }
            catch (Exception ex)
            {
                refus.Add($"{candidat} ({ex.GetType().Name})");
            }
        }

        // Aucun n'a marche. On demarre quand meme : « Chemin » rendra
        // null, les CV repondront 404, et la sonde de sante le dira.
        Racine = candidats.Last()!;
        Empechement = "aucun dossier inscriptible — " + string.Join(", ", refus);
        _journal.LogError("Depot des fichiers indisponible : {Empechement}", Empechement);
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
        try { RapatrierVraiment(env); }
        catch (Exception ex)
        {
            // Le rapatriement est une commodite, pas une condition de
            // service. Un demarrage ne se perd pas pour cela.
            _journal.LogError(ex, "Rapatriement des fichiers impossible");
        }
    }

    private void RapatrierVraiment(IWebHostEnvironment env)
    {
        // Deux emplacements a vider : celui d'origine, dans wwwroot, ou
        // les fichiers etaient servis sans authentification ; et celui
        // du premier correctif, sous le dossier de l'application, ou le
        // deploiement les aurait effaces.
        foreach (var ancien in new[]
                 {
                     Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"),
                                  "uploads", "resumes"),
                     Path.Combine(env.ContentRootPath, "donnees", "cv"),
                 })
        {
            if (!string.Equals(ancien, Racine, StringComparison.OrdinalIgnoreCase))
                Vider(ancien);
        }
    }

    private void Vider(string ancien)
    {
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
                _journal.LogError(ex, "Fichier « {Fichier} » non rapatrié depuis {Ancien}", source, ancien);
            }
        }

        if (deplaces > 0)
            _journal.LogWarning("{Nombre} fichier(s) rapatriés depuis {Ancien} vers {Racine}",
                                deplaces, ancien, Racine);

        try
        {
            if (!Directory.EnumerateFileSystemEntries(ancien).Any()) Directory.Delete(ancien);
        }
        catch { /* le dossier vide qui subsiste ne gene personne */ }
    }
}
