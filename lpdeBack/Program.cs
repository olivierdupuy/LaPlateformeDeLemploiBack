using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using lpdeBack.Data;
using lpdeBack.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

var jwtKey = builder.Configuration["Jwt:Key"] ?? "LpdeSecretKey2026SuperSecure!@#$%^&*()_+";
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
});

builder.Services.AddAuthorization();
builder.Services.AddHttpClient();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins(
                  "http://localhost:4200",
                  "http://localhost",
                  "https://localhost",
                  "capacitor://localhost"
              )
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseCors("AllowAngular");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// ═══════════════════════════════════
//  SEED: Database + Roles + Users + Offers + Applications
// ═══════════════════════════════════
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<AppDbContext>();
    var userManager = services.GetRequiredService<UserManager<AppUser>>();
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

    db.Database.Migrate();

    // ── Roles ──
    foreach (var role in new[] { "Admin", "Recruiter", "Candidate" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    // Skip seed if data already exists
    if (!db.JobOffers.Any())
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
        var admin = await CreateUser(new AppUser { Email = "admin@lpde.fr", FirstName = "Admin", LastName = "LPDE", Role = "Admin", Bio = "Administrateur de la plateforme." }, "Admin123!");

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
    else
    {
        // Just ensure admin exists even if data is already seeded
        if (await userManager.FindByEmailAsync("admin@lpde.fr") == null)
        {
            var admin = new AppUser { UserName = "admin@lpde.fr", Email = "admin@lpde.fr", FirstName = "Admin", LastName = "LPDE", Role = "Admin", EmailConfirmed = true };
            await userManager.CreateAsync(admin, "Admin123!");
            await userManager.AddToRoleAsync(admin, "Admin");
        }
    }
}

app.Run();
