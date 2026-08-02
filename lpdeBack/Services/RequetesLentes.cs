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
    }
}
