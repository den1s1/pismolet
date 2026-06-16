using Microsoft.EntityFrameworkCore;

namespace Pismolet.Web.Infrastructure.Database;

public sealed class PismoletDbContext(DbContextOptions<PismoletDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<MailingEntity> Mailings => Set<MailingEntity>();
    public DbSet<RecipientEntity> Recipients => Set<RecipientEntity>();
    public DbSet<AuditRecordEntity> AuditRecords => Set<AuditRecordEntity>();
    public DbSet<GlobalSuppressionEntity> GlobalSuppressions => Set<GlobalSuppressionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>().HasKey(x => x.Email);
        modelBuilder.Entity<MailingEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<RecipientEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<AuditRecordEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<GlobalSuppressionEntity>().HasKey(x => x.Email);
    }
}

public sealed class UserEntity
{
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool EmailConfirmed { get; set; }
}

public sealed class MailingEntity
{
    public Guid Id { get; set; }
    public string OwnerEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string StatusRu { get; set; } = string.Empty;
}

public sealed class RecipientEntity
{
    public Guid Id { get; set; }
    public Guid MailingId { get; set; }
    public string Email { get; set; } = string.Empty;
}

public sealed class AuditRecordEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string User { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Context { get; set; } = "{}";
}

public sealed class GlobalSuppressionEntity
{
    public string Email { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
