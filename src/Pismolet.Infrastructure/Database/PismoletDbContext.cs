using Microsoft.EntityFrameworkCore;

namespace Pismolet.Web.Infrastructure.Database;

public sealed class PismoletDbContext(DbContextOptions<PismoletDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<MailingEntity> Mailings => Set<MailingEntity>();
    public DbSet<ImportBatchEntity> ImportBatches => Set<ImportBatchEntity>();
    public DbSet<RecipientEntity> Recipients => Set<RecipientEntity>();
    public DbSet<ImportIssueEntity> ImportIssues => Set<ImportIssueEntity>();
    public DbSet<MailingDeclarationEntity> MailingDeclarations => Set<MailingDeclarationEntity>();
    public DbSet<MailingMessageDraftEntity> MailingMessageDrafts => Set<MailingMessageDraftEntity>();
    public DbSet<AuditRecordEntity> AuditRecords => Set<AuditRecordEntity>();
    public DbSet<GlobalSuppressionEntity> GlobalSuppressions => Set<GlobalSuppressionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Email);
            entity.HasIndex(x => x.NormalizedEmail).IsUnique();
            entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
            entity.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PasswordHash).IsRequired();
            entity.Property(x => x.ConfirmationToken).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ProfileStatus).HasMaxLength(40).IsRequired();
        });

        modelBuilder.Entity<MailingEntity>(entity =>
        {
            entity.ToTable("mailings");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OwnerEmail);
            entity.Property(x => x.OwnerEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(160).IsRequired();
            entity.Property(x => x.StatusRu).HasMaxLength(80).IsRequired();
            entity.Property(x => x.PublicId).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<ImportBatchEntity>(entity =>
        {
            entity.ToTable("import_batches");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.MailingId);
            entity.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.SourceFormat).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.HasOne<MailingEntity>().WithMany().HasForeignKey(x => x.MailingId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecipientEntity>(entity =>
        {
            entity.ToTable("recipients");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.MailingId);
            entity.HasIndex(x => x.NormalizedEmail);
            entity.Property(x => x.SourceEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(40).IsRequired();
            entity.Property(x => x.ExclusionReason).HasMaxLength(200);
            entity.HasOne<MailingEntity>().WithMany().HasForeignKey(x => x.MailingId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ImportBatchEntity>().WithMany().HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ImportIssueEntity>(entity =>
        {
            entity.ToTable("import_issues");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ImportBatchId);
            entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(200).IsRequired();
            entity.HasOne<ImportBatchEntity>().WithMany().HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MailingDeclarationEntity>(entity =>
        {
            entity.ToTable("mailing_declarations");
            entity.HasKey(x => x.MailingId);
            entity.HasIndex(x => x.UserEmail);
            entity.Property(x => x.UserEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.BaseSource).HasMaxLength(60).IsRequired();
            entity.Property(x => x.DeclarationVersion).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Ip).HasMaxLength(80).IsRequired();
            entity.Property(x => x.UserAgent).HasMaxLength(512).IsRequired();
            entity.HasOne<MailingEntity>().WithOne().HasForeignKey<MailingDeclarationEntity>(x => x.MailingId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MailingMessageDraftEntity>(entity =>
        {
            entity.ToTable("mailing_message_drafts");
            entity.HasKey(x => x.MailingId);
            entity.Property(x => x.SenderName).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Body).IsRequired();
            entity.Property(x => x.MessageType).HasMaxLength(40).IsRequired();
            entity.HasOne<MailingEntity>().WithOne().HasForeignKey<MailingMessageDraftEntity>(x => x.MailingId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditRecordEntity>(entity =>
        {
            entity.ToTable("audit_records");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.User);
            entity.Property(x => x.User).HasMaxLength(254).IsRequired();
            entity.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Ip).HasMaxLength(80).IsRequired();
            entity.Property(x => x.UserAgent).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Context).IsRequired();
        });

        modelBuilder.Entity<GlobalSuppressionEntity>(entity =>
        {
            entity.ToTable("global_suppressions");
            entity.HasKey(x => x.NormalizedEmail);
            entity.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
        });
    }
}

public sealed class UserEntity
{
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ConfirmationToken { get; set; } = string.Empty;
    public bool EmailConfirmed { get; set; }
    public string ProfileStatus { get; set; } = string.Empty;
    public int DailySendLimit { get; set; }
    public int TotalSendLimit { get; set; }
    public bool PremoderationRequired { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class MailingEntity
{
    public Guid Id { get; set; }
    public string OwnerEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string StatusRu { get; set; } = string.Empty;
    public string PublicId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ImportBatchEntity
{
    public Guid Id { get; set; }
    public Guid MailingId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string SourceFormat { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public int TotalRows { get; set; }
    public int Accepted { get; set; }
    public int Duplicates { get; set; }
    public int Invalid { get; set; }
    public int GloballySuppressed { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class RecipientEntity
{
    public Guid Id { get; set; }
    public Guid MailingId { get; set; }
    public string SourceEmail { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ExclusionReason { get; set; }
    public Guid? ImportBatchId { get; set; }
}

public sealed class ImportIssueEntity
{
    public Guid Id { get; set; }
    public Guid ImportBatchId { get; set; }
    public int RowNumber { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class MailingDeclarationEntity
{
    public Guid MailingId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string BaseSource { get; set; } = string.Empty;
    public bool IsBaseLegalityConfirmed { get; set; }
    public bool IsAdvertisingConsentConfirmed { get; set; }
    public string DeclarationVersion { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string Ip { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
}

public sealed class MailingMessageDraftEntity
{
    public Guid MailingId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AuditRecordEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string User { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string Context { get; set; } = "{}";
}

public sealed class GlobalSuppressionEntity
{
    public string NormalizedEmail { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
