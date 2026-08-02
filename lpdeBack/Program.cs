using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Serilog;
using lpdeBack.Data;
using lpdeBack.Models;

var builder = WebApplication.CreateBuilder(args);

// ══════════════════════════════════════════════
//  Journalisation
//
//  Le journal par defaut ecrit sur la console, que personne ne regarde
//  sur un serveur IIS, et dans le journal des evenements Windows, ou
//  rien n'est cherchable. Une exception rattrapee par le filet portait
//  bien une reference — « 4F2A9C31 » — mais retrouver la ligne
//  correspondante supposait d'ouvrir une session sur la machine.
//
//  Serilog ecrit un fichier par jour, garde trente jours, et sort en
//  texte structure : la reference, la route et la duree sont des
//  proprietes, pas des morceaux de phrase. Trente jours parce que
//  c'est la duree pendant laquelle on enquete encore sur un incident,
//  et parce qu'au-dela ces fichiers contiennent des adresses IP dont
//  la conservation n'est plus justifiee.
//
//  Le repertoire vit hors du site publie : « msdeploy sync » efface ce
//  qu'il ne connait pas, et un journal efface a chaque deploiement
//  n'aurait jamais la trace de ce que le deploiement a casse.
// ══════════════════════════════════════════════
var repertoireJournal = builder.Configuration["Diagnostics:RepertoireJournal"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "..", "journaux");

builder.Host.UseSerilog((contexte, services, config) => config
    .ReadFrom.Configuration(contexte.Configuration)
    .Enrich.FromLogContext()
    // Le bruit d'EF et du routage noierait tout le reste. Les requetes
    // lentes remontent par ailleurs, via leur propre intercepteur.
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(repertoireJournal, "lpde-.log"),
        rollingInterval: Serilog.RollingInterval.Day,
        retainedFileCountLimit: 30,
        // Un incident peut produire beaucoup en peu de temps ; sans
        // plafond par fichier, le disque se remplit et le site s'arrete
        // pour de bon.
        fileSizeLimitBytes: 64 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

// Firebase Admin SDK
//
// « DefaultInstance is null » : l'instance par defaut est globale au
// processus, et « Create » leve si elle existe deja. En production
// l'application ne demarre qu'une fois, mais deux classes de tests
// d'integration montent deux hotes dans le meme processus — la seconde
// echouait alors au demarrage, pour une raison sans rapport avec ce
// qu'elle verifiait.
var firebaseCredPath = Path.Combine(builder.Environment.ContentRootPath, "firebase-service-account.json");
if (File.Exists(firebaseCredPath) && FirebaseApp.DefaultInstance is null)
{
    FirebaseApp.Create(new AppOptions
    {
        Credential = GoogleCredential.FromFile(firebaseCredPath)
    });
}

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

// ── Ce qu'on repond a une saisie refusee ──
//
// « [ApiController] » rend d'office un ValidationProblemDetails : un
// objet « errors » indexe par nom de propriete, avec des phrases en
// anglais — « The Email field is not a valid e-mail address. » Le client
// lit « message » et ne trouvait rien : toute saisie fautive produisait
// le meme message generique, qui n'indiquait ni le champ ni la raison.
//
// On rend donc la premiere phrase sous « message », pour que l'existant
// l'affiche sans rien changer, et la liste complete sous « erreurs »,
// indexee par champ en minuscule initiale — la casse que le client
// emploie — pour souligner les champs un par un.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var erreurs = context.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                e => string.IsNullOrEmpty(e.Key) ? "_" : char.ToLowerInvariant(e.Key[0]) + e.Key[1..],
                e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray());

        var premiere = erreurs.Values.SelectMany(v => v).FirstOrDefault()
                       ?? "Certaines informations sont incomplètes ou mal formées.";

        return new BadRequestObjectResult(new { message = premiere, erreurs });
    };
});

// ══════════════════════════════════════════════
//  Limitation de debit
//
//  Il n'y en avait aucune. Le blocage apres cinq essais protege un
//  compte connu, et le plafond horaire protege le solde de SMS ; tout
//  le reste — inscriptions, mots de passe oublies, abonnements, avis,
//  contributions de salaire, et surtout la generation de CV par IA,
//  facturee au jeton a chaque appel — pouvait etre appele en boucle.
//
//  Les plafonds sont cales sur l'usage reel, pas sur la prudence : trop
//  bas, ils bloqueraient un bureau entier derriere une seule adresse ;
//  trop hauts, ils ne serviraient a rien. Ils comptent par adresse, sauf
//  celui de l'IA qui compte par compte — c'est la personne qui coute.
// ══════════════════════════════════════════════
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Le client lit « message » : sans cela, un refus arriverait vide et
    // l'interface afficherait une erreur technique sans explication.
    options.OnRejected = async (contexte, jeton) =>
    {
        var attente = contexte.Lease.TryGetMetadata(MetadataName.RetryAfter, out var d)
            ? (int)d.TotalSeconds : 60;

        contexte.HttpContext.Response.Headers.RetryAfter = attente.ToString();
        contexte.HttpContext.Response.ContentType = "application/json";
        await contexte.HttpContext.Response.WriteAsJsonAsync(new
        {
            message = $"Trop de tentatives depuis cet appareil. Réessayez dans {Math.Max(1, attente / 60)} minute"
                      + (attente >= 120 ? "s." : "."),
            secondesAAttendre = attente,
        }, jeton);
    };

    // Garde-fou general : il ne vise pas l'usage, meme intensif, mais le
    // moissonnage — vingt requetes par seconde soutenues ne viennent pas
    // d'une personne qui consulte des offres.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetFixedWindowLimiter(
            lpdeBack.Validation.AntiRobot.Client(http),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 1_200,
                Window = TimeSpan.FromMinutes(1),
            }));

    // Tout ce qui touche a l'identite : connexion, inscription, code de
    // second facteur, mot de passe oublie.
    //
    // Trente par minute, et non dix. Dix paraissait prudent jusqu'a ce
    // qu'on mesure : une seule adresse couvre souvent tout un bureau
    // derriere un NAT, et vingt personnes qui se connectent un lundi
    // matin epuisaient le quota avant la moitie. Une batterie de tests
    // l'a montre par accident — cinq essais de mot de passe faux, qui
    // eprouvaient le verrouillage de compte, se faisaient couper avant
    // d'y arriver.
    //
    // Ce n'est pas un relachement : ce n'est pas ce compteur qui arrete
    // qui devine un mot de passe, c'est le verrouillage du compte au
    // cinquieme echec. Celui-ci borne le VOLUME — le balayage d'une
    // liste d'adresses, l'inondation d'inscriptions — et trente par
    // minute le borne tout aussi bien, puisque chaque compte se ferme
    // de son cote apres cinq tentatives.
    options.AddPolicy("identite", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            lpdeBack.Validation.AntiRobot.Client(http),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
            }));

    // ── API publique ──
    // Un partenaire equipe appelle plus vite qu'une personne, et c'est
    // normal : il synchronise. Le plafond compte par cle et non par
    // adresse — deux clients derriere le meme reseau d'entreprise ne
    // doivent pas se penaliser l'un l'autre — et il est cale sur une
    // synchronisation raisonnable, pas sur un moissonnage.
    options.AddPolicy("catalogue-api", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Request.Headers.Authorization.FirstOrDefault() ?? lpdeBack.Validation.AntiRobot.Client(http),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 600,
                Window = TimeSpan.FromMinutes(1),
            }));

    // L'abonnement a la lettre. Le double opt-in protege deja l'abonne —
    // rien ne part tant qu'il n'a pas confirme — mais rien n'empechait de
    // s'en servir pour noyer une victime sous des demandes de
    // confirmation. Personne ne s'abonne cinq fois en une heure.
    options.AddPolicy("abonnement", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            lpdeBack.Validation.AntiRobot.Client(http),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromHours(1),
            }));

    // Ce qui se depose publiquement : candidatures, avis, contributions
    // de salaire, signalements. Trente par heure couvrent largement une
    // recherche d'emploi active — on ne postule pas a trente offres par
    // heure en ecrivant a chacune.
    options.AddPolicy("publication", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            lpdeBack.Validation.AntiRobot.Compte(http),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromHours(1),
            }));

    // La generation par IA est la seule qui coute de l'argent a chaque
    // appel. Elle se compte par compte et non par adresse : c'est la
    // personne qu'on autorise, et changer d'adresse ne doit pas remettre
    // le compteur a zero.
    options.AddPolicy("ia", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            lpdeBack.Validation.AntiRobot.Compte(http),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromHours(1),
            }));
});

// Le filtre s'applique de lui-meme a tout formulaire qui declare les
// deux champs : celui qu'on oublierait d'appeler a la main serait
// precisement celui qu'on exploiterait.
builder.Services.AddScoped<lpdeBack.Validation.AntiRobotFilter>();
builder.Services.Configure<MvcOptions>(o =>
    o.Filters.Add<lpdeBack.Validation.AntiRobotFilter>());

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// L'intercepteur des requetes lentes : en singleton, il ne porte qu'un
// seuil et un journal, et le contexte est cree a chaque requete.
builder.Services.AddSingleton<lpdeBack.Services.RequetesLentes>();

builder.Services.AddDbContext<AppDbContext>((provider, options) =>
    options
        .UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
        // Ce qui depasse le seuil part au journal, avec sa duree. Sans
        // cela, une requete qui balaie cent mille offres faute d'index
        // ne se distingue en rien d'une page lente a cause du reseau.
        .AddInterceptors(provider.GetRequiredService<lpdeBack.Services.RequetesLentes>()));

builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    // ── Mot de passe ──
    // Les classes de caracteres obligatoires produisent « Password1! » : le
    // meme mot de passe que tout le monde, avec les memes substitutions.
    // C'est la longueur qui resiste, et le NIST a cesse depuis 2017 de
    // recommander l'inverse. On exige donc huit caracteres et quatre
    // distincts — de quoi ecarter « aaaaaaaa » — et rien d'autre.
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8;
    options.Password.RequiredUniqueChars = 4;

    options.User.RequireUniqueEmail = true;

    // ── Verrouillage ──
    // CheckPasswordSignInAsync etait appele avec lockoutOnFailure: false :
    // un robot pouvait essayer des mots de passe indefiniment, sans que rien
    // ne ralentisse ni ne compte. Cinq essais donnent le droit a l'erreur,
    // quinze minutes rendent l'attaque par force brute sans objet.
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.AllowedForNewUsers = true;

    // La confirmation d'adresse n'interdit pas la connexion : elle
    // conditionne les envois et la recuperation du mot de passe. Bloquer
    // l'entree enfermerait dehors tous les comptes crees avant qu'un
    // serveur de courriel n'existe.
    options.SignIn.RequireConfirmedEmail = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// Le jeton de reinitialisation vaut par defaut un jour entier. Un lien qui
// ouvre un compte n'a pas a survivre une nuit : trente minutes suffisent a
// relever sa boite et a cliquer.
builder.Services.Configure<DataProtectionTokenProviderOptions>(o =>
    o.TokenLifespan = TimeSpan.FromMinutes(30));

// ── Cle de signature JWT ──
// Aucun repli : une valeur par defaut ici restaurerait silencieusement une cle
// connue le jour ou la configuration viendrait a manquer, et l'API se remettrait
// a signer des jetons que n'importe qui peut forger. Mieux vaut ne pas demarrer.
var jwtKey = builder.Configuration["Jwt:Key"];

if (string.IsNullOrWhiteSpace(jwtKey))
    throw new InvalidOperationException(
        "Jwt:Key absente. Renseignez-la via les secrets de deploiement, " +
        "ou en local avec « dotnet user-secrets set \"Jwt:Key\" \"<votre cle>\" ».");

// HMAC-SHA256 exige 256 bits : en deca, la signature echoue a la premiere
// connexion avec un message autrement plus obscur que celui-ci.
if (jwtKey.Length < 32)
    throw new InvalidOperationException(
        $"Jwt:Key trop courte ({jwtKey.Length} caracteres). HMAC-SHA256 en exige au moins 32.");

// Cette valeur a ete publiee sur un depot public : elle ne signe plus rien.
if (jwtKey == "LpdeSecretKey2026SuperSecure!@#$%^&*()_+")
    throw new InvalidOperationException(
        "Jwt:Key est la cle compromise du depot. Generez-en une nouvelle : " +
        "elle est lisible par quiconque a lu le code, et permet de forger un jeton Admin.");
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "LpdeBack",
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "LpdeFront",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },

        // ── Le jeton signe ne suffit plus ──
        // Une signature valide prouve que nous avons emis ce jeton ; elle ne
        // dit pas qu'il vaut encore. Suspendre un compte, changer un mot de
        // passe, couper une session ou desactiver la double authentification
        // ne fermaient donc rien avant l'expiration, sept jours plus tard.
        //
        // Deux verifications le rendent revocable : la session que ce jeton
        // designe existe-t-elle encore, et le tampon de securite du compte
        // est-il toujours celui qu'il porte ? Identity change ce tampon de
        // lui-meme des que le mot de passe ou la double authentification
        // bougent — un seul geste tue alors tous les jetons a la fois.
        OnTokenValidated = async context =>
        {
            var principal = context.Principal;
            if (principal == null) { context.Fail("Jeton illisible."); return; }

            // Un jeton de defi franchit l'etape de double authentification et
            // rien d'autre : il n'ouvre aucune page.
            if (principal.FindFirst(lpdeBack.Services.SessionService.ClaimDefi) != null)
            {
                context.Fail("Ce jeton ne vaut que pour la double authentification.");
                return;
            }

            var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(jti) || string.IsNullOrEmpty(userId))
            {
                context.Fail("Jeton incomplet.");
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Jti == jti);

            // Les jetons emis avant l'existence des sessions n'en ont pas.
            // Les refuser deconnecterait tout le monde d'un coup au
            // deploiement ; on les laisse vivre jusqu'a leur expiration
            // naturelle, mais le tampon leur est quand meme oppose.
            if (session != null)
            {
                if (session.RevokedAt != null)
                {
                    context.Fail("Session fermee.");
                    return;
                }

                // Ecrire a chaque requete couterait plus cher que toute la
                // verification : on ne rafraichit que par tranches.
                if (DateTime.UtcNow - session.LastSeenAt > TimeSpan.FromMinutes(5))
                {
                    session.LastSeenAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }
            }

            var tamponJeton = principal.FindFirst(lpdeBack.Services.SessionService.ClaimTampon)?.Value;
            if (tamponJeton != null)
            {
                var compte = await db.Users
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.SecurityStamp, u.IsActive })
                    .FirstOrDefaultAsync();

                if (compte == null || !compte.IsActive)
                {
                    context.Fail("Compte indisponible.");
                    return;
                }
                if (compte.SecurityStamp != tamponJeton)
                {
                    context.Fail("Jeton perime : les identifiants du compte ont change.");
                    return;
                }
            }
        },
    };
});

builder.Services.AddAuthorization();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddSignalR();

// ══════════════════════════════════════════════
//  Compression des reponses
//
//  Une page de resultats est un tableau JSON d'une centaine d'offres,
//  chacune avec sa description : deux a trois cents kilo-octets, envoyes
//  tels quels a chaque recherche. Le meme corps compresse en Brotli
//  tient dans une vingtaine — le JSON est repetitif par nature, les
//  memes noms de proprietes revenant a chaque element.
//
//  Sur une liaison mobile, c'est la difference entre une seconde et
//  huit. Le cout serveur est negligeable devant le temps de la requete
//  SQL qui l'a produit.
//
//  HTTPS uniquement — active par defaut, et on le laisse ainsi : la
//  compression sur un canal chiffre a ete la source d'attaques
//  (BREACH), et rien ici ne justifie de prendre ce risque puisque tout
//  passe en HTTPS de toute facon.
// ══════════════════════════════════════════════
builder.Services.AddResponseCompression(options =>
{
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults
        .MimeTypes.Concat(new[] { "application/json", "application/xml", "text/xml" });
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(
    o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(
    o => o.Level = System.IO.Compression.CompressionLevel.Fastest);

// ══════════════════════════════════════════════
//  Cache de sortie
//
//  Les listes publiques repartaient en base a chaque requete, alors
//  qu'elles sont identiques pour tout le monde et qu'elles changent au
//  rythme des imports — quelques fois par jour. Aux heures de pointe,
//  la meme page de resultats etait recalculee des centaines de fois par
//  minute.
//
//  Trois durees, calees sur ce que chaque chose vaut :
//
//    « catalogue » (60 s) : les listes d'offres. Court, parce qu'une
//    offre publiee par un recruteur doit apparaitre tout de suite —
//    une minute d'attente est acceptable, dix ne le seraient pas.
//
//    « reference » (10 min) : salaires, facettes de parcours, annuaire
//    d'entreprises. Ces agregats bougent a l'echelle de la journee.
//
//    « plan-de-site » (1 h) : le plan est lourd a produire (cent mille
//    URL) et n'est lu que par des robots, qui ne s'offusquent pas d'une
//    heure de retard.
//
//  Le cache ne s'applique qu'aux requetes anonymes : la politique par
//  defaut d'ASP.NET ignore deja tout ce qui porte un en-tete
//  d'autorisation ou un cookie, ce qui evite le pire des accidents —
//  servir la page d'un membre a un autre.
// ══════════════════════════════════════════════
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("catalogue", p => p
        .Expire(TimeSpan.FromSeconds(60))
        .SetVaryByQuery("*"));

    options.AddPolicy("reference", p => p
        .Expire(TimeSpan.FromMinutes(10))
        .SetVaryByQuery("*"));

    options.AddPolicy("plan-de-site", p => p
        .Expire(TimeSpan.FromHours(1))
        .SetVaryByQuery("*"));

    // Sans plafond, un catalogue de cent mille offres finirait par tenir
    // en memoire deux fois : celle de la base et celle du cache.
    options.MaximumBodySize = 8 * 1024 * 1024;
    options.SizeLimit = 128 * 1024 * 1024;
});
builder.Services.AddScoped<lpdeBack.Services.PushNotificationService>();
builder.Services.AddScoped<lpdeBack.Services.ActivityLogService>();
builder.Services.AddScoped<lpdeBack.Services.SessionService>();
// Sans serveur configure, l'expediteur ecrit les messages au journal plutot
// que de les perdre : on suit un flux complet en developpement, lien compris.
builder.Services.AddSingleton<lpdeBack.Services.IEmailSender, lpdeBack.Services.EmailSender>();
// En singleton : le client SMS garde l'ecart avec l'horloge d'OVH et le nom
// du compte decouvert. Les remesurer a chaque requete ferait deux appels
// reseau de plus pour chaque code envoye.
builder.Services.AddSingleton<lpdeBack.Services.OvhSmsService>();
builder.Services.AddScoped<lpdeBack.Services.DeuxFacteursSms>();
// La lettre d'information passe par Brevo et non par le SMTP transactionnel :
// expedier des milliers de messages depuis la boite qui porte aussi les mots
// de passe oublies ruinerait la reputation de celle-ci, et les mots de passe
// oublies cesseraient d'arriver avec le reste.
builder.Services.AddSingleton<lpdeBack.Services.BrevoService>();
builder.Services.AddScoped<lpdeBack.Services.NewsletterService>();
// Ce qu'on a le droit d'envoyer, et a qui : preferences par categorie
// et adresses qui ne repondent plus.
builder.Services.AddScoped<lpdeBack.Services.ConsentementCourriel>();
// Formules, quotas, mises en avant, factures.
builder.Services.AddScoped<lpdeBack.Services.FacturationService>();
// L'encaissement, derriere une interface : changer de prestataire ne
// doit toucher ni les factures, ni les quotas, ni les controleurs.
builder.Services.AddScoped<lpdeBack.Services.PrestatairePaiement>();
// Dedoublonnage inter-sources, expiration et analyse de fraude des
// offres importees.
builder.Services.AddScoped<lpdeBack.Services.QualiteCatalogue>();
// Notification des systemes tiers abonnes a nos evenements.
builder.Services.AddScoped<lpdeBack.Services.WebhookService>();
// Depot d'une offre chez les partenaires, et surtout son retrait :
// une offre pourvue qui reste en ligne ailleurs continue de recevoir
// des candidatures que personne ne lira.
builder.Services.AddScoped<lpdeBack.Services.Multidiffusion>();
// Signale les offres nouvelles aux moteurs qui l'acceptent, sans
// attendre qu'ils relisent le plan de site.
builder.Services.AddScoped<lpdeBack.Services.IndexNow>();
// Qui peut gerer quoi, cote recruteur. En portee de requete : il
// memoise l'equipe le temps d'un appel, pas au-dela.
builder.Services.AddScoped<lpdeBack.Services.PerimetreRecruteur>();

// Le rangement des fichiers deposes, hors de wwwroot. Singleton : il ne
// porte qu'un chemin, et le demarrage s'en sert avant toute requete.
builder.Services.AddSingleton<lpdeBack.Services.DepotFichiers>();

// Le filet : plus aucune exception ne sort en page brute.
builder.Services.AddExceptionHandler<lpdeBack.Middleware.FiletErreur>();
builder.Services.AddProblemDetails();

builder.Services.AddHostedService<lpdeBack.Services.NewsletterSenderService>();

// La redaction hebdomadaire de la lettre. Elle ne fait que deposer des
// brouillons : c'est l'expediteur ci-dessus qui envoie, et lui seul, sur
// un clic humain.
builder.Services.AddHostedService<lpdeBack.Services.RedactionNewsletterService>();

// Tient les durees de conservation annoncees dans les mentions legales.
// A blanc tant que « purge_active » vaut false.
builder.Services.AddHostedService<lpdeBack.Services.PurgeService>();
builder.Services.AddScoped<lpdeBack.Services.JobImportService>();
// En singleton : le cache de jetons France Travail n'a d'interet que s'il
// survit a la requete qui l'a rempli.
builder.Services.AddSingleton<lpdeBack.Services.FranceTravailService>();
builder.Services.AddHostedService<lpdeBack.Services.JobImportBackgroundService>();

// ── Modele de langage (analyse de CV, redaction assistee) ──
// Contrat « chat/completions » d'OpenAI, servi aussi bien par OpenAI que par
// un Ollama, un llama.cpp ou un LM Studio installes sur le reseau local.
builder.Services.Configure<lpdeBack.Services.AiOptions>(builder.Configuration.GetSection("Ai"));
builder.Services.AddScoped<lpdeBack.Services.AiClient>();

// La couche qui decide QUAND parler au modele, et surtout quand s'en
// passer : cache, plafond journalier, repli silencieux sur les regles.
// Tout ce qui la surplombe — correspondance, lecture de recherche,
// moderation — fonctionne sans elle.
builder.Services.AddScoped<lpdeBack.Services.AssistantIa>();
builder.Services
    .AddHttpClient(lpdeBack.Services.AiClient.HttpClientName, (provider, client) =>
    {
        var options = provider.GetRequiredService<IOptions<lpdeBack.Services.AiOptions>>().Value;
        // Un modele local met des dizaines de secondes a analyser un CV entier :
        // le delai par defaut de 100 s couperait la reponse en plein milieu.
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    })
    .ConfigurePrimaryHttpMessageHandler(provider =>
    {
        var options = provider.GetRequiredService<IOptions<lpdeBack.Services.AiOptions>>().Value;
        var handler = new HttpClientHandler();
        if (options.AcceptInvalidCertificate)
        {
            // Reserve a un serveur de confiance du reseau local dont le
            // certificat est auto-signe. Ne concerne que ce client HTTP.
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        return handler;
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins(
                  "http://localhost:4200",
                  "http://localhost",
                  "http://localhost:5013",
                  "https://localhost",
                  "capacitor://localhost",
                  "https://www.laplateformedelemploi.com",
                  "https://laplateformedelemploi.com"
              )
              .AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders("X-Total-Count")
              .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // En production, le filet : reponse propre au visiteur, trace
    // complete au journal, et une reference qui relie les deux.
    app.UseExceptionHandler();

    // ── HTTPS, et rien d'autre ──
    //
    // L'API repondait en clair a qui l'appelait en clair. Un jeton porte
    // par un en-tete Authorization traversait alors le reseau lisible,
    // et le proxy d'un hotel ou d'un aeroport n'a pas besoin de plus.
    //
    // La redirection seule ne suffit pas : elle protege la deuxieme
    // requete, jamais la premiere. HSTS regle cela en disant au
    // navigateur de ne plus jamais essayer en clair — un an, sous-
    // domaines compris.
    //
    // Consequence a connaitre avant de la garder : tant que l'en-tete
    // n'a pas expire, le domaine est inaccessible en HTTP, y compris si
    // le certificat vient a manquer. C'est le prix, et il est assume.
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Avant tout ce qui produit un corps, sinon il n'y a plus rien a
// compresser au moment ou l'on s'en occupe.
app.UseResponseCompression();

// Une ligne par requete, avec sa duree et son code : c'est ce qui
// permet de dire « la lenteur vient de /joboffers, pas du reseau ».
// Serilog n'en fait qu'une, la ou le journal par defaut en produit
// trois pour la meme requete.
app.UseSerilogRequestLogging(options =>
{
    // Les sondes de surveillance frappent /api/sante toutes les
    // minutes : au niveau Information, elles representeraient a elles
    // seules la moitie du journal.
    options.GetLevel = (contexte, duree, ex) =>
        ex is not null || contexte.Response.StatusCode >= 500
            ? Serilog.Events.LogEventLevel.Error
            : contexte.Request.Path.StartsWithSegments("/api/sante")
                ? Serilog.Events.LogEventLevel.Verbose
                : Serilog.Events.LogEventLevel.Information;
});

// Les fichiers deposes ont quitte wwwroot ; ce rapatriement s'assure
// qu'aucun n'y reste, y compris ceux televerses avant le correctif.
//
// Enrobe : aucune etape de confort ne doit empecher l'application de
// demarrer. Un site qui ne se leve pas est toujours pire que le
// probleme qu'on essayait de resoudre — cette ligne a deja coute une
// interruption de service.
try
{
    using var portee = app.Services.CreateScope();
    portee.ServiceProvider.GetRequiredService<lpdeBack.Services.DepotFichiers>()
          .Ranger(app.Environment);
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Preparation du depot de fichiers impossible — l'application demarre sans");
}

// Ceinture et bretelles : meme si un fichier reapparaissait sous
// « /uploads », il ne serait pas servi. La fuite ne se rouvrira pas par
// distraction.
app.Use(async (ctx, suite) =>
{
    if (ctx.Request.Path.StartsWithSegments("/uploads"))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await suite();
});

app.UseStaticFiles();
app.UseCors("AllowAngular");
app.UseMiddleware<lpdeBack.Middleware.MaintenanceMiddleware>();
// Avant l'authentification : un flot de requetes doit etre arrete
// avant qu'on ne depense a verifier chacune de leurs signatures.
app.UseRateLimiter();

// Apres CORS (la reponse mise en cache doit porter ses en-tetes
// d'origine) et avant l'authentification : une lecture publique deja
// calculee n'a pas besoin qu'on verifie une signature pour etre rendue.
app.UseOutputCache();

app.UseAuthentication();
app.UseAuthorization();
app.MapHub<lpdeBack.Hubs.ChatHub>("/hubs/chat");
app.MapControllers();

// ═══════════════════════════════════
//  SEED: Database + Roles + Users + Offers + Applications
// ═══════════════════════════════════
//
// Saute sous l'environnement « Test ». Les tests d'integration montent
// le vrai pipeline — authentification, autorisation par role, filtres —
// sur une base SQLite qu'ils creent et peuplent eux-memes. Ce bloc-ci
// applique des migrations SQL Server et du SQL brut ; le laisser
// tourner ferait echouer le demarrage avant le premier test, et pour
// une raison qui n'aurait rien a voir avec ce qu'on veut eprouver.
if (!app.Environment.IsEnvironment("Test"))
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<AppDbContext>();
    var userManager = services.GetRequiredService<UserManager<AppUser>>();
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

    db.Database.Migrate();

    // ── Fix existing offers: set ModerationStatus to Approved if empty ──
    await db.Database.ExecuteSqlRawAsync(
        "UPDATE JobOffers SET ModerationStatus = 'Approved' WHERE ModerationStatus IS NULL OR ModerationStatus = ''");

    // ── Backfill: geocode existing offers missing coordinates (recherche par rayon) ──
    var toGeocode = await db.JobOffers
        .Where(j => j.Latitude == null && j.Location != null && j.Location != "")
        .ToListAsync();
    if (toGeocode.Count > 0)
    {
        foreach (var j in toGeocode)
        {
            var geo = lpdeBack.Services.GeoUtils.Geocode(j.Location);
            if (geo != null) { j.Latitude = geo.Value.Lat; j.Longitude = geo.Value.Lng; }
        }
        await db.SaveChangesAsync();
    }

    // ── Backfill: URL source des offres importées (pour postuler sur le site d'origine) ──
    var noUrl = await db.JobOffers
        .Where(j => j.ExternalSource != null && j.ExternalUrl == null && j.ExternalId != null)
        .ToListAsync();
    if (noUrl.Count > 0)
    {
        foreach (var j in noUrl)
        {
            var key = j.ExternalId!.Contains(':') ? j.ExternalId!.Split(':', 2)[1] : j.ExternalId!;
            j.ExternalUrl = j.ExternalSource switch
            {
                "francetravail" => $"https://candidat.francetravail.fr/offres/recherche/detail/{key}",
                "arbeitnow" => $"https://www.arbeitnow.com/view/{key}",
                _ => "https://remotive.com/remote-jobs",
            };
        }
        await db.SaveChangesAsync();
    }

    // ── Platform Settings (seed defaults) ──
    if (!db.PlatformSettings.Any())
    {
        db.PlatformSettings.AddRange(
            new PlatformSetting { Key = "maintenance_mode", Value = "false", Type = "bool", Description = "Active le mode maintenance" },
            new PlatformSetting { Key = "default_offer_duration", Value = "30", Type = "int", Description = "Durée par défaut des offres (jours)" },
            new PlatformSetting { Key = "max_applications_per_candidate", Value = "20", Type = "int", Description = "Nombre max de candidatures par candidat" },
            new PlatformSetting { Key = "require_moderation", Value = "false", Type = "bool", Description = "Modération obligatoire avant publication" },
            new PlatformSetting { Key = "welcome_message", Value = "Bienvenue sur La Plateforme de l'Emploi !", Type = "string", Description = "Message d'accueil" },
            new PlatformSetting { Key = "allow_registration", Value = "true", Type = "bool", Description = "Autoriser les nouvelles inscriptions" },
            new PlatformSetting { Key = "contact_email", Value = "contact@laplateformedelemploi.com", Type = "string", Description = "Email de contact" }
        );
        await db.SaveChangesAsync();
    }

    // ── Mentions legales ──
    //
    // Elles vivent dans les parametres et non dans le code : ce sont des
    // informations d'exploitation — raison sociale, SIRET, hebergeur — qui
    // changent sans qu'on redeploie, et que l'exploitant doit pouvoir
    // saisir lui-meme depuis la console.
    //
    // L'ajout se fait cle par cle et non dans le bloc ci-dessus : celui-ci
    // ne s'execute que sur une base vierge, une nouvelle cle n'y serait
    // jamais creee sur une base existante.
    var mentions = new (string Cle, string Valeur, string Description)[]
    {
        ("legal_raison_sociale", "", "Mentions legales — raison sociale, forme juridique, capital"),
        ("legal_adresse", "", "Mentions legales — adresse du siege social"),
        ("legal_siret", "", "Mentions legales — numero SIRET"),
        ("legal_tva", "", "Mentions legales — numero de TVA intracommunautaire"),
        ("legal_telephone", "", "Mentions legales — telephone"),
        ("legal_directeur_publication", "", "Mentions legales — directeur de la publication"),
        // Verifie au registre RIPE : l'adresse du site pointe sur un reseau
        // OVH SAS situe en France. Prerempli, a confirmer par l'exploitant.
        ("legal_hebergeur", "OVHcloud (OVH SAS) — 2 rue Kellermann, 59100 Roubaix, France — 1007", "Mentions legales — hebergeur"),
        ("legal_dpo", "", "Confidentialite — delegue a la protection des donnees, si designe"),
        // Recommandation de la CNIL pour les donnees de recrutement : deux
        // ans apres le dernier contact avec le candidat. Valeur proposee,
        // a valider par l'exploitant.
        ("legal_conservation_compte", "2 ans apres la derniere connexion", "Confidentialite — conservation d'un compte inactif"),
        ("legal_conservation_candidatures", "2 ans apres le dernier contact, conformement a la recommandation de la CNIL", "Confidentialite — conservation des candidatures"),
        ("legal_conservation_journal", "12 mois", "Confidentialite — conservation du journal d'administration"),

        // Les durees ci-dessus sont annoncees en toutes lettres aux
        // visiteurs ; celles-ci sont ce que la machine applique. Elles
        // doivent dire la meme chose : une mention legale qu'aucun code
        // ne tient est une phrase, pas un engagement.
        //
        // « purge_active » reste a false : mis en service, le nettoyage
        // effacerait des comptes des la premiere nuit. Tant qu'il vaut
        // false, il compte et journalise ce qu'il ferait sans y toucher.
        // On lit les chiffres dans le journal, puis on l'autorise.
        ("purge_active", "false", "Conservation — appliquer réellement la purge (sinon, elle tourne à blanc)"),
        ("purge_compte_mois", "24", "Conservation — fermeture d'un compte après N mois sans connexion"),
        ("purge_preavis_jours", "60", "Conservation — préavis envoyé N jours avant la fermeture"),
        ("purge_candidatures_mois", "24", "Conservation — effacement des candidatures après N mois"),
        ("purge_journal_mois", "12", "Conservation — effacement du journal d'administration après N mois"),
    };

    var clesExistantes = db.PlatformSettings.Select(s => s.Key).ToHashSet();
    var manquantes = mentions
        .Where(m => !clesExistantes.Contains(m.Cle))
        .Select(m => new PlatformSetting { Key = m.Cle, Value = m.Valeur, Type = "string", Description = m.Description })
        .ToList();
    if (manquantes.Count > 0)
    {
        db.PlatformSettings.AddRange(manquantes);
        await db.SaveChangesAsync();
    }

    // ── Roles ──
    foreach (var role in new[] { "Admin", "Recruiter", "Candidate" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    // ── Jeu de demonstration ──
    // Comptes, offres et candidatures fictifs : cela n'a de sens qu'en
    // developpement. Ce bloc s'executait jusqu'ici quel que soit
    // l'environnement, et sa branche « else » recreait en production un compte
    // Admin dont le mot de passe figure dans ce fichier, sur un depot public.
    //
    // Migrations, rattrapages, reglages de plateforme et roles restent au-dessus :
    // eux doivent bien s'executer partout.
    var seedDemoData = app.Environment.IsDevelopment();

    // Configurable pour qu'aucun mot de passe, meme de demonstration, ne soit
    // fige ici. La valeur de repli ne sert que sur un poste de developpement.
    var demoAdminPassword = app.Configuration["Seed:AdminPassword"] ?? "Admin123!";

    if (seedDemoData && !db.JobOffers.Any())
    {
        // ── Users ──
        async Task<AppUser> CreateUser(AppUser user, string password)
        {
            user.UserName = user.Email;
            user.EmailConfirmed = true;
            await userManager.CreateAsync(user, password);
            await userManager.AddToRoleAsync(user, user.Role);
            return user;
        }

        // Admin
        var admin = await CreateUser(new AppUser { Email = "admin@lpde.fr", FirstName = "Admin", LastName = "LPDE", Role = "Admin", Bio = "Administrateur de la plateforme." }, demoAdminPassword);

        // Recruiters
        var sophie = await CreateUser(new AppUser { Email = "sophie.martin@techcorp.fr", FirstName = "Sophie", LastName = "Martin", Role = "Recruiter", Company = "TechCorp", PhoneNumber = "06 11 22 33 44", City = "Paris", Title = "Responsable recrutement", Bio = "Responsable recrutement chez TechCorp, specialisee dans les profils tech et innovation." }, "Recruiter123!");
        var lucas = await CreateUser(new AppUser { Email = "lucas.bernard@creativestudio.fr", FirstName = "Lucas", LastName = "Bernard", Role = "Recruiter", Company = "CreativeStudio", PhoneNumber = "06 22 33 44 55", City = "Lyon", Title = "Directeur des Ressources Humaines", Bio = "Directeur RH chez CreativeStudio, studio de design base a Lyon." }, "Recruiter123!");
        var emma = await CreateUser(new AppUser { Email = "emma.dubois@cloudnine.fr", FirstName = "Emma", LastName = "Dubois", Role = "Recruiter", Company = "CloudNine", PhoneNumber = "06 33 44 55 66", City = "Paris", Title = "Talent Acquisition Manager", Bio = "Talent Acquisition Manager chez CloudNine, expert cloud et DevOps." }, "Recruiter123!");
        var thomas = await CreateUser(new AppUser { Email = "thomas.petit@startupflow.fr", FirstName = "Thomas", LastName = "Petit", Role = "Recruiter", Company = "StartupFlow", PhoneNumber = "06 44 55 66 77", City = "Bordeaux", Title = "Co-fondateur & CEO", Bio = "Co-fondateur de StartupFlow, startup en forte croissance a Bordeaux." }, "Recruiter123!");
        var marie = await CreateUser(new AppUser { Email = "marie.leroy@financeplus.fr", FirstName = "Marie", LastName = "Leroy", Role = "Recruiter", Company = "FinancePlus", PhoneNumber = "06 55 66 77 88", City = "Paris", Title = "DRH", Bio = "DRH chez FinancePlus, cabinet comptable parisien." }, "Recruiter123!");

        // Candidates
        var jean = await CreateUser(new AppUser { Email = "jean.dupont@email.fr", FirstName = "Jean", LastName = "Dupont", Role = "Candidate", PhoneNumber = "06 10 20 30 40", City = "Paris", Title = "Developpeur Full Stack", Bio = "Developpeur full stack avec 3 ans d'experience en Angular et .NET Core. Passionne par le cloud et les architectures modernes.", Skills = "Angular,TypeScript,.NET Core,C#,SQL Server,Docker,Git,Azure", ExperienceYears = 3, Education = "Master Informatique - Universite Paris-Saclay", LinkedInUrl = "https://linkedin.com/in/jean-dupont" }, "Candidat123!");
        var alice = await CreateUser(new AppUser { Email = "alice.moreau@email.fr", FirstName = "Alice", LastName = "Moreau", Role = "Candidate", PhoneNumber = "06 20 30 40 50", City = "Lyon", Title = "Designer UX/UI Senior", Bio = "Designer UX/UI senior avec 5 ans d'experience. Experte Figma, design systems et tests utilisateurs. Passionnee par l'accessibilite.", Skills = "Figma,Adobe XD,Design System,Prototypage,Tests utilisateurs,HTML,CSS", ExperienceYears = 5, Education = "Licence Design Numerique - Ecole de Conde", PortfolioUrl = "https://alice-moreau.design" }, "Candidat123!");
        var karim = await CreateUser(new AppUser { Email = "karim.benali@email.fr", FirstName = "Karim", LastName = "Benali", Role = "Candidate", PhoneNumber = "06 30 40 50 60", City = "Toulouse", Title = "Data Analyst Junior", Bio = "Data analyst junior, diplome en statistiques. Competences en Python, SQL et Power BI. Passionne par la data visualisation.", Skills = "Python,SQL,Power BI,Excel,Pandas,Matplotlib,Statistiques", ExperienceYears = 1, Education = "Master Statistiques - Universite Toulouse III" }, "Candidat123!");
        var camille = await CreateUser(new AppUser { Email = "camille.roux@email.fr", FirstName = "Camille", LastName = "Roux", Role = "Candidate", PhoneNumber = "06 40 50 60 70", City = "Marseille", Title = "Chef de Projet Digital", Bio = "Chef de projet digital certifiee Scrum Master avec 4 ans d'experience en agence. Experte en methodologies agiles.", Skills = "Scrum,Agile,Jira,Confluence,Gestion de projet,SEO,Google Analytics", ExperienceYears = 4, Education = "Master Marketing Digital - IAE Aix-Marseille", LinkedInUrl = "https://linkedin.com/in/camille-roux" }, "Candidat123!");
        var hugo = await CreateUser(new AppUser { Email = "hugo.lambert@email.fr", FirstName = "Hugo", LastName = "Lambert", Role = "Candidate", PhoneNumber = "06 50 60 70 80", City = "Lille", Title = "Etudiant en informatique", Bio = "Etudiant en 5eme annee d'informatique a la recherche d'un stage de fin d'etudes en developpement backend Java/Spring.", Skills = "Java,Spring Boot,PostgreSQL,Docker,Git,Linux", ExperienceYears = 0, Education = "5eme annee Ingenieur Informatique - Polytech Lille" }, "Candidat123!");

        // ── Job Offers (linked to recruiters, enriched) ──
        var offers = new List<JobOffer>
        {
            // Sophie Martin @ TechCorp
            new() { Title = "Developpeur Full Stack Angular / .NET", Company = "TechCorp", Location = "Paris", Description = "Nous recherchons un developpeur full stack passionne pour rejoindre notre equipe innovation. Vous travaillerez sur des projets ambitieux en Angular et .NET Core, dans un environnement agile avec CI/CD.", ContractType = "CDI", Salary = "45K - 55K EUR", Category = "Tech", IsRemote = true, CreatedAt = new DateTime(2026, 4, 1), Tags = "Angular,.NET,C#,TypeScript", CreatedByUserId = sophie.Id, MinSalary = 45000, MaxSalary = 55000, ExperienceRequired = "Intermediaire", EducationLevel = "Bac+5", Benefits = "Teletravail 3j/sem,Tickets restaurant,RTT,Mutuelle,Prime annuelle", CompanyDescription = "TechCorp est une entreprise innovante specialisee dans les solutions digitales pour les grands comptes.", IsUrgent = true },
            new() { Title = "Developpeur Mobile React Native", Company = "TechCorp", Location = "Paris", Description = "Developpez des applications mobiles cross-platform pour nos clients grands comptes. Publication sur les stores et integration d'APIs REST.", ContractType = "CDI", Salary = "42K - 52K EUR", Category = "Tech", IsRemote = true, CreatedAt = new DateTime(2026, 4, 6), Tags = "React Native,Mobile,iOS,Android", CreatedByUserId = sophie.Id, MinSalary = 42000, MaxSalary = 52000, ExperienceRequired = "Junior", EducationLevel = "Bac+3", Benefits = "Teletravail,Tickets restaurant,Formation continue" },

            // Lucas Bernard @ CreativeStudio
            new() { Title = "Designer UX/UI Senior", Company = "CreativeStudio", Location = "Lyon", Description = "Rejoignez notre studio creatif pour concevoir des interfaces utilisateur innovantes. Vous piloterez le design system et menerez les tests utilisateurs.", ContractType = "CDI", Salary = "40K - 50K EUR", Category = "Design", IsRemote = false, CreatedAt = new DateTime(2026, 4, 2), Tags = "Figma,UX,UI,Design System", CreatedByUserId = lucas.Id, MinSalary = 40000, MaxSalary = 50000, ExperienceRequired = "Senior", EducationLevel = "Bac+3", Benefits = "MacBook Pro,Budget formation,Afterworks,Locaux design", CompanyDescription = "CreativeStudio est un studio de design numerique base a Lyon, specialise en UX et branding." },
            new() { Title = "Motion Designer Junior", Company = "CreativeStudio", Location = "Lyon", Description = "Creez des animations et micro-interactions pour nos projets web et mobile. After Effects et Lottie requis.", ContractType = "CDD", Salary = "28K - 34K EUR", Category = "Design", IsRemote = false, CreatedAt = new DateTime(2026, 4, 8), Tags = "After Effects,Lottie,Animation,Motion", CreatedByUserId = lucas.Id, MinSalary = 28000, MaxSalary = 34000, ExperienceRequired = "Junior", EducationLevel = "Bac+2" },

            // Emma Dubois @ CloudNine
            new() { Title = "Ingenieur DevOps Cloud", Company = "CloudNine", Location = "Paris", Description = "Automatisez et optimisez nos pipelines CI/CD et notre infrastructure cloud AWS. Terraform et Kubernetes requis. Equipe de 8 DevOps.", ContractType = "CDI", Salary = "50K - 65K EUR", Category = "Tech", IsRemote = true, CreatedAt = new DateTime(2026, 4, 9), Tags = "AWS,Terraform,Kubernetes,Docker", CreatedByUserId = emma.Id, MinSalary = 50000, MaxSalary = 65000, ExperienceRequired = "Senior", EducationLevel = "Bac+5", Benefits = "Full remote possible,Stock options,Budget materiel,Conferences", CompanyDescription = "CloudNine est un pure player cloud qui accompagne les entreprises dans leur transformation numerique.", IsUrgent = true },
            new() { Title = "Stagiaire Developpeur Backend Java", Company = "CloudNine", Location = "Paris", Description = "Stage de 6 mois au sein de notre equipe backend. Decouvrez Spring Boot, microservices et architecture cloud dans un contexte production.", ContractType = "Stage", Salary = "1000 - 1200 EUR/mois", Category = "Tech", IsRemote = false, CreatedAt = new DateTime(2026, 4, 3), Tags = "Java,Spring Boot,Microservices", CreatedByUserId = emma.Id, ExperienceRequired = "Junior", EducationLevel = "Bac+4", Benefits = "Tickets restaurant,Transport rembourse 50%" },

            // Thomas Petit @ StartupFlow
            new() { Title = "Responsable Marketing Digital", Company = "StartupFlow", Location = "Bordeaux", Description = "Definissez et executez la strategie marketing digitale de notre startup en forte croissance. SEO, SEA, social media et growth hacking.", ContractType = "CDI", Salary = "42K - 52K EUR", Category = "Marketing", IsRemote = true, CreatedAt = new DateTime(2026, 4, 5), Tags = "SEO,SEA,Social Media,Growth", CreatedByUserId = thomas.Id, MinSalary = 42000, MaxSalary = 52000, ExperienceRequired = "Intermediaire", EducationLevel = "Bac+5", Benefits = "Teletravail,BSPCE,Ambiance startup,Baby-foot", CompanyDescription = "StartupFlow est une startup tech en hypercroissance qui revolutionne la gestion de projet." },
            new() { Title = "Chef de Projet Digital", Company = "StartupFlow", Location = "Bordeaux", Description = "Pilotez des projets web et mobile de A a Z. Methodologie Agile, gestion de backlog et coordination technique.", ContractType = "CDI", Salary = "38K - 48K EUR", Category = "Marketing", IsRemote = false, CreatedAt = new DateTime(2026, 4, 4), Tags = "Agile,Scrum,Gestion de projet", CreatedByUserId = thomas.Id, MinSalary = 38000, MaxSalary = 48000, ExperienceRequired = "Intermediaire", EducationLevel = "Bac+5", Benefits = "Tickets restaurant,RTT,Mutuelle" },
            new() { Title = "Data Analyst Junior", Company = "StartupFlow", Location = "Bordeaux", Description = "Analysez les donnees utilisateurs et creez des dashboards. Python, SQL et Power BI indispensables.", ContractType = "CDD", Salary = "30K - 35K EUR", Category = "Data", IsRemote = true, CreatedAt = new DateTime(2026, 4, 7), Tags = "Python,SQL,Power BI,Data", CreatedByUserId = thomas.Id, MinSalary = 30000, MaxSalary = 35000, ExperienceRequired = "Junior", EducationLevel = "Bac+5" },

            // Marie Leroy @ FinancePlus
            new() { Title = "Comptable Senior", Company = "FinancePlus", Location = "Paris", Description = "Gerez la comptabilite generale et analytique de notre groupe. Maitrise des normes IFRS et d'un ERP (SAP ou Sage).", ContractType = "CDI", Salary = "45K - 55K EUR", Category = "Finance", IsRemote = false, CreatedAt = new DateTime(2026, 4, 10), Tags = "Comptabilite,IFRS,SAP,Sage", CreatedByUserId = marie.Id, MinSalary = 45000, MaxSalary = 55000, ExperienceRequired = "Senior", EducationLevel = "Bac+5", Benefits = "13eme mois,Mutuelle famille,CE,Parking", CompanyDescription = "FinancePlus est un cabinet d'expertise comptable de reference a Paris." },
            new() { Title = "Alternant Ressources Humaines", Company = "FinancePlus", Location = "Paris", Description = "Alternance de 12 mois au sein du service RH. Recrutement, formation et gestion administrative du personnel.", ContractType = "Alternance", Salary = "1000 - 1400 EUR/mois", Category = "RH", IsRemote = false, CreatedAt = new DateTime(2026, 4, 11), Tags = "RH,Recrutement,Formation", CreatedByUserId = marie.Id, ExperienceRequired = "Junior", EducationLevel = "Bac+3", Benefits = "Tickets restaurant,Transport" },
        };

        db.JobOffers.AddRange(offers);
        await db.SaveChangesAsync();

        // ── Applications (candidates apply to relevant offers) ──
        var savedOffers = db.JobOffers.ToList();

        var applications = new List<Application>
        {
            // Jean Dupont (dev full stack) postule aux offres tech
            new() { JobOfferId = savedOffers.First(o => o.Title.Contains("Full Stack")).Id, FullName = "Jean Dupont", Email = jean.Email!, Phone = jean.PhoneNumber, CoverLetter = "Passionne par Angular et .NET depuis 3 ans, je souhaite rejoindre TechCorp pour contribuer a vos projets innovants. Mon experience en CI/CD et architecture cloud serait un atout pour votre equipe.", Status = "Reviewed", AppliedAt = new DateTime(2026, 4, 2), UserId = jean.Id },
            new() { JobOfferId = savedOffers.First(o => o.Title.Contains("DevOps")).Id, FullName = "Jean Dupont", Email = jean.Email!, Phone = jean.PhoneNumber, CoverLetter = "Fort de mon experience en deploiement cloud et conteneurisation, je suis tres interesse par ce poste DevOps chez CloudNine.", Status = "Pending", AppliedAt = new DateTime(2026, 4, 10), UserId = jean.Id },

            // Alice Moreau (designer) postule aux offres design
            new() { JobOfferId = savedOffers.First(o => o.Title.Contains("UX/UI")).Id, FullName = "Alice Moreau", Email = alice.Email!, Phone = alice.PhoneNumber, CoverLetter = "Avec 5 ans d'experience en UX/UI et une maitrise avancee de Figma, je serais ravie de rejoindre CreativeStudio pour piloter votre design system.", Status = "Accepted", AppliedAt = new DateTime(2026, 4, 3), UserId = alice.Id },
            new() { JobOfferId = savedOffers.First(o => o.Title.Contains("Motion")).Id, FullName = "Alice Moreau", Email = alice.Email!, Phone = alice.PhoneNumber, CoverLetter = "Bien que specialisee en UX, j'ai une solide experience en motion design et animation d'interfaces.", Status = "Pending", AppliedAt = new DateTime(2026, 4, 9), UserId = alice.Id },

            // Karim Benali (data) postule aux offres data/tech
            new() { JobOfferId = savedOffers.First(o => o.Title.Contains("Data Analyst")).Id, FullName = "Karim Benali", Email = karim.Email!, Phone = karim.PhoneNumber, CoverLetter = "Diplome en statistiques avec des competences solides en Python et SQL, je suis motive pour rejoindre StartupFlow et transformer vos donnees en insights actionnables.", Status = "Reviewed", AppliedAt = new DateTime(2026, 4, 8), UserId = karim.Id },

            // Camille Roux (chef de projet) postule aux offres management
            new() { JobOfferId = savedOffers.First(o => o.Title.Contains("Chef de Projet")).Id, FullName = "Camille Roux", Email = camille.Email!, Phone = camille.PhoneNumber, CoverLetter = "Certifiee Scrum Master avec 4 ans d'experience en gestion de projets digitaux, je souhaite apporter mon expertise agile a StartupFlow.", Status = "Accepted", AppliedAt = new DateTime(2026, 4, 5), UserId = camille.Id },
            new() { JobOfferId = savedOffers.First(o => o.Title.Contains("Marketing Digital")).Id, FullName = "Camille Roux", Email = camille.Email!, Phone = camille.PhoneNumber, CoverLetter = "Mon experience en pilotage de projets digitaux et ma connaissance du SEO/SEA font de moi une candidate ideale pour ce poste.", Status = "Pending", AppliedAt = new DateTime(2026, 4, 6), UserId = camille.Id },

            // Hugo Lambert (etudiant) postule aux stages/alternances
            new() { JobOfferId = savedOffers.First(o => o.Title.Contains("Stagiaire")).Id, FullName = "Hugo Lambert", Email = hugo.Email!, Phone = hugo.PhoneNumber, CoverLetter = "Etudiant en 5eme annee d'informatique, je recherche un stage de fin d'etudes en backend Java. J'ai deja realise des projets personnels en Spring Boot.", Status = "Reviewed", AppliedAt = new DateTime(2026, 4, 4), UserId = hugo.Id },
            new() { JobOfferId = savedOffers.First(o => o.Title.Contains("Alternant")).Id, FullName = "Hugo Lambert", Email = hugo.Email!, Phone = hugo.PhoneNumber, CoverLetter = "Interesse par les RH en complement de ma formation technique, je serais ravi de decouvrir ce domaine chez FinancePlus.", Status = "Rejected", AppliedAt = new DateTime(2026, 4, 11), UserId = hugo.Id },
        };

        db.Applications.AddRange(applications);
        await db.SaveChangesAsync();
    }
    else if (seedDemoData)
    {
        // Base de developpement deja peuplee : on garantit seulement l'admin.
        if (await userManager.FindByEmailAsync("admin@lpde.fr") == null)
        {
            var admin = new AppUser { UserName = "admin@lpde.fr", Email = "admin@lpde.fr", FirstName = "Admin", LastName = "LPDE", Role = "Admin", EmailConfirmed = true };
            await userManager.CreateAsync(admin, demoAdminPassword);
            await userManager.AddToRoleAsync(admin, "Admin");
        }
    }
}

app.Run();

/// <summary>
/// Rend la classe implicite des instructions de haut niveau visible au
/// projet de tests : <c>WebApplicationFactory&lt;Program&gt;</c> a besoin
/// d'un type pour savoir quelle application monter.
/// </summary>
public partial class Program { }
