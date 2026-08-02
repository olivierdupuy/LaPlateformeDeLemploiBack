using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using lpdeBack.Data;

namespace lpdeBack.Tests;

/// <summary>
/// Une base jetable, le temps d'un test.
///
/// SQLite en memoire plutot que le fournisseur « InMemory » d'EF : ce
/// dernier n'est pas une base relationnelle, il ne connait ni cle
/// etrangere, ni unicite, ni transaction. Un test qui passe dessus ne
/// dit donc rien de ce qui se passera en production — et c'est
/// precisement sur ces regles-la que porte la facturation, dont le
/// numero doit etre unique et sans trou.
///
/// La connexion est gardee ouverte volontairement : une base SQLite
/// « :memory: » disparait avec sa derniere connexion, et le contexte
/// EF en ouvre puis en ferme une par requete. Sans cette connexion
/// tenue a part, la base s'evaporerait entre deux appels.
/// </summary>
public sealed class BaseEnMemoire : IDisposable
{
    private readonly SqliteConnection _connexion;

    public AppDbContext Contexte { get; }

    public BaseEnMemoire()
    {
        _connexion = new SqliteConnection("DataSource=:memory:");
        _connexion.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connexion)
            .Options;

        Contexte = new AppDbContext(options);
        Contexte.Database.EnsureCreated();
    }

    /// <summary>Un service de facturation branche sur cette base.</summary>
    public lpdeBack.Services.FacturationService Facturation() =>
        new(Contexte, NullLogger<lpdeBack.Services.FacturationService>.Instance);

    /// <summary>
    /// Cree un compte, et le rend.
    ///
    /// SQLite fait respecter les cles etrangeres, ce que le fournisseur
    /// « InMemory » ne fait pas : une offre rattachee a un identifiant
    /// de recruteur inexistant est refusee ici, et acceptee la-bas. La
    /// contrainte est genante dans les tests, et c'est exactement ce
    /// qu'on lui demande — elle est aussi ce qui protege la base en
    /// production.
    /// </summary>
    public lpdeBack.Models.AppUser Compte(string id, string role = "Recruiter")
    {
        var compte = new lpdeBack.Models.AppUser
        {
            Id = id,
            UserName = $"{id}@exemple.fr",
            NormalizedUserName = $"{id}@EXEMPLE.FR",
            Email = $"{id}@exemple.fr",
            NormalizedEmail = $"{id}@EXEMPLE.FR",
            FirstName = "Test",
            LastName = id,
            Role = role,
            SecurityStamp = Guid.NewGuid().ToString(),
        };

        Contexte.Users.Add(compte);
        Contexte.SaveChanges();
        return compte;
    }

    public void Dispose()
    {
        Contexte.Dispose();
        _connexion.Dispose();
    }
}
