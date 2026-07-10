using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Pismolet.Web.Infrastructure.Postmaster;

public static class MailruPostmasterSyncTriggers
{
    public const string Scheduled = "scheduled";
    public const string Manual = "manual";
}

public sealed record MailruPostmasterManualSyncOptions(int CooldownSeconds)
{
    public const int MinCooldownSeconds = 10;
    public const int MaxCooldownSeconds = 3600;
    public const int DefaultCooldownSeconds = 60;

    public static MailruPostmasterManualSyncOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var raw = configuration["MailruPostmaster:ManualSyncCooldownSeconds"]
            ?? configuration["MailruPostmaster__ManualSyncCooldownSeconds"]
            ?? Environment.GetEnvironmentVariable("MailruPostmaster__ManualSyncCooldownSeconds");
        var value = int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, MinCooldownSeconds, MaxCooldownSeconds)
            : DefaultCooldownSeconds;
        return new MailruPostmasterManualSyncOptions(value);
    }
}

public sealed record MailruPostmasterSyncRunCompletion(
    string Status,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    int MetricDays,
    int TroubleCount,
    string? ErrorCode)
{
    public static MailruPostmasterSyncRunCompletion FromResult(MailruPostmasterSyncRunResult result) =>
        new(
            result.Status switch
            {
                MailruPostmasterSyncRunStatus.Disabled => "disabled",
                MailruPostmasterSyncRunStatus.NotConfigured => "not_configured",
                MailruPostmasterSyncRunStatus.SkippedAlreadyRunning => "skipped_already_running",
                MailruPostmasterSyncRunStatus.Succeeded => "succeeded",
                MailruPostmasterSyncRunStatus.Failed => "failed",
                _ => "unknown"
            },
            result.DateFrom,
            result.DateTo,
            result.MetricDays,
            result.TroubleCount,
            result.ErrorCode);

    public static MailruPostmasterSyncRunCompletion Cancelled() =>
        new("cancelled", null, null, 0, 0, "operation_cancelled");

    public static MailruPostmasterSyncRunCompletion UnexpectedFailure() =>
        new("failed", null, null, 0, 0, "unexpected_sync_error");
}

public interface IMailruPostmasterSyncRunJournal
{
    Task<Guid> StartAsync(
        string domain,
        string trigger,
        string? requestedBy,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        Guid runId,
        MailruPostmasterSyncRunCompletion completion,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    Task<DateTimeOffset?> GetLatestStartedAtAsync(
        string domain,
        string trigger,
        CancellationToken cancellationToken = default);
}

public sealed class EfMailruPostmasterSyncRunJournal(IServiceScopeFactory scopeFactory) : IMailruPostmasterSyncRunJournal
{
    public async Task<Guid> StartAsync(
        string domain,
        string trigger,
        string? requestedBy,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MailruPostmasterDbContext>();
        var entity = new MailruPostmasterSyncRunEntity
        {
            Id = Guid.NewGuid(),
            Domain = NormalizeDomain(domain),
            Trigger = NormalizeRequired(trigger, 24),
            RequestedBy = NormalizeOptional(requestedBy, 320),
            StartedAt = startedAt,
            Status = "running"
        };
        db.SyncRuns.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task CompleteAsync(
        Guid runId,
        MailruPostmasterSyncRunCompletion completion,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MailruPostmasterDbContext>();
        var entity = await db.SyncRuns.SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.CompletedAt = completedAt;
        entity.DurationMs = Math.Max(0L, (long)(completedAt - entity.StartedAt).TotalMilliseconds);
        entity.Status = NormalizeRequired(completion.Status, 40);
        entity.DateFrom = completion.DateFrom;
        entity.DateTo = completion.DateTo;
        entity.MetricDays = completion.MetricDays;
        entity.TroubleCount = completion.TroubleCount;
        entity.ErrorCode = NormalizeOptional(completion.ErrorCode, 120);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<DateTimeOffset?> GetLatestStartedAtAsync(
        string domain,
        string trigger,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MailruPostmasterDbContext>();
        var normalizedDomain = NormalizeDomain(domain);
        var normalizedTrigger = NormalizeRequired(trigger, 24);
        var startedAtValues = await db.SyncRuns
            .AsNoTracking()
            .Where(x => x.Domain == normalizedDomain && x.Trigger == normalizedTrigger)
            .Select(x => x.StartedAt)
            .ToListAsync(cancellationToken);
        return startedAtValues.Count == 0 ? null : startedAtValues.Max();
    }

    private static string NormalizeDomain(string value) => NormalizeRequired(value.Trim().TrimEnd('.').ToLowerInvariant(), 253);

    private static string NormalizeRequired(string? value, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Обязательное значение журнала Postmaster не задано.", nameof(value));
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}

public sealed class InMemoryMailruPostmasterSyncRunJournal : IMailruPostmasterSyncRunJournal
{
    private readonly Lock sync = new();
    private readonly Dictionary<Guid, MailruPostmasterSyncRunEntity> runs = [];

    public Task<Guid> StartAsync(
        string domain,
        string trigger,
        string? requestedBy,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entity = new MailruPostmasterSyncRunEntity
        {
            Id = Guid.NewGuid(),
            Domain = domain.Trim().TrimEnd('.').ToLowerInvariant(),
            Trigger = trigger.Trim(),
            RequestedBy = requestedBy?.Trim(),
            StartedAt = startedAt,
            Status = "running"
        };
        lock (sync)
        {
            runs[entity.Id] = entity;
        }

        return Task.FromResult(entity.Id);
    }

    public Task CompleteAsync(
        Guid runId,
        MailruPostmasterSyncRunCompletion completion,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (runs.TryGetValue(runId, out var entity))
            {
                entity.CompletedAt = completedAt;
                entity.DurationMs = Math.Max(0L, (long)(completedAt - entity.StartedAt).TotalMilliseconds);
                entity.Status = completion.Status;
                entity.DateFrom = completion.DateFrom;
                entity.DateTo = completion.DateTo;
                entity.MetricDays = completion.MetricDays;
                entity.TroubleCount = completion.TroubleCount;
                entity.ErrorCode = completion.ErrorCode;
            }
        }

        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> GetLatestStartedAtAsync(
        string domain,
        string trigger,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedDomain = domain.Trim().TrimEnd('.').ToLowerInvariant();
        DateTimeOffset? value;
        lock (sync)
        {
            value = runs.Values
                .Where(x => x.Domain == normalizedDomain && x.Trigger == trigger)
                .OrderByDescending(x => x.StartedAt)
                .Select(x => (DateTimeOffset?)x.StartedAt)
                .FirstOrDefault();
        }

        return Task.FromResult(value);
    }

    public IReadOnlyList<MailruPostmasterSyncRunEntity> Snapshot()
    {
        lock (sync)
        {
            return runs.Values
                .OrderByDescending(x => x.StartedAt)
                .Select(Clone)
                .ToArray();
        }
    }

    private static MailruPostmasterSyncRunEntity Clone(MailruPostmasterSyncRunEntity source) => new()
    {
        Id = source.Id,
        Domain = source.Domain,
        Trigger = source.Trigger,
        RequestedBy = source.RequestedBy,
        StartedAt = source.StartedAt,
        CompletedAt = source.CompletedAt,
        DurationMs = source.DurationMs,
        Status = source.Status,
        DateFrom = source.DateFrom,
        DateTo = source.DateTo,
        MetricDays = source.MetricDays,
        TroubleCount = source.TroubleCount,
        ErrorCode = source.ErrorCode
    };
}

public interface IMailruPostmasterSyncExecutor
{
    Task<MailruPostmasterSyncRunResult> RunAsync(
        string trigger,
        string? requestedBy = null,
        CancellationToken cancellationToken = default);
}

public sealed class MailruPostmasterSyncExecutor(
    IMailruPostmasterSynchronizer synchronizer,
    IMailruPostmasterSyncRunJournal journal,
    MailruPostmasterOptions integrationOptions,
    TimeProvider timeProvider,
    ILogger<MailruPostmasterSyncExecutor> logger) : IMailruPostmasterSyncExecutor
{
    public async Task<MailruPostmasterSyncRunResult> RunAsync(
        string trigger,
        string? requestedBy = null,
        CancellationToken cancellationToken = default)
    {
        var startedAt = timeProvider.GetUtcNow();
        var runId = await TryStartJournalAsync(trigger, requestedBy, startedAt, cancellationToken);

        try
        {
            var result = await synchronizer.RunOnceAsync(cancellationToken);
            await TryCompleteJournalAsync(
                runId,
                MailruPostmasterSyncRunCompletion.FromResult(result),
                timeProvider.GetUtcNow());
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryCompleteJournalAsync(
                runId,
                MailruPostmasterSyncRunCompletion.Cancelled(),
                timeProvider.GetUtcNow());
            throw;
        }
        catch
        {
            await TryCompleteJournalAsync(
                runId,
                MailruPostmasterSyncRunCompletion.UnexpectedFailure(),
                timeProvider.GetUtcNow());
            throw;
        }
    }

    private async Task<Guid?> TryStartJournalAsync(
        string trigger,
        string? requestedBy,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            return await journal.StartAsync(
                integrationOptions.Domain,
                trigger,
                requestedBy,
                startedAt,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Mail.ru Postmaster failed to start sync journal entry without affecting synchronization. domain={Domain} trigger={Trigger}",
                integrationOptions.Domain,
                trigger);
            return null;
        }
    }

    private async Task TryCompleteJournalAsync(
        Guid? runId,
        MailruPostmasterSyncRunCompletion completion,
        DateTimeOffset completedAt)
    {
        if (runId is null)
        {
            return;
        }

        try
        {
            await journal.CompleteAsync(runId.Value, completion, completedAt, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Mail.ru Postmaster failed to complete sync journal entry without affecting synchronization. domain={Domain} runId={RunId}",
                integrationOptions.Domain,
                runId);
        }
    }
}

public enum MailruPostmasterManualSyncStatus
{
    Succeeded,
    Failed,
    RateLimited,
    AlreadyRunning,
    Disabled,
    NotConfigured
}

public sealed record MailruPostmasterManualSyncResult(
    MailruPostmasterManualSyncStatus Status,
    int? RetryAfterSeconds,
    MailruPostmasterSyncRunResult? SyncResult);

public interface IMailruPostmasterManualSyncService
{
    Task<MailruPostmasterManualSyncResult> RunAsync(
        string? requestedBy,
        CancellationToken cancellationToken = default);
}

public sealed class MailruPostmasterManualSyncService(
    IMailruPostmasterSyncExecutor executor,
    IMailruPostmasterSyncRunJournal journal,
    MailruPostmasterOptions integrationOptions,
    MailruPostmasterManualSyncOptions options,
    TimeProvider timeProvider,
    ILogger<MailruPostmasterManualSyncService> logger) : IMailruPostmasterManualSyncService
{
    private readonly SemaphoreSlim manualGate = new(1, 1);
    private DateTimeOffset? lastAcceptedAt;

    public async Task<MailruPostmasterManualSyncResult> RunAsync(
        string? requestedBy,
        CancellationToken cancellationToken = default)
    {
        if (!integrationOptions.Enabled)
        {
            return new(MailruPostmasterManualSyncStatus.Disabled, null, null);
        }

        if (!integrationOptions.IsConfigured)
        {
            return new(MailruPostmasterManualSyncStatus.NotConfigured, null, null);
        }

        if (!await manualGate.WaitAsync(0, cancellationToken))
        {
            return new(MailruPostmasterManualSyncStatus.AlreadyRunning, null, null);
        }

        try
        {
            var now = timeProvider.GetUtcNow();
            var latestPersistent = await TryGetLatestManualStartedAtAsync(cancellationToken);
            var latest = Max(lastAcceptedAt, latestPersistent);
            if (latest is not null)
            {
                var remaining = TimeSpan.FromSeconds(options.CooldownSeconds) - (now - latest.Value);
                if (remaining > TimeSpan.Zero)
                {
                    return new(
                        MailruPostmasterManualSyncStatus.RateLimited,
                        Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds)),
                        null);
                }
            }

            lastAcceptedAt = now;
            var result = await executor.RunAsync(
                MailruPostmasterSyncTriggers.Manual,
                requestedBy,
                cancellationToken);
            return new(
                result.Status switch
                {
                    MailruPostmasterSyncRunStatus.Succeeded => MailruPostmasterManualSyncStatus.Succeeded,
                    MailruPostmasterSyncRunStatus.Disabled => MailruPostmasterManualSyncStatus.Disabled,
                    MailruPostmasterSyncRunStatus.NotConfigured => MailruPostmasterManualSyncStatus.NotConfigured,
                    MailruPostmasterSyncRunStatus.SkippedAlreadyRunning => MailruPostmasterManualSyncStatus.AlreadyRunning,
                    _ => MailruPostmasterManualSyncStatus.Failed
                },
                null,
                result);
        }
        finally
        {
            manualGate.Release();
        }
    }

    private async Task<DateTimeOffset?> TryGetLatestManualStartedAtAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await journal.GetLatestStartedAtAsync(
                integrationOptions.Domain,
                MailruPostmasterSyncTriggers.Manual,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Mail.ru Postmaster failed to read manual sync cooldown from journal. domain={Domain}",
                integrationOptions.Domain);
            return null;
        }
    }

    private static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return left >= right ? left : right;
    }
}
