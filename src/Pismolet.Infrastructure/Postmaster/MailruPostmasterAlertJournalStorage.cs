using Microsoft.EntityFrameworkCore;

namespace Pismolet.Web.Infrastructure.Postmaster;

public sealed class MailruPostmasterAlertEventEntity
{
    public Guid Id { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public long MessagesSent { get; set; }
    public double ObservedValue { get; set; }
    public double ThresholdValue { get; set; }
    public DateTimeOffset FirstObservedAt { get; set; }
    public DateTimeOffset LastObservedAt { get; set; }
    public int OccurrenceCount { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? LastNotifiedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class MailruPostmasterAlertJournalModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<MailruPostmasterAlertEventEntity>(entity =>
        {
            entity.ToTable("mailru_postmaster_alert_events");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Domain, x.Status, x.UpdatedAt });
            entity.HasIndex(x => new { x.Domain, x.Severity, x.UpdatedAt });
            entity.HasIndex(x => new { x.Domain, x.Code, x.Fingerprint })
                .IsUnique()
                .HasFilter("\"Status\" = 'active'");
            entity.Property(x => x.Domain).HasMaxLength(253).IsRequired();
            entity.Property(x => x.Code).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Severity).HasMaxLength(24).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DateFrom).HasColumnType("date");
            entity.Property(x => x.DateTo).HasColumnType("date");
        });
    }
}

public interface IMailruPostmasterAlertJournalStore
{
    Task<IReadOnlyList<MailruPostmasterAlertJournalEvent>> ReadActiveAsync(
        string domain,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MailruPostmasterAlertJournalEvent>> ReadRecentAsync(
        string domain,
        MailruPostmasterAlertEventStatus? status = null,
        MailruPostmasterAlertSeverity? severity = null,
        int take = 100,
        CancellationToken cancellationToken = default);

    Task ApplyAsync(
        IReadOnlyList<MailruPostmasterAlertJournalChange> changes,
        CancellationToken cancellationToken = default);
}

public sealed class EfMailruPostmasterAlertJournalStore(MailruPostmasterDbContext db)
    : IMailruPostmasterAlertJournalStore
{
    public async Task<IReadOnlyList<MailruPostmasterAlertJournalEvent>> ReadActiveAsync(
        string domain,
        CancellationToken cancellationToken = default)
    {
        var normalizedDomain = NormalizeDomain(domain);
        var entities = await db.AlertEvents
            .AsNoTracking()
            .Where(x => x.Domain == normalizedDomain && x.Status == "active")
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.Code)
            .ToListAsync(cancellationToken);

        return entities.Select(ToModel).ToArray();
    }

    public async Task<IReadOnlyList<MailruPostmasterAlertJournalEvent>> ReadRecentAsync(
        string domain,
        MailruPostmasterAlertEventStatus? status = null,
        MailruPostmasterAlertSeverity? severity = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var normalizedDomain = NormalizeDomain(domain);
        var query = db.AlertEvents
            .AsNoTracking()
            .Where(x => x.Domain == normalizedDomain);

        if (status is not null)
        {
            var storedStatus = ToStored(status.Value);
            query = query.Where(x => x.Status == storedStatus);
        }

        if (severity is not null)
        {
            var storedSeverity = ToStored(severity.Value);
            query = query.Where(x => x.Severity == storedSeverity);
        }

        var entities = await query
            .OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.LastObservedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);

        return entities.Select(ToModel).ToArray();
    }

    public async Task ApplyAsync(
        IReadOnlyList<MailruPostmasterAlertJournalChange> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0)
        {
            return;
        }

        foreach (var change in changes)
        {
            var journalEvent = change.Event;
            var entity = await db.AlertEvents.SingleOrDefaultAsync(
                x => x.Id == journalEvent.Id,
                cancellationToken);

            if (entity is null && change.ChangeType != MailruPostmasterAlertJournalChangeType.Activated)
            {
                var normalizedDomain = NormalizeDomain(journalEvent.Domain);
                var normalizedCode = Normalize(journalEvent.Code, 120, "unknown");
                var normalizedFingerprint = Normalize(journalEvent.Fingerprint, 64, string.Empty);
                entity = await db.AlertEvents.SingleOrDefaultAsync(
                    x => x.Domain == normalizedDomain &&
                         x.Code == normalizedCode &&
                         x.Fingerprint == normalizedFingerprint &&
                         x.Status == "active",
                    cancellationToken);
            }

            if (entity is null)
            {
                entity = new MailruPostmasterAlertEventEntity { Id = journalEvent.Id };
                db.AlertEvents.Add(entity);
            }

            Copy(journalEvent, entity);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void Copy(
        MailruPostmasterAlertJournalEvent source,
        MailruPostmasterAlertEventEntity target)
    {
        target.Domain = NormalizeDomain(source.Domain);
        target.Code = Normalize(source.Code, 120, "unknown");
        target.Severity = ToStored(source.Severity);
        target.Category = Normalize(source.Category, 64, "unknown");
        target.Status = ToStored(source.Status);
        target.Fingerprint = Normalize(source.Fingerprint, 64, string.Empty);
        target.DateFrom = source.DateFrom;
        target.DateTo = source.DateTo;
        target.MessagesSent = Math.Max(0, source.MessagesSent);
        target.ObservedValue = source.ObservedValue;
        target.ThresholdValue = source.ThresholdValue;
        target.FirstObservedAt = source.FirstObservedAt;
        target.LastObservedAt = source.LastObservedAt;
        target.OccurrenceCount = Math.Max(1, source.OccurrenceCount);
        target.ResolvedAt = source.ResolvedAt;
        target.LastNotifiedAt = source.LastNotifiedAt;
        target.CreatedAt = source.CreatedAt;
        target.UpdatedAt = source.UpdatedAt;
    }

    private static MailruPostmasterAlertJournalEvent ToModel(MailruPostmasterAlertEventEntity entity) => new(
        entity.Id,
        entity.Domain,
        entity.Code,
        ParseSeverity(entity.Severity),
        entity.Category,
        ParseStatus(entity.Status),
        entity.Fingerprint,
        entity.DateFrom,
        entity.DateTo,
        entity.MessagesSent,
        entity.ObservedValue,
        entity.ThresholdValue,
        entity.FirstObservedAt,
        entity.LastObservedAt,
        entity.OccurrenceCount,
        entity.ResolvedAt,
        entity.LastNotifiedAt,
        entity.CreatedAt,
        entity.UpdatedAt);

    private static string ToStored(MailruPostmasterAlertSeverity severity) => severity switch
    {
        MailruPostmasterAlertSeverity.Critical => "critical",
        MailruPostmasterAlertSeverity.Warning => "warning",
        _ => "info"
    };

    private static string ToStored(MailruPostmasterAlertEventStatus status) => status switch
    {
        MailruPostmasterAlertEventStatus.Resolved => "resolved",
        _ => "active"
    };

    private static MailruPostmasterAlertSeverity ParseSeverity(string value) => value switch
    {
        "critical" => MailruPostmasterAlertSeverity.Critical,
        "warning" => MailruPostmasterAlertSeverity.Warning,
        _ => MailruPostmasterAlertSeverity.Info
    };

    private static MailruPostmasterAlertEventStatus ParseStatus(string value) =>
        value == "resolved"
            ? MailruPostmasterAlertEventStatus.Resolved
            : MailruPostmasterAlertEventStatus.Active;

    private static string NormalizeDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new ArgumentException("Домен Mail.ru Postmaster не задан.", nameof(domain));
        }

        return domain.Trim().TrimEnd('.').ToLowerInvariant();
    }

    private static string Normalize(string? value, int maxLength, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}

public sealed class EmptyMailruPostmasterAlertJournalStore : IMailruPostmasterAlertJournalStore
{
    public Task<IReadOnlyList<MailruPostmasterAlertJournalEvent>> ReadActiveAsync(
        string domain,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MailruPostmasterAlertJournalEvent>>(
            Array.Empty<MailruPostmasterAlertJournalEvent>());

    public Task<IReadOnlyList<MailruPostmasterAlertJournalEvent>> ReadRecentAsync(
        string domain,
        MailruPostmasterAlertEventStatus? status = null,
        MailruPostmasterAlertSeverity? severity = null,
        int take = 100,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MailruPostmasterAlertJournalEvent>>(
            Array.Empty<MailruPostmasterAlertJournalEvent>());

    public Task ApplyAsync(
        IReadOnlyList<MailruPostmasterAlertJournalChange> changes,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
