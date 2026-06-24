using Microsoft.EntityFrameworkCore;

namespace Pismolet.Web.Infrastructure.Database;

public sealed class LegalEvidenceDbContext(DbContextOptions<LegalEvidenceDbContext> options) : DbContext(options)
{
    public DbSet<LegalDocumentVersionEntity> LegalDocumentVersions => Set<LegalDocumentVersionEntity>();
    public DbSet<LegalEventEntity> LegalEvents => Set<LegalEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LegalDocumentVersionEntity>(entity =>
        {
            entity.ToTable("legal_document_versions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.DocumentKey, x.Version }).IsUnique();
            entity.HasIndex(x => new { x.DocumentKey, x.IsActive });
            entity.Property(x => x.DocumentKey).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Version).HasMaxLength(80).IsRequired();
            entity.Property(x => x.TextHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Text).IsRequired();
            entity.Property(x => x.Url).HasMaxLength(512);
        });

        modelBuilder.Entity<LegalEventEntity>(entity =>
        {
            entity.ToTable("legal_events");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ClientId, x.CreatedAt });
            entity.HasIndex(x => new { x.MailingId, x.CreatedAt });
            entity.HasIndex(x => new { x.ImportBatchId, x.CreatedAt });
            entity.HasIndex(x => x.EventType);
            entity.Property(x => x.EventType).HasMaxLength(120).IsRequired();
            entity.Property(x => x.ClientId).HasMaxLength(254).IsRequired();
            entity.Property(x => x.UserId).HasMaxLength(254);
            entity.Property(x => x.DocumentKey).HasMaxLength(120);
            entity.Property(x => x.DocumentVersion).HasMaxLength(80);
            entity.Property(x => x.TextHash).HasMaxLength(64);
            entity.Property(x => x.EventTextSnapshot).HasMaxLength(16000);
            entity.Property(x => x.Result).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Ip).HasMaxLength(80);
            entity.Property(x => x.UserAgent).HasMaxLength(512);
            entity.Property(x => x.Route).HasMaxLength(512);
            entity.Property(x => x.MetadataJson).IsRequired();
        });
    }
}

public sealed class LegalDocumentVersionEntity
{
    public Guid Id { get; set; }
    public string DocumentKey { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string TextHash { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? Url { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class LegalEventEntity
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public Guid? ImportBatchId { get; set; }
    public Guid? MailingId { get; set; }
    public string? DocumentKey { get; set; }
    public string? DocumentVersion { get; set; }
    public string? TextHash { get; set; }
    public string? EventTextSnapshot { get; set; }
    public string Result { get; set; } = string.Empty;
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public string? Route { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}
