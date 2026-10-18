using Credia.Domain;
using Microsoft.EntityFrameworkCore;
namespace Credia.Infrastructure;
public sealed class CrediaDbContext(DbContextOptions<CrediaDbContext> options) : DbContext(options)
{
    public DbSet<CreditApplication> Applications => Set<CreditApplication>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<DocumentRecord> Documents => Set<DocumentRecord>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<AppUser>().HasIndex(x => x.Email).IsUnique();
        model.Entity<AppUser>().Property(x => x.Email).HasMaxLength(160);
        var app = model.Entity<CreditApplication>();
        app.HasIndex(x => x.Reference).IsUnique();
        app.Property(x => x.Reference).HasMaxLength(32);
        app.HasIndex(x => new { x.OwnerId, x.Status, x.CreatedAt });
        app.Property(x => x.Version).IsConcurrencyToken();
        app.Property(x => x.BorrowerName).HasMaxLength(120);
        app.Property(x => x.DocumentNumber).HasMaxLength(12);
        app.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        app.HasMany(x => x.Documents).WithOne().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        app.HasMany(x => x.Evaluations).WithOne().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        app.HasMany(x => x.Audit).WithOne().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<AuditEntry>().HasIndex(x => new { x.ApplicationId, x.CreatedAt });
        model.Entity<OutboxMessage>().HasIndex(x => new { x.DeliveredAt, x.CreatedAt });
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var prop in entity.GetProperties().Where(p => p.ClrType == typeof(decimal))) { prop.SetPrecision(18); prop.SetScale(4); }
    }
}
