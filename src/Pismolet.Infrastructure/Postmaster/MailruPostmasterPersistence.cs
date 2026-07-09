using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pismolet.Web.Infrastructure.Postmaster;

public sealed class MailruPostmasterDbContext(DbContextOptions<MailruPostmasterDbContext> options) : DbContext(options)
{
    public DbSet<MailruPostmasterDomainDailyMetricEntity> DomainDailyMetrics => Set<MailruPostmasterDomainDailyMetricEntity>();
    public DbSet<MailruPostmasterTroubleSnapshotEntity> TroubleSnapshots => Set<MailruPostmasterTroubleSnapshotEntity>();
    public DbSet<MailruPostmasterSyncStateEntity> SyncStates => Set<MailruPostmasterSyncStateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MailruPostmasterDomainDailyMetricEntity>(entity =>
        {
            entity.ToTable("mailru_postmaster_domain_daily_metrics");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Domain, x.Date }).IsUnique();
            entity.HasIndex(x => x.Date);
            entity.Property(x => x.Domain).HasMaxLength(253).IsRequired();
            entity.Property(x => x.Date).HasColumnType("date").IsRequired();
        });

        modelBuilder.Entity<MailruPostmasterTroubleSnapshotEntity>(entity =>
        {
            entity.ToTable("mailru_postmaster_trouble_snapshots");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Domain, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.Domain, x.IsActive });
            entity.Property(x => x.Domain).HasMaxLength(253).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        });

        modelBuilder.Entity<MailruPostmasterSyncStateEntity>(entity =>
        {
            entity.ToTable("mailru_postmaster_sync_states");
            entity.HasKey(x => x.Domain);
            entity.Property(x => x.Domain).HasMaxLength(253).IsRequired();
            entity.Property(x => x.LastErrorCode).HasMaxLength(120);
            entity.Property(x => x.LastErrorSummary).HasMaxLength(1000);
            entity.Property(x => x.LastDomainDate).HasColumnType("date");
        });
    }
}

public sealed class MailruPostmasterDbContextFactory : IDesignTimeDbContextFactory<MailruPostmasterDbContext>
{
    public MailruPostmasterDbContext CreateDbContext(string[] args)
    {
        var connectionString = ResolveConnectionString(
            Environment.GetEnvironmentVariable("ConnectionStrings__PismoletDb"),
            Environment.GetEnvironmentVariable("PISMOLET_CONNECTION_STRING"));

        var options = new DbContextOptionsBuilder<MailruPostmasterDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new MailruPostmasterDbContext(options);
    }

    public static string ResolveConnectionString(string? configuredConnectionString, string? legacyConnectionString)
    {
        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            return configuredConnectionString;
        }

        if (!string.IsNullOrWhiteSpace(legacyConnectionString))
        {
            return legacyConnectionString;
        }

        throw new InvalidOperationException(
            "Для design-time операций Mail.ru Postmaster задайте ConnectionStrings__PismoletDb.");
    }
}

public sealed class MailruPostmasterDomainDailyMetricEntity
{
    public Guid Id { get; set; }
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

public sealed class MailruPostmasterTroubleSnapshotEntity
{
    public Guid Id { get; set; }
    public string Domain { get; set; } = string.Empty;
    public int Code { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class MailruPostmasterSyncStateEntity
{
    public string Domain { get; set; } = string.Empty;
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorSummary { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateOnly? LastDomainDate { get; set; }
    public DateTimeOffset? LastMailingSyncAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public interface IMailruPostmasterStorage
{
    Task UpsertDomainDailyMetricsAsync(
        IReadOnlyCollection<MailruPostmasterDailyStatistics> metrics,
        DateTimeOffset collectedAt,
        CancellationToken cancellationToken = default);

    Task SynchronizeTroublesAsync(
        string domain,
        IReadOnlyCollection<MailruPostmasterTrouble> troubles,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);

    Task<MailruPostmasterSyncStateEntity?> GetSyncStateAsync(
        string domain,
        CancellationToken cancellationToken = default);

    Task MarkAttemptAsync(
        string domain,
        DateTimeOffset attemptedAt,
        CancellationToken cancellationToken = default);

    Task MarkSuccessAsync(
        string domain,
        DateTimeOffset succeededAt,
        DateOnly? lastDomainDate,
        CancellationToken cancellationToken = default);

    Task MarkFailureAsync(
        string domain,
        DateTimeOffset failedAt,
        string errorCode,
        string errorSummary,
        CancellationToken cancellationToken = default);
}

public sealed class EfMailruPostmasterStorage(MailruPostmasterDbContext db) : IMailruPostmasterStorage
{
    public async Task UpsertDomainDailyMetricsAsync(
        IReadOnlyCollection<MailruPostmasterDailyStatistics> metrics,
        DateTimeOffset collectedAt,
        CancellationToken cancellationToken = default)
    {
        foreach (var metric in metrics
                     .Where(x => !string.IsNullOrWhiteSpace(x.Domain))
                     .GroupBy(x => new { Domain = NormalizeDomain(x.Domain), x.Date })
                     .Select(x => x.Last()))
        {
            var domain = NormalizeDomain(metric.Domain);
            var entity = await db.DomainDailyMetrics.SingleOrDefaultAsync(
                x => x.Domain == domain && x.Date == metric.Date,
                cancellationToken);

            if (entity is null)
            {
                entity = new MailruPostmasterDomainDailyMetricEntity
                {
                    Id = Guid.NewGuid(),
                    Domain = domain,
                    Date = metric.Date,
                    CollectedAt = collectedAt
                };
                db.DomainDailyMetrics.Add(entity);
            }

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

    public async Task SynchronizeTroublesAsync(
        string domain,
        IReadOnlyCollection<MailruPostmasterTrouble> troubles,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        var normalizedDomain = NormalizeDomain(domain);
        var existing = await db.TroubleSnapshots
            .Where(x => x.Domain == normalizedDomain)
            .ToListAsync(cancellationToken);
        var byCode = existing.ToDictionary(x => x.Code);
        var incoming = troubles
            .Where(x => NormalizeDomain(x.Domain) == normalizedDomain)
            .GroupBy(x => x.Code)
            .Select(x => x.Last())
            .ToDictionary(x => x.Code);

        foreach (var trouble in incoming.Values)
        {
            if (!byCode.TryGetValue(trouble.Code, out var entity))
            {
                entity = new MailruPostmasterTroubleSnapshotEntity
                {
                    Id = Guid.NewGuid(),
                    Domain = normalizedDomain,
                    Code = trouble.Code,
                    FirstSeenAt = observedAt
                };
                db.TroubleSnapshots.Add(entity);
                byCode[trouble.Code] = entity;
            }

            entity.Message = Truncate(trouble.Message, 1000);
            entity.IsActive = true;
            entity.LastSeenAt = observedAt;
            entity.ResolvedAt = null;
        }

        foreach (var entity in existing.Where(x => x.IsActive && !incoming.ContainsKey(x.Code)))
        {
            entity.IsActive = false;
            entity.ResolvedAt = observedAt;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<MailruPostmasterSyncStateEntity?> GetSyncStateAsync(
        string domain,
        CancellationToken cancellationToken = default) =>
        db.SyncStates
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Domain == NormalizeDomain(domain), cancellationToken);

    public async Task MarkAttemptAsync(
        string domain,
        DateTimeOffset attemptedAt,
        CancellationToken cancellationToken = default)
    {
        var state = await GetOrCreateSyncStateAsync(domain, attemptedAt, cancellationToken);
        state.LastAttemptAt = attemptedAt;
        state.UpdatedAt = attemptedAt;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkSuccessAsync(
        string domain,
        DateTimeOffset succeededAt,
        DateOnly? lastDomainDate,
        CancellationToken cancellationToken = default)
    {
        var state = await GetOrCreateSyncStateAsync(domain, succeededAt, cancellationToken);
        state.LastAttemptAt = succeededAt;
        state.LastSuccessAt = succeededAt;
        state.LastErrorCode = null;
        state.LastErrorSummary = null;
        state.ConsecutiveFailures = 0;
        state.LastDomainDate = lastDomainDate;
        state.UpdatedAt = succeededAt;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailureAsync(
        string domain,
        DateTimeOffset failedAt,
        string errorCode,
        string errorSummary,
        CancellationToken cancellationToken = default)
    {
        var state = await GetOrCreateSyncStateAsync(domain, failedAt, cancellationToken);
        state.LastAttemptAt = failedAt;
        state.LastErrorCode = Truncate(errorCode, 120);
        state.LastErrorSummary = Truncate(errorSummary, 1000);
        state.ConsecutiveFailures++;
        state.UpdatedAt = failedAt;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<MailruPostmasterSyncStateEntity> GetOrCreateSyncStateAsync(
        string domain,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var normalizedDomain = NormalizeDomain(domain);
        var state = await db.SyncStates.SingleOrDefaultAsync(x => x.Domain == normalizedDomain, cancellationToken);
        if (state is not null)
        {
            return state;
        }

        state = new MailruPostmasterSyncStateEntity
        {
            Domain = normalizedDomain,
            UpdatedAt = now
        };
        db.SyncStates.Add(state);
        return state;
    }

    private static string NormalizeDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new ArgumentException("Домен Mail.ru Postmaster не задан.", nameof(domain));
        }

        return domain.Trim().TrimEnd('.').ToLowerInvariant();
    }

    private static string Truncate(string? value, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
