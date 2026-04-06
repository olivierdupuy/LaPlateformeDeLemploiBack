using System.Text;
using LaPlateformeDeLemploiAPI.Data;
using LaPlateformeDeLemploiAPI.Hubs;
using LaPlateformeDeLemploiAPI.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
        // Allow SignalR to receive the JWT token from the query string
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
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
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddSingleton<LaPlateformeDeLemploiAPI.Services.ResumeParserService>();
builder.Services.AddSingleton<LaPlateformeDeLemploiAPI.Services.CompanyAIService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAngular");
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

// Auto-migrate + seed test users
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Creer les comptes de test s'ils n'existent pas
    if (!db.Users.Any(u => u.Email == "chercheur@test.fr"))
    {
        db.Users.Add(new User
        {
            Email = "chercheur@test.fr",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test123!"),
            FirstName = "Marie",
            LastName = "Dupont",
            Role = "JobSeeker",
            Phone = "06 12 34 56 78",
            Bio = "Developpeuse Full Stack passionnee avec 5 ans d'experience. Specialisee en Angular, .NET et SQL Server. Actuellement en recherche active d'un poste en CDI sur Paris ou en full remote.",
            Skills = "Angular, .NET, C#, TypeScript, SQL Server, Azure, Docker, Git, REST API, Agile/Scrum"
        });
    }

    if (!db.Users.Any(u => u.Email == "entreprise@test.fr"))
    {
        // Lier au TechCorp France (company id 1)
        db.Users.Add(new User
        {
            Email = "entreprise@test.fr",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test123!"),
            FirstName = "Pierre",
            LastName = "Martin",
            Role = "Company",
            Phone = "01 23 45 67 89",
            CompanyId = 1 // TechCorp France
        });
    }

    db.SaveChanges();
}

app.Run();
