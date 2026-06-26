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
    public DbSet<SendEventEntity> SendEvents => Set<SendEventEntity>();
    public DbSet<TrackedLinkEntity> TrackedLinks => Set<TrackedLinkEntity>();
    public DbSet<ClickEventEntity> ClickEvents => Set<ClickEventEntity>();
    public DbSet<PostfixDeliveryEventEntity> PostfixDeliveryEvents => Set<PostfixDeliveryEventEntity>();
    public DbSet<ProviderWebhookEventEntity> ProviderWebhookEvents => Set<ProviderWebhookEventEntity>();
    public DbSet<ClientSuppressionEntity> ClientSuppressions => Set<ClientSuppressionEntity>();
    public DbSet<ReplyEventEntity> ReplyEvents => Set<ReplyEventEntity>();

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
            entity.HasIndex(x => new { x.MailingId, x.RowNumber });
            entity.HasIndex(x => x.NormalizedEmail);
            entity.Property(x => x.SourceEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.RowNumber).IsRequired();
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
            entity.HasIndex(x => x.ImportBatchId);
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
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.EmailNormalized).IsUnique();
            entity.HasIndex(x => x.EmailHash);
            entity.HasIndex(x => x.SourceMailingId);
            entity.Property(x => x.EmailNormalized).HasMaxLength(254).IsRequired();
            entity.Property(x => x.EmailHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Source).HasMaxLength(40).IsRequired();
            entity.Property(x => x.SourceRecipientKey).HasMaxLength(120);
            entity.Property(x => x.CreatedIpHash).HasMaxLength(64);
            entity.Property(x => x.UserAgentHash).HasMaxLength(64);
        });

        modelBuilder.Entity<SendEventEntity>(entity =>
        {
            entity.ToTable("send_events");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.MailingId);
            entity.HasIndex(x => new { x.MailingId, x.RecipientEmail }).IsUnique();
            entity.HasIndex(x => x.ProviderMessageId);
            entity.HasIndex(x => x.TrackingToken).IsUnique();
            entity.HasIndex(x => new { x.MailingId, x.Status, x.CreatedAt });
            entity.HasIndex(x => new { x.MailingId, x.DeliveryStatus });
            entity.HasIndex(x => new { x.OwnerEmail, x.UpdatedAt });
            entity.HasIndex(x => new { x.OwnerEmail, x.AcceptedAt });
            entity.HasIndex(x => new { x.OwnerEmail, x.AcceptedUtcDay });
            entity.Property(x => x.OwnerEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.RecipientEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Provider).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ProviderMessageId).HasMaxLength(240);
            entity.Property(x => x.TrackingToken).HasMaxLength(64);
            entity.Property(x => x.ErrorCode).HasMaxLength(120);
            entity.Property(x => x.ErrorMessage).HasMaxLength(1000);
            entity.Property(x => x.DeliveryStatus).HasMaxLength(40).IsRequired();
            entity.Property(x => x.LastDeliverySummary).HasMaxLength(1000);
            entity.HasOne<MailingEntity>().WithMany().HasForeignKey(x => x.MailingId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrackedLinkEntity>(entity =>
        {
            entity.ToTable("tracked_links");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.MailingId);
            entity.HasIndex(x => x.Token).IsUnique();
            entity.HasIndex(x => new { x.MailingId, x.RecipientEmail });
            entity.Property(x => x.RecipientEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.Token).HasMaxLength(64).IsRequired();
            entity.Property(x => x.OriginalUrl).HasMaxLength(2048).IsRequired();
            entity.HasOne<MailingEntity>().WithMany().HasForeignKey(x => x.MailingId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClickEventEntity>(entity =>
        {
            entity.ToTable("click_events");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TrackedLinkId);
            entity.HasIndex(x => new { x.MailingId, x.ClickedAt });
            entity.HasIndex(x => new { x.MailingId, x.RecipientEmail });
            entity.Property(x => x.RecipientEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.Token).HasMaxLength(64).IsRequired();
            entity.Property(x => x.OriginalUrl).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.IpHash).HasMaxLength(64);
            entity.Property(x => x.UserAgentHash).HasMaxLength(64);
            entity.HasOne<TrackedLinkEntity>().WithMany().HasForeignKey(x => x.TrackedLinkId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<MailingEntity>().WithMany().HasForeignKey(x => x.MailingId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProviderWebhookEventEntity>(entity =>
        {
            entity.ToTable("provider_webhook_events");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Provider, x.ProviderEventId }).IsUnique();
            entity.HasIndex(x => x.ProviderMessageId);
            entity.HasIndex(x => x.MailingId);
            entity.HasIndex(x => new { x.MailingId, x.EventType });
            entity.Property(x => x.Provider).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ProviderEventId).HasMaxLength(240).IsRequired();
            entity.Property(x => x.ProviderMessageId).HasMaxLength(240);
            entity.Property(x => x.ClientId).HasMaxLength(254);
            entity.Property(x => x.RecipientEmailNormalized).HasMaxLength(254);
            entity.Property(x => x.EventType).HasMaxLength(40).IsRequired();
            entity.Property(x => x.RawPayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.RawPayloadStored).HasMaxLength(4096);
            entity.Property(x => x.ReasonCode).HasMaxLength(120);
            entity.Property(x => x.ReasonMessage).HasMaxLength(1000);
            entity.Property(x => x.ProcessingStatus).HasMaxLength(40).IsRequired();
        });

        modelBuilder.Entity<ClientSuppressionEntity>(entity =>
        {
            entity.ToTable("client_suppressions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ClientId, x.EmailNormalized }).IsUnique();
            entity.HasIndex(x => x.EmailNormalized);
            entity.Property(x => x.ClientId).HasMaxLength(254).IsRequired();
            entity.Property(x => x.EmailNormalized).HasMaxLength(254).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(40).IsRequired();
            entity.Property(x => x.SourceProviderMessageId).HasMaxLength(240);
        });

        modelBuilder.Entity<ReplyEventEntity>(entity =>
        {
            entity.ToTable("reply_events");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Provider, x.ProviderInboundEventId }).IsUnique();
            entity.HasIndex(x => new { x.MailingId, x.ReceivedAt });
            entity.HasIndex(x => new { x.ClientId, x.ReceivedAt });
            entity.HasIndex(x => new { x.ProcessingStatus, x.ReceivedAt });
            entity.HasIndex(x => x.BodyExpiresAt);
            entity.Property(x => x.Provider).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ProviderInboundEventId).HasMaxLength(240).IsRequired();
            entity.Property(x => x.ClientId).HasMaxLength(254);
            entity.Property(x => x.RecipientEmailNormalized).HasMaxLength(254);
            entity.Property(x => x.FromEmailNormalized).HasMaxLength(254).IsRequired();
            entity.Property(x => x.ToAddress).HasMaxLength(512).IsRequired();
            entity.Property(x => x.ReplyTokenHash).HasMaxLength(64);
            entity.Property(x => x.SubjectPreview).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ForwardToEmailNormalized).HasMaxLength(254);
            entity.Property(x => x.ProcessingStatus).HasMaxLength(40).IsRequired();
            entity.Property(x => x.BodyStorageStatus).HasMaxLength(40).IsRequired();
            entity.Property(x => x.BodyTextStored).HasMaxLength(16000);
            entity.Property(x => x.RawPayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ErrorCode).HasMaxLength(120);
            entity.Property(x => x.ErrorMessage).HasMaxLength(1000);
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
    public int ClientSuppressed { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class RecipientEntity
{
    public Guid Id { get; set; }
    public Guid MailingId { get; set; }
    public int RowNumber { get; set; }
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
    public Guid? ImportBatchId { get; set; }
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
    public DateTimeOffset At { get; set; }
    public string User { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
}

public sealed class GlobalSuppressionEntity
{
    public Guid Id { get; set; }
    public string EmailNormalized { get; set; } = string.Empty;
    public string EmailHash { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public Guid? SourceMailingId { get; set; }
    public string? SourceRecipientKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedIpHash { get; set; }
    public string? UserAgentHash { get; set; }
}

public sealed class SendEventEntity
{
    public Guid Id { get; set; }
    public Guid MailingId { get; set; }
    public string OwnerEmail { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateOnly? AcceptedUtcDay { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? ProviderMessageId { get; set; }
    public string? TrackingToken { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string DeliveryStatus { get; set; } = string.Empty;
    public DateTimeOffset? LastDeliveryEventAt { get; set; }
    public string? LastDeliverySummary { get; set; }
    public DateTimeOffset? FirstOpenedAt { get; set; }
    public DateTimeOffset? LastOpenedAt { get; set; }
    public int OpenCount { get; set; }
}

public sealed class TrackedLinkEntity
{
    public Guid Id { get; set; }
    public Guid MailingId { get; set; }
    public string Token { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public string OriginalUrl { get; set; } = string.Empty;
    public int ClickCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FirstClickedAt { get; set; }
    public DateTimeOffset? LastClickedAt { get; set; }
}

public sealed class ClickEventEntity
{
    public Guid Id { get; set; }
    public Guid TrackedLinkId { get; set; }
    public Guid MailingId { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string OriginalUrl { get; set; } = string.Empty;
    public DateTimeOffset ClickedAt { get; set; }
    public string? IpHash { get; set; }
    public string? UserAgentHash { get; set; }
}

public sealed class ProviderWebhookEventEntity
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderEventId { get; set; } = string.Empty;
    public string? ProviderMessageId { get; set; }
    public Guid? MailingId { get; set; }
    public string? ClientId { get; set; }
    public string? RecipientEmailNormalized { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string RawPayloadHash { get; set; } = string.Empty;
    public string? RawPayloadStored { get; set; }
    public string? ReasonCode { get; set; }
    public string? ReasonMessage { get; set; }
    public string ProcessingStatus { get; set; } = string.Empty;
}

public sealed class ClientSuppressionEntity
{
    public Guid Id { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string EmailNormalized { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string? SourceProviderMessageId { get; set; }
}

public sealed class ReplyEventEntity
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderInboundEventId { get; set; } = string.Empty;
    public Guid? MailingId { get; set; }
    public string? ClientId { get; set; }
    public string RecipientEmailNormalized { get; set; } = string.Empty;
    public string FromEmailNormalized { get; set; } = string.Empty;
    public string ToAddress { get; set; } = string.Empty;
    public string? ReplyTokenHash { get; set; }
    public string SubjectPreview { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
    public string ProcessingStatus { get; set; } = string.Empty;
    public string BodyStorageStatus { get; set; } = string.Empty;
    public DateTimeOffset? BodyExpiresAt { get; set; }
    public string? BodyTextStored { get; set; }
    public string RawPayloadHash { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
