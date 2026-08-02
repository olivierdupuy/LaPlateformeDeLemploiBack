using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace lpdeBack.Services;

/// <summary>
/// Les requetes qui trainent.
///
/// Une page lente ne dit pas pourquoi elle est lente. On soupconne le
/// reseau, on soupconne le client, et pendant ce temps une seule
/// requete balaie cent mille offres parce qu'un index manque sur une
/// colonne ajoutee six mois plus tot. Rien ne le signalait : EF ne se
/// plaint pas, il attend.
///
/// Cet intercepteur ne journalise que ce qui depasse un seuil. Tout
/// journaliser reviendrait a ne rien journaliser — le volume noierait
/// les cinq requetes qui comptent sous des millions de lectures a deux
/// millisecondes, et couterait plus cher que ce qu'il mesure.
///
/// Le texte SQL est tronque : il sert a reconnaitre la requete, pas a
/// la rejouer. Les parametres ne sont jamais journalises, ils
/// contiennent des adresses et des noms.
/// </summary>
public sealed class RequetesLentes : DbCommandInterceptor
{
    private readonly ILogger<RequetesLentes> _journal;
    private readonly TimeSpan _seuil;

    public RequetesLentes(ILogger<RequetesLentes> journal, IConfiguration config)
    {
        _journal = journal;
        // 500 ms : au-dela, la lenteur se voit a l'ecran. En dessous,
        // c'est du reglage fin qui ne vaut pas une ligne de journal.
        var ms = int.TryParse(config["Diagnostics:SeuilRequeteLenteMs"], out var v) ? v : 500;
        _seuil = TimeSpan.FromMilliseconds(ms);
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Noter(command, eventData.Duration);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Noter(command, eventData.Duration);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(
        DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Noter(command, eventData.Duration);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Noter(command, eventData.Duration);
        return base.NonQueryExecuted(command, eventData, result);
    }

    private void Noter(DbCommand command, TimeSpan duree)
    {
        if (duree < _seuil) return;

        var sql = command.CommandText.ReplaceLineEndings(" ");
        if (sql.Length > 800) sql = sql[..800] + " […]";

        _journal.LogWarning(
            "Requete lente : {DureeMs} ms — {Sql}",
            (int)duree.TotalMilliseconds, sql);

        Retenir(sql, (int)duree.TotalMilliseconds);
    }

    // ══════════════════════════════════════
    //  De quoi le voir depuis la console
    // ══════════════════════════════════════
    //
    // Le journal Serilog dit tout, et personne ne l'ouvre : il faut un
    // acces au serveur, et savoir quoi y chercher. Une page lente se
    // constate depuis un navigateur, et c'est de la que la question se
    // pose — « pourquoi celle-ci traine ». On garde donc en memoire de
    // quoi y repondre.
    //
    // Par forme de requete et non par occurrence : la meme requete lente
    // appelee deux cents fois est un seul probleme, pas deux cents. Ce
    // qu'on veut savoir, c'est laquelle, combien de fois, et au pire
    // combien de temps.

    /// <summary>Une forme de requete lente, et ce qu'elle a coute.</summary>
    /// <param name="Sql">Le texte, tronque. Les parametres n'y sont jamais :
    /// ils contiennent des adresses et des noms.</param>
    public sealed record Trace(string Sql, int Occurrences, int PireMs, int DernierMs, DateTime Derniere);

    /// <summary>
    /// Combien de formes distinctes on retient.
    ///
    /// Cinquante : au-dela, ce n'est plus un diagnostic mais un journal,
    /// et un journal en memoire finit par la remplir. Quand le tableau
    /// est plein, la forme la moins recente cede sa place.
    /// </summary>
    private const int FormesRetenues = 50;

    private sealed class Cumul
    {
        public int Occurrences;
        public int PireMs;
        public int DernierMs;
        public DateTime Derniere;
    }

    private static readonly ConcurrentDictionary<string, Cumul> _formes = new();

    private static void Retenir(string sql, int ms)
    {
        var cumul = _formes.GetOrAdd(sql, _ => new Cumul());

        // Sans verrou : deux requetes lentes simultanees sur la meme forme
        // peuvent perdre une unite de compteur. Le chiffre sert a classer,
        // pas a facturer — un verrou par requete couterait plus que
        // l'exactitude ne rapporte.
        cumul.Occurrences++;
        cumul.DernierMs = ms;
        cumul.Derniere = DateTime.UtcNow;
        if (ms > cumul.PireMs) cumul.PireMs = ms;

        if (_formes.Count > FormesRetenues)
        {
            var plusVieille = _formes.OrderBy(f => f.Value.Derniere).FirstOrDefault();
            if (plusVieille.Key is not null) _formes.TryRemove(plusVieille.Key, out _);
        }
    }

    /// <summary>Les formes les plus couteuses d'abord : le pire temps mesure.</summary>
    public static IReadOnlyList<Trace> Rapport() =>
        _formes
            .Select(f => new Trace(f.Key, f.Value.Occurrences, f.Value.PireMs,
                                   f.Value.DernierMs, f.Value.Derniere))
            .OrderByDescending(t => t.PireMs)
            .ToList();

    /// <summary>
    /// Repartir de zero.
    ///
    /// Apres avoir pose l'index qui manquait, on veut savoir si la
    /// requete est encore lente — et non relire un cumul qui date d'avant
    /// le correctif.
    /// </summary>
    public static void Oublier() => _formes.Clear();
}
