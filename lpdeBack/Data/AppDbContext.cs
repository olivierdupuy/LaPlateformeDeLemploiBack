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
            entity.HasOne(j => j.CreatedByUser).WithMany().HasForeignKey(j => j.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Application>(entity =>
        {
            entity.HasOne(a => a.JobOffer).WithMany(j => j.Applications).HasForeignKey(a => a.JobOfferId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(a => a.Email);
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
