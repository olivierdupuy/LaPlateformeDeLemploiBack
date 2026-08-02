using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using lpdeBack.Models;

namespace lpdeBack.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<JobOffer> JobOffers => Set<JobOffer>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<Interview> Interviews => Set<Interview>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<CvSection> CvSections => Set<CvSection>();
    public DbSet<PushToken> PushTokens { get; set; }
    public DbSet<JobNote> JobNotes => Set<JobNote>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<JobReport> JobReports => Set<JobReport>();
    public DbSet<CompanyReview> CompanyReviews => Set<CompanyReview>();
    public DbSet<SalaryContribution> SalaryContributions => Set<SalaryContribution>();
    public DbSet<CompanyQuestion> CompanyQuestions => Set<CompanyQuestion>();
    public DbSet<CompanyAnswer> CompanyAnswers => Set<CompanyAnswer>();
    public DbSet<CompanyFollow> CompanyFollows => Set<CompanyFollow>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<JobEvent> JobEvents => Set<JobEvent>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<NewsletterSubscriber> NewsletterSubscribers => Set<NewsletterSubscriber>();
    public DbSet<NewsletterCampaign> NewsletterCampaigns => Set<NewsletterCampaign>();
    public DbSet<NewsletterDelivery> NewsletterDeliveries => Set<NewsletterDelivery>();

    // ── Exploitation ──
    public DbSet<ErreurNavigateur> ErreursNavigateur => Set<ErreurNavigateur>();

    // ── Conformite ──
    public DbSet<SignalementDsa> SignalementsDsa => Set<SignalementDsa>();
    public DbSet<PreferencesCourriel> PreferencesCourriel => Set<PreferencesCourriel>();
    public DbSet<RetourCourriel> RetoursCourriel => Set<RetourCourriel>();

    // ── Facturation ──
    public DbSet<Abonnement> Abonnements => Set<Abonnement>();
    public DbSet<MiseEnAvant> MisesEnAvant => Set<MiseEnAvant>();
    public DbSet<Facture> Factures => Set<Facture>();

    // ── Integrations ──
    public DbSet<JetonApi> JetonsApi => Set<JetonApi>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<LivraisonWebhook> LivraisonsWebhook => Set<LivraisonWebhook>();
    public DbSet<Diffusion> Diffusions => Set<Diffusion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>().ToTable("Users");
        modelBuilder.Entity<Microsoft.AspNetCore.Identity.IdentityRole>().ToTable("Roles");
        modelBuilder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<string>>().ToTable("UserRoles");
        modelBuilder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<string>>().ToTable("UserClaims");
        modelBuilder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<string>>().ToTable("UserLogins");
        modelBuilder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<string>>().ToTable("UserTokens");
        modelBuilder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<string>>().ToTable("RoleClaims");

        modelBuilder.Entity<JobOffer>(entity =>
        {
            entity.HasIndex(e => e.Category);
            entity.HasIndex(e => e.ContractType);
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.Company);
            entity.HasIndex(e => e.Title);
            // Couvre le filtrage liste + regroupement par entreprise sur gros volume.
            entity.HasIndex(e => new { e.IsActive, e.ModerationStatus, e.Company });
            entity.HasIndex(e => e.ExternalId);
            // Le dedoublonnage inter-sources interroge cette colonne pour
            // chaque offre importee : sans index, un import de dix mille
            // offres ferait dix mille balayages de table.
            entity.HasIndex(e => e.Empreinte);
            // L'expiration des offres importees balaie par source et par
            // date de derniere vue.
            entity.HasIndex(e => new { e.ExternalSource, e.VueChezLaSourceLe });
            entity.HasOne(j => j.CreatedByUser).WithMany().HasForeignKey(j => j.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        // ── Exploitation, conformite, facturation, integrations ──
        // Les index posent les acces reels : regrouper les erreurs par
        // empreinte, retrouver des preferences par adresse ou par jeton,
        // authentifier un appel d'API par l'empreinte de sa cle.
        modelBuilder.Entity<ErreurNavigateur>(e =>
        {
            e.HasIndex(x => x.Empreinte).IsUnique();
            e.HasIndex(x => x.DerniereVue);
        });

        modelBuilder.Entity<SignalementDsa>(e =>
        {
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => x.Statut);
        });

        modelBuilder.Entity<PreferencesCourriel>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.Jeton).IsUnique();
        });

        modelBuilder.Entity<RetourCourriel>(e => e.HasIndex(x => x.Email).IsUnique());

        modelBuilder.Entity<Abonnement>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.Statut);
        });

        modelBuilder.Entity<MiseEnAvant>(e =>
        {
            e.HasIndex(x => x.JobOfferId);
            e.HasIndex(x => x.FinLe);
        });

        modelBuilder.Entity<Facture>(e =>
        {
            e.HasIndex(x => x.Numero).IsUnique();
            e.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<JetonApi>(e =>
        {
            e.HasIndex(x => x.Empreinte).IsUnique();
            e.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<Webhook>(e => e.HasIndex(x => x.UserId));
        modelBuilder.Entity<LivraisonWebhook>(e => e.HasIndex(x => new { x.WebhookId, x.CreeLe }));

        modelBuilder.Entity<Application>(entity =>
        {
            entity.HasOne(a => a.JobOffer).WithMany(j => j.Applications).HasForeignKey(a => a.JobOfferId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(a => a.Email);
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            // Chaque requete authentifiee cherche la session par son jti :
            // sans index unique, la verification couterait un balayage de
            // table a chaque appel.
            entity.HasIndex(s => s.Jti).IsUnique();
            entity.HasIndex(s => new { s.UserId, s.RevokedAt });
        });

        modelBuilder.Entity<NewsletterSubscriber>(entity =>
        {
            // Une adresse, un abonne. Sans cette unicite, un formulaire
            // soumis deux fois creerait deux abonnements — donc deux copies
            // de chaque message, et deux desinscriptions a faire.
            entity.HasIndex(s => s.Email).IsUnique();
            entity.HasIndex(s => s.UnsubscribeToken);
            entity.HasIndex(s => new { s.Status, s.UnsubscribedAt });
            entity.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<NewsletterDelivery>(entity =>
        {
            entity.HasOne(d => d.Campaign).WithMany(c => c.Deliveries)
                  .HasForeignKey(d => d.CampaignId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(d => d.Subscriber).WithMany()
                  .HasForeignKey(d => d.SubscriberId).OnDelete(DeleteBehavior.Cascade);
            // C'est cet index unique qui empeche d'ecrire deux fois a la meme
            // personne quand un envoi reprend apres un arret.
            entity.HasIndex(d => new { d.CampaignId, d.SubscriberId }).IsUnique();
            entity.HasIndex(d => new { d.CampaignId, d.Status });
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(n => new { n.UserId, n.IsRead });
        });

        modelBuilder.Entity<SavedSearch>(entity =>
        {
            entity.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(s => s.UserId);
        });

        modelBuilder.Entity<Interview>(entity =>
        {
            entity.HasOne(i => i.Application).WithMany(a => a.Interviews).HasForeignKey(i => i.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(i => i.ApplicationId);
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasOne(m => m.Sender).WithMany().HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(m => m.Receiver).WithMany().HasForeignKey(m => m.ReceiverId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(m => m.Application).WithMany(a => a.Messages).HasForeignKey(m => m.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(m => new { m.ApplicationId, m.CreatedAt });
        });

        modelBuilder.Entity<CvSection>(entity =>
        {
            entity.HasOne(c => c.User).WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(c => new { c.UserId, c.SectionType });
        });

        modelBuilder.Entity<MessageTemplate>(entity =>
        {
            entity.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JobNote>(entity =>
        {
            entity.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(n => n.JobOffer).WithMany().HasForeignKey(n => n.JobOfferId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(n => new { n.UserId, n.JobOfferId }).IsUnique();
        });

        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(a => a.CreatedAt);
            entity.HasIndex(a => a.Action);
        });

        modelBuilder.Entity<PlatformSetting>(entity =>
        {
            entity.HasIndex(s => s.Key).IsUnique();
        });

        modelBuilder.Entity<Announcement>(entity =>
        {
            entity.HasOne(a => a.CreatedByUser).WithMany().HasForeignKey(a => a.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(a => a.IsActive);
        });

        modelBuilder.Entity<JobOffer>(entity2 =>
        {
            entity2.HasIndex(e => e.ModerationStatus);
        });
    }
}
