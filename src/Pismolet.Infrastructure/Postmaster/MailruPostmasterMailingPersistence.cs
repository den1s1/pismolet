using Microsoft.EntityFrameworkCore;

namespace Pismolet.Web.Infrastructure.Postmaster;

public sealed class MailruPostmasterMailingDailyMetricEntity
{
    public Guid Id { get; set; }
    public Guid MailingId { get; set; }
    public string MsgType { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public long MessagesSent { get; set; }
    public long Delivered { get; set; }
    public long ProbablySpam { get; set; }
    public long Spam { get; set; }
    public long Complaints { get; set; }
    public long Read { get; set; }
    public long DeletedRead { get; set; }
    public long DeletedUnread { get; set; }
    public double SpamPercent { get; set; }
    public double ProbablySpamPercent { get; set; }
    public double Reputation { get; set; }
    public double Trend { get; set; }
    public DateTimeOffset CollectedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class MailruPostmasterMailingSyncStateEntity
{
    public Guid MailingId { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string MsgType { get; set; } = string.Empty;
    public DateTimeOffset FirstSendAt { get; set; }
    public DateTimeOffset LastSendAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorSummary { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateOnly? LastDateTo { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastDataFingerprint { get; set; }
    public int UnchangedSuccessCount { get; set; }
    public bool HasData { get; set; }
    public bool IsStable { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class MailruPostmasterMailingModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<MailruPostmasterMailingDailyMetricEntity>(entity =>
        {
            entity.ToTable("mailru_postmaster_mailing_daily_metrics");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Domain, x.MailingId, x.Date }).IsUnique();
            entity.HasIndex(x => new { x.MailingId, x.Date });
            entity.HasIndex(x => x.MsgType);
            entity.Property(x => x.Domain).HasMaxLength(253).IsRequired();
            entity.Property(x => x.MsgType).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Date).HasColumnType("date").IsRequired();
        });

        modelBuilder.Entity<MailruPostmasterMailingSyncStateEntity>(entity =>
        {
            entity.ToTable("mailru_postmaster_mailing_sync_states");
            entity.HasKey(x => new { x.Domain, x.MailingId });
            entity.HasIndex(x => new { x.NextAttemptAt, x.CompletedAt });
            entity.HasIndex(x => x.MsgType);
            entity.Property(x => x.Domain).HasMaxLength(253).IsRequired();
            entity.Property(x => x.MsgType).HasMaxLength(32).IsRequired();
            entity.Property(x => x.LastErrorCode).HasMaxLength(120);
            entity.Property(x => x.LastErrorSummary).HasMaxLength(1000);
            entity.Property(x => x.LastDateTo).HasColumnType("date");
            entity.Property(x => x.LastDataFingerprint).HasMaxLength(64);
        });
    }
}

public interface IMailruPostmasterMailingStorage
{
    Task UpsertDailyMetricsAsync(
        Guid mailingId,
        string domain,
        string msgType,
        IReadOnlyCollection<MailruPostmasterDailyStatistics> metrics,
        DateTimeOffset collectedAt,
        CancellationToken cancellationToken = default);

    Task<MailruPostmasterMailingSyncStateEntity?> GetSyncStateAsync(
        string domain,
        Guid mailingId,
        CancellationToken cancellationToken = default);

    Task MarkAttemptAsync(
        string domain,
        Guid mailingId,
        string msgType,
        DateTimeOffset firstSendAt,
        DateTimeOffset lastSendAt,
        DateTimeOffset attemptedAt,
        CancellationToken cancellationToken = default);

    Task MarkSuccessAsync(
        string domain,
        Guid mailingId,
        string msgType,
        DateTimeOffset firstSendAt,
        DateTimeOffset lastSendAt,
        DateTimeOffset succeededAt,
        DateOnly dateTo,
        string? dataFingerprint,
        int unchangedSuccessCount,
        bool hasData,
        bool isStable,
        DateTimeOffset? nextAttemptAt,
        DateTimeOffset? completedAt,
        CancellationToken cancellationToken = default);

    Task MarkFailureAsync(
        string domain,
        Guid mailingId,
        string msgType,
        DateTimeOffset firstSendAt,
        DateTimeOffset lastSendAt,
        DateTimeOffset failedAt,
        string errorCode,
        string errorSummary,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken = default);
}

public sealed class EfMailruPostmasterMailingStorage(MailruPostmasterDbContext db) : IMailruPostmasterMailingStorage
{
    public async Task UpsertDailyMetricsAsync(
        Guid mailingId,
        string domain,
        string msgType,
        IReadOnlyCollection<MailruPostmasterDailyStatistics> metrics,
        DateTimeOffset collectedAt,
        CancellationToken cancellationToken = default)
    {
        EnsureMailingId(mailingId);
        var normalizedDomain = NormalizeDomain(domain);
        var normalizedMsgType = NormalizeMsgType(msgType);

        foreach (var metric in metrics
                     .Where(x => string.Equals(NormalizeDomain(x.Domain), normalizedDomain, StringComparison.Ordinal))
                     .GroupBy(x => x.Date)
                     .Select(x => x.Last()))
        {
            var entity = await db.MailingDailyMetrics.SingleOrDefaultAsync(
                x => x.Domain == normalizedDomain && x.MailingId == mailingId && x.Date == metric.Date,
                cancellationToken);

            if (entity is null)
            {
                entity = new MailruPostmasterMailingDailyMetricEntity
                {
                    Id = Guid.NewGuid(),
                    MailingId = mailingId,
                    Domain = normalizedDomain,
                    Date = metric.Date,
                    CollectedAt = collectedAt
                };
                db.MailingDailyMetrics.Add(entity);
            }

            entity.MsgType = normalizedMsgType;
            entity.MessagesSent = metric.MessagesSent;
            entity.Delivered = metric.Delivered;
            entity.ProbablySpam = metric.ProbablySpam;
            entity.Spam = metric.Spam;
            entity.Complaints = metric.Complaints;
            entity.Read = metric.Read;
            entity.DeletedRead = metric.DeletedRead;
            entity.DeletedUnread = metric.DeletedUnread;
            entity.SpamPercent = metric.SpamPercent;
            entity.ProbablySpamPercent = metric.ProbablySpamPercent;
            entity.Reputation = metric.Reputation;
            entity.Trend = metric.Trend;
            entity.UpdatedAt = collectedAt;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<MailruPostmasterMailingSyncStateEntity?> GetSyncStateAsync(
        string domain,
        Guid mailingId,
        CancellationToken cancellationToken = default)
    {
        EnsureMailingId(mailingId);
        var normalizedDomain = NormalizeDomain(domain);
        return db.MailingSyncStates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Domain == normalizedDomain && x.MailingId == mailingId,
                cancellationToken);
    }

    public async Task MarkAttemptAsync(
        string domain,
        Guid mailingId,
        string msgType,
        DateTimeOffset firstSendAt,
        DateTimeOffset lastSendAt,
        DateTimeOffset attemptedAt,
        CancellationToken cancellationToken = default)
    {
        var state = await GetOrCreateStateAsync(
            domain,
            mailingId,
            msgType,
            firstSendAt,
            lastSendAt,
            attemptedAt,
            cancellationToken);
        state.LastAttemptAt = attemptedAt;
        state.UpdatedAt = attemptedAt;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkSuccessAsync(
        string domain,
        Guid mailingId,
        string msgType,
        DateTimeOffset firstSendAt,
        DateTimeOffset lastSendAt,
        DateTimeOffset succeededAt,
        DateOnly dateTo,
        string? dataFingerprint,
        int unchangedSuccessCount,
        bool hasData,
        bool isStable,
        DateTimeOffset? nextAttemptAt,
        DateTimeOffset? completedAt,
        CancellationToken cancellationToken = default)
    {
        var state = await GetOrCreateStateAsync(
            domain,
            mailingId,
            msgType,
            firstSendAt,
            lastSendAt,
            succeededAt,
            cancellationToken);
        state.LastAttemptAt = succeededAt;
        state.LastSuccessAt = succeededAt;
        state.LastErrorCode = null;
        state.LastErrorSummary = null;
        state.ConsecutiveFailures = 0;
        state.LastDateTo = dateTo;
        state.LastDataFingerprint = NormalizeFingerprint(dataFingerprint);
        state.UnchangedSuccessCount = Math.Max(0, unchangedSuccessCount);
        state.HasData = hasData;
        state.IsStable = isStable;
        state.NextAttemptAt = nextAttemptAt;
        state.CompletedAt = completedAt;
        state.UpdatedAt = succeededAt;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailureAsync(
        string domain,
        Guid mailingId,
        string msgType,
        DateTimeOffset firstSendAt,
        DateTimeOffset lastSendAt,
        DateTimeOffset failedAt,
        string errorCode,
        string errorSummary,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken = default)
    {
        var state = await GetOrCreateStateAsync(
            domain,
            mailingId,
            msgType,
            firstSendAt,
            lastSendAt,
            failedAt,
            cancellationToken);
        state.LastAttemptAt = failedAt;
        state.LastErrorCode = Truncate(errorCode, 120);
        state.LastErrorSummary = Truncate(errorSummary, 1000);
        state.ConsecutiveFailures++;
        state.NextAttemptAt = nextAttemptAt;
        state.UpdatedAt = failedAt;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<MailruPostmasterMailingSyncStateEntity> GetOrCreateStateAsync(
        string domain,
        Guid mailingId,
        string msgType,
        DateTimeOffset firstSendAt,
        DateTimeOffset lastSendAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        EnsureMailingId(mailingId);
        var normalizedDomain = NormalizeDomain(domain);
        var normalizedMsgType = NormalizeMsgType(msgType);
        var normalizedFirstSendAt = firstSendAt <= lastSendAt ? firstSendAt : lastSendAt;
        var normalizedLastSendAt = lastSendAt >= firstSendAt ? lastSendAt : firstSendAt;
        var state = await db.MailingSyncStates.SingleOrDefaultAsync(
            x => x.Domain == normalizedDomain && x.MailingId == mailingId,
            cancellationToken);

        if (state is null)
        {
            state = new MailruPostmasterMailingSyncStateEntity
            {
                Domain = normalizedDomain,
                MailingId = mailingId,
                MsgType = normalizedMsgType,
                FirstSendAt = normalizedFirstSendAt,
                LastSendAt = normalizedLastSendAt,
                UpdatedAt = now
            };
            db.MailingSyncStates.Add(state);
            return state;
        }

        state.MsgType = normalizedMsgType;
        if (normalizedFirstSendAt < state.FirstSendAt)
        {
            state.FirstSendAt = normalizedFirstSendAt;
        }

        if (normalizedLastSendAt > state.LastSendAt)
        {
            state.LastSendAt = normalizedLastSendAt;
        }

        return state;
    }

    private static void EnsureMailingId(Guid mailingId)
    {
        if (mailingId == Guid.Empty)
        {
            throw new ArgumentException("Идентификатор рассылки не задан.", nameof(mailingId));
        }
    }

    private static string NormalizeDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new ArgumentException("Домен Mail.ru Postmaster не задан.", nameof(domain));
        }

        return domain.Trim().TrimEnd('.').ToLowerInvariant();
    }

    private static string NormalizeMsgType(string msgType)
    {
        var normalized = msgType?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 32 || normalized.Any(ch => !char.IsAsciiLetterOrDigit(ch)))
        {
            throw new ArgumentException("msgtype Mail.ru должен содержать от 1 до 32 ASCII-букв или цифр.", nameof(msgType));
        }

        return normalized;
    }

    private static string? NormalizeFingerprint(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Length != 64 || normalized.Any(ch => !char.IsAsciiHexDigit(ch)))
        {
            throw new ArgumentException("Fingerprint метрик должен быть SHA-256 в hex-формате.", nameof(value));
        }

        return normalized;
    }

    private static string Truncate(string? value, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
