using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using lpdeBack.Data;
using lpdeBack.Models;

namespace lpdeBack.Tests;

/// <summary>
/// L'API entiere, montee en memoire.
///
/// La difference avec un test de service tient en un mot :
/// <c>[Authorize]</c>. Appeler une methode de controleur directement
/// saute l'authentification, l'autorisation par role, les filtres et la
/// negociation de contenu — c'est-a-dire precisement la couche ou se
/// logent les failles qu'on veut eprouver. Un candidat qui appelle
/// <c>SupprimerOffre</c> en direct verra la methode s'executer ; le
/// meme appel a travers le pipeline recoit un 403.
///
/// Trois substitutions, et rien d'autre :
///
///   — SQL Server devient SQLite en memoire. Relationnel, avec cles
///     etrangeres, donc fidele sur ce qui nous interesse ;
///   — les taches de fond sont retirees. Elles importeraient des offres
///     et enverraient des courriels pendant les tests ;
///   — l'environnement est « Test », ce que le demarrage reconnait pour
///     sauter migrations et donnees de demonstration.
/// </summary>
public class ApiEnMemoire : WebApplicationFactory<Program>
{
    private SqliteConnection? _connexion;

    private const string CleJwt = "cle-de-test-suffisamment-longue-pour-hmac-sha256-0123456789";
    private const string Emetteur = "LpdeBack";
    private const string Public = "LpdeFront";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        // La racine de contenu est posee a la main, et il le faut.
        //
        // `Mvc.Testing` la deduit normalement d'un manifeste genere a la
        // compilation, dont la valeur est une expression MSBuild :
        // « $([System.IO.Path]::GetDirectoryName('…/lpdeBack.csproj')) ».
        // Le chemin du depot contient une apostrophe — « La Plateforme
        // de l'emploi » — qui referme la chaine MSBuild au milieu du
        // mot. L'expression n'est jamais evaluee, et le serveur cherche
        // un repertoire litteralement nomme « $([System.IO.Path]… ».
        builder.UseContentRoot(RacineDuProjetWeb());

        builder.UseSetting("Jwt:Key", CleJwt);
        builder.UseSetting("Jwt:Issuer", Emetteur);
        builder.UseSetting("Jwt:Audience", Public);
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=(sans-objet);Database=Test;");

        builder.ConfigureServices(services =>
        {
            // ── La base ──
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();

            _connexion = new SqliteConnection("DataSource=:memory:");
            _connexion.Open();

            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connexion));

            // ── Les taches de fond ──
            foreach (var service in services.Where(s => s.ServiceType == typeof(IHostedService)).ToList())
                services.Remove(service);

            // ── Le schema ──
            using var fournisseur = services.BuildServiceProvider();
            using var portee = fournisseur.CreateScope();
            var db = portee.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        });
    }

    /// <summary>
    /// Remonte depuis le repertoire de sortie jusqu'au projet web.
    ///
    /// Cherche plutot que de compter les « .. » : le nombre de niveaux
    /// change entre Debug et Release, et entre une execution locale et
    /// celle de l'integration continue.
    /// </summary>
    private static string RacineDuProjetWeb()
    {
        var repertoire = new DirectoryInfo(AppContext.BaseDirectory);

        while (repertoire is not null)
        {
            var candidat = Path.Combine(repertoire.FullName, "lpdeBack", "lpdeBack.csproj");
            if (File.Exists(candidat)) return Path.GetDirectoryName(candidat)!;
            repertoire = repertoire.Parent;
        }

        throw new DirectoryNotFoundException(
            "Projet lpdeBack introuvable au-dessus de " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// Un client porteur d'un jeton pour ce compte.
    ///
    /// Le jeton est forge avec la meme cle que celle donnee a
    /// l'application : il traverse donc la validation reelle, signature
    /// comprise. Un jeton fabrique de toutes pieces ne prouverait rien.
    /// </summary>
    public HttpClient ClientPour(AppUser compte)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Jeton(compte));
        return client;
    }

    /// <summary>Un client sans jeton : le visiteur anonyme.</summary>
    public HttpClient ClientAnonyme() => CreateClient();

    private static string Jeton(AppUser compte)
    {
        var revendications = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, compte.Id),
            new(ClaimTypes.Name, compte.UserName ?? compte.Id),
            new(ClaimTypes.Email, compte.Email ?? ""),
            new(ClaimTypes.Role, compte.Role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var cle = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CleJwt));

        var jeton = new JwtSecurityToken(
            issuer: Emetteur,
            audience: Public,
            claims: revendications,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(cle, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(jeton);
    }

    /// <summary>Fait quelque chose dans la base, hors requete HTTP.</summary>
    public async Task<T> DansLaBase<T>(Func<AppDbContext, Task<T>> quoi)
    {
        using var portee = Services.CreateScope();
        var db = portee.ServiceProvider.GetRequiredService<AppDbContext>();
        return await quoi(db);
    }

    /// <summary>Cree un compte et le rend, jeton compris.</summary>
    public async Task<AppUser> Compte(string id, string role, string? entreprise = null)
    {
        return await DansLaBase(async db =>
        {
            var compte = new AppUser
            {
                Id = id,
                UserName = $"{id}@exemple.fr",
                NormalizedUserName = $"{id}@EXEMPLE.FR",
                Email = $"{id}@exemple.fr",
                NormalizedEmail = $"{id}@EXEMPLE.FR",
                EmailConfirmed = true,
                FirstName = "Test",
                LastName = id,
                Role = role,
                Company = entreprise,
                SecurityStamp = Guid.NewGuid().ToString(),
            };

            db.Users.Add(compte);

            // Le pipeline verifie que la session du jeton existe encore :
            // un jeton dont la session a ete coupee ne vaut plus rien,
            // c'est ce qui rend la revocation possible. Les tests
            // n'ouvrent pas de session, et le controle ne s'applique
            // qu'aux jetons qui en declarent une.
            await db.SaveChangesAsync();
            return compte;
        });
    }

    /// <summary>Cree une offre et rend son identifiant.</summary>
    public async Task<int> Offre(string auteurId, bool brouillon = false, bool active = true,
                                 string titre = "Developpeur")
    {
        return await DansLaBase(async db =>
        {
            var offre = new JobOffer
            {
                Title = titre,
                Company = "TechCorp",
                Location = "Marseille",
                Description = "Un poste.",
                CreatedByUserId = auteurId,
                IsActive = active,
                IsDraft = brouillon,
                ModerationStatus = "Approved",
            };
            db.JobOffers.Add(offre);
            await db.SaveChangesAsync();
            return offre.Id;
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connexion?.Dispose();
    }
}
