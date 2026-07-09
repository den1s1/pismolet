using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pismolet.Web.Infrastructure.Postmaster;

public sealed record MailruPostmasterSyncOptions(
    int SyncHourMoscow,
    int BackfillDays,
    int ResyncRecentDays)
{
    public const int MinSyncHourMoscow = 0;
    public const int MaxSyncHourMoscow = 23;
    public const int MinBackfillDays = 1;
    public const int MaxBackfillDays = 365;
    public const int MinResyncRecentDays = 1;
    public const int MaxResyncRecentDays = 30;

    public static MailruPostmasterSyncOptions Default => new(
        SyncHourMoscow: 6,
        BackfillDays: 30,
        ResyncRecentDays: 3);

    public static MailruPostmasterSyncOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var fallback = Default;
        return new MailruPostmasterSyncOptions(
            SyncHourMoscow: ReadInt(
                configuration,
                "MailruPostmaster:SyncHourMoscow",
                fallback.SyncHourMoscow,
                MinSyncHourMoscow,
                MaxSyncHourMoscow),
            BackfillDays: ReadInt(
                configuration,
                "MailruPostmaster:BackfillDays",
                fallback.BackfillDays,
                MinBackfillDays,
                MaxBackfillDays),
            ResyncRecentDays: ReadInt(
                configuration,
                "MailruPostmaster:ResyncRecentDays",
                fallback.ResyncRecentDays,
                MinResyncRecentDays,
                MaxResyncRecentDays));
    }

    private static int ReadInt(
        IConfiguration configuration,
        string key,
        int fallback,
        int min,
        int max)
    {
        var value = configuration[key]
            ?? configuration[key.Replace(":", "__", StringComparison.Ordinal)]
            ?? Environment.GetEnvironmentVariable(key.Replace(":", "__", StringComparison.Ordinal));

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;
    }
}

public static class MailruPostmasterSyncSchedule
{
    private static readonly TimeZoneInfo MoscowTimeZone = ResolveMoscowTimeZone();

    public static DateOnly GetMoscowDate(DateTimeOffset utcNow)
    {
        var moscowNow = TimeZoneInfo.ConvertTime(utcNow, MoscowTimeZone);
        return DateOnly.FromDateTime(moscowNow.Date);
    }

    public static DateTimeOffset GetNextRunAtUtc(DateTimeOffset utcNow, int syncHourMoscow)
    {
        var hour = Math.Clamp(
            syncHourMoscow,
            MailruPostmasterSyncOptions.MinSyncHourMoscow,
            MailruPostmasterSyncOptions.MaxSyncHourMoscow);
        var moscowNow = TimeZoneInfo.ConvertTime(utcNow, MoscowTimeZone);
        var nextMoscowLocal = DateTime.SpecifyKind(
            moscowNow.Date.AddHours(hour),
            DateTimeKind.Unspecified);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(nextMoscowLocal, MoscowTimeZone);

        if (nextUtc <= utcNow.UtcDateTime)
        {
            nextMoscowLocal = nextMoscowLocal.AddDays(1);
            nextUtc = TimeZoneInfo.ConvertTimeToUtc(nextMoscowLocal, MoscowTimeZone);
        }

        return new DateTimeOffset(nextUtc, TimeSpan.Zero);
    }

    private static TimeZoneInfo ResolveMoscowTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        }
    }
}

public sealed record MailruPostmasterSyncDateRange(
    DateOnly DateFrom,
    DateOnly DateTo,
    bool IsBackfill);

public enum MailruPostmasterSyncRunStatus
{
    Disabled,
    NotConfigured,
    SkippedAlreadyRunning,
    Succeeded,
    Failed
}

public sealed record MailruPostmasterSyncRunResult(
    MailruPostmasterSyncRunStatus Status,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    int MetricDays,
    int TroubleCount,
    string? ErrorCode)
{
    public static MailruPostmasterSyncRunResult Disabled() =>
        new(MailruPostmasterSyncRunStatus.Disabled, null, null, 0, 0, null);

    public static MailruPostmasterSyncRunResult NotConfigured() =>
        new(MailruPostmasterSyncRunStatus.NotConfigured, null, null, 0, 0, "integration_not_configured");

    public static MailruPostmasterSyncRunResult SkippedAlreadyRunning() =>
        new(MailruPostmasterSyncRunStatus.SkippedAlreadyRunning, null, null, 0, 0, null);

    public static MailruPostmasterSyncRunResult Succeeded(
        MailruPostmasterSyncDateRange range,
        int metricDays,
        int troubleCount) =>
        new(
            MailruPostmasterSyncRunStatus.Succeeded,
            range.DateFrom,
            range.DateTo,
            metricDays,
            troubleCount,
            null);

    public static MailruPostmasterSyncRunResult Failed(
        MailruPostmasterSyncDateRange? range,
        string errorCode) =>
        new(
            MailruPostmasterSyncRunStatus.Failed,
            range?.DateFrom,
            range?.DateTo,
            0,
            0,
            errorCode);
}

public interface IMailruPostmasterSynchronizer
{
    Task<MailruPostmasterSyncRunResult> RunOnceAsync(CancellationToken cancellationToken = default);
}

public sealed class MailruPostmasterSynchronizer(
    IMailruPostmasterClient client,
    IServiceScopeFactory scopeFactory,
    MailruPostmasterOptions integrationOptions,
    MailruPostmasterSyncOptions syncOptions,
    TimeProvider timeProvider,
    ILogger<MailruPostmasterSynchronizer> logger) : IMailruPostmasterSynchronizer
{
    private readonly SemaphoreSlim runLock = new(1, 1);

    public async Task<MailruPostmasterSyncRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!integrationOptions.Enabled)
        {
            return MailruPostmasterSyncRunResult.Disabled();
        }

        if (!integrationOptions.IsConfigured)
        {
            logger.LogWarning(
                "Mail.ru Postmaster synchronization skipped because integration is not configured. domain={Domain}",
                integrationOptions.Domain);
            return MailruPostmasterSyncRunResult.NotConfigured();
        }

        if (!await runLock.WaitAsync(0, cancellationToken))
        {
            logger.LogInformation(
                "Mail.ru Postmaster synchronization skipped because another run is active. domain={Domain}",
                integrationOptions.Domain);
            return MailruPostmasterSyncRunResult.SkippedAlreadyRunning();
        }

        try
        {
            return await RunCoreAsync(cancellationToken);
        }
        finally
        {
            runLock.Release();
        }
    }

    public static MailruPostmasterSyncDateRange CalculateDateRange(
        DateTimeOffset nowUtc,
        DateOnly? lastDomainDate,
        MailruPostmasterSyncOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var moscowToday = MailruPostmasterSyncSchedule.GetMoscowDate(nowUtc);
        var dateTo = moscowToday.AddDays(-1);

        if (lastDomainDate is null)
        {
            return new MailruPostmasterSyncDateRange(
                dateTo.AddDays(-(options.BackfillDays - 1)),
                dateTo,
                IsBackfill: true);
        }

        var nextUnsynchronizedDate = lastDomainDate.Value.AddDays(1);
        var recentRangeStart = dateTo.AddDays(-(options.ResyncRecentDays - 1));
        var dateFrom = nextUnsynchronizedDate < recentRangeStart
            ? nextUnsynchronizedDate
            : recentRangeStart;

        return new MailruPostmasterSyncDateRange(dateFrom, dateTo, IsBackfill: false);
    }

    private async Task<MailruPostmasterSyncRunResult> RunCoreAsync(CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        var domain = NormalizeDomain(integrationOptions.Domain);
        var stage = "storage_initialization";
        MailruPostmasterSyncDateRange? range = null;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<IMailruPostmasterStorage>();

            var state = await storage.GetSyncStateAsync(domain, cancellationToken);
            range = CalculateDateRange(startedAt, state?.LastDomainDate, syncOptions);
            await storage.MarkAttemptAsync(domain, startedAt, cancellationToken);

            logger.LogInformation(
                "Mail.ru Postmaster synchronization started. domain={Domain} dateFrom={DateFrom} dateTo={DateTo} isBackfill={IsBackfill}",
                domain,
                range.DateFrom,
                range.DateTo,
                range.IsBackfill);

            stage = "registered_domains";
            var registeredDomainsResult = await client.GetRegisteredDomainsAsync(cancellationToken);
            if (!registeredDomainsResult.IsSuccess)
            {
                return await FailApiStageAsync(
                    storage,
                    domain,
                    stage,
                    registeredDomainsResult.ErrorCode,
                    registeredDomainsResult.ErrorMessage,
                    range,
                    cancellationToken);
            }

            var isDomainRegistered = registeredDomainsResult.Data?.Any(x =>
                string.Equals(NormalizeDomain(x.Domain), domain, StringComparison.Ordinal)) == true;
            if (!isDomainRegistered)
            {
                return await FailApiStageAsync(
                    storage,
                    domain,
                    stage,
                    "domain_not_registered",
                    "Настроенный домен отсутствует в списке подтверждённых доменов Mail.ru Postmaster.",
                    range,
                    cancellationToken);
            }

            stage = "troubles";
            var troublesResult = await client.GetTroublesAsync(cancellationToken);
            if (!troublesResult.IsSuccess)
            {
                return await FailApiStageAsync(
                    storage,
                    domain,
                    stage,
                    troublesResult.ErrorCode,
                    troublesResult.ErrorMessage,
                    range,
                    cancellationToken);
            }

            stage = "detailed_statistics";
            var statisticsResult = await client.GetDetailedStatisticsAsync(
                range.DateFrom,
                range.DateTo,
                domain,
                cancellationToken: cancellationToken);
            if (!statisticsResult.IsSuccess)
            {
                return await FailApiStageAsync(
                    storage,
                    domain,
                    stage,
                    statisticsResult.ErrorCode,
                    statisticsResult.ErrorMessage,
                    range,
                    cancellationToken);
            }

            var observedAt = timeProvider.GetUtcNow();
            var troubles = (troublesResult.Data ?? [])
                .Where(x => string.Equals(NormalizeDomain(x.Domain), domain, StringComparison.Ordinal))
                .ToArray();
            var metrics = (statisticsResult.Data ?? [])
                .Where(x =>
                    string.Equals(NormalizeDomain(x.Domain), domain, StringComparison.Ordinal) &&
                    x.Date >= range.DateFrom &&
                    x.Date <= range.DateTo)
                .ToArray();

            stage = "trouble_storage";
            await storage.SynchronizeTroublesAsync(domain, troubles, observedAt, cancellationToken);

            stage = "metric_storage";
            await storage.UpsertDomainDailyMetricsAsync(metrics, observedAt, cancellationToken);

            stage = "sync_state_storage";
            await storage.MarkSuccessAsync(domain, observedAt, range.DateTo, cancellationToken);

            stopwatch.Stop();
            logger.LogInformation(
                "Mail.ru Postmaster synchronization completed. domain={Domain} dateFrom={DateFrom} dateTo={DateTo} metricDays={MetricDays} troubleCount={TroubleCount} durationMs={DurationMs}",
                domain,
                range.DateFrom,
                range.DateTo,
                metrics.Length,
                troubles.Length,
                stopwatch.ElapsedMilliseconds);

            return MailruPostmasterSyncRunResult.Succeeded(range, metrics.Length, troubles.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(
                ex,
                "Mail.ru Postmaster synchronization failed without affecting the application host. domain={Domain} stage={Stage} dateFrom={DateFrom} dateTo={DateTo} durationMs={DurationMs}",
                domain,
                stage,
                range?.DateFrom,
                range?.DateTo,
                stopwatch.ElapsedMilliseconds);

            return MailruPostmasterSyncRunResult.Failed(
                range,
                stage.Contains("storage", StringComparison.Ordinal)
                    ? "postmaster_storage_error"
                    : "unexpected_sync_error");
        }
    }

    private async Task<MailruPostmasterSyncRunResult> FailApiStageAsync(
        IMailruPostmasterStorage storage,
        string domain,
        string stage,
        string? errorCode,
        string? errorMessage,
        MailruPostmasterSyncDateRange range,
        CancellationToken cancellationToken)
    {
        var normalizedErrorCode = string.IsNullOrWhiteSpace(errorCode)
            ? "postmaster_api_error"
            : errorCode.Trim();
        var normalizedErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
            ? "Ошибка Mail.ru Postmaster API."
            : errorMessage.Trim();
        var failedAt = timeProvider.GetUtcNow();

        logger.LogWarning(
            "Mail.ru Postmaster synchronization failed. domain={Domain} stage={Stage} errorCode={ErrorCode} dateFrom={DateFrom} dateTo={DateTo}",
            domain,
            stage,
            normalizedErrorCode,
            range.DateFrom,
            range.DateTo);

        try
        {
            await storage.MarkFailureAsync(
                domain,
                failedAt,
                normalizedErrorCode,
                normalizedErrorMessage,
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
                "Mail.ru Postmaster failed to persist synchronization error without affecting the application host. domain={Domain} stage={Stage} errorCode={ErrorCode}",
                domain,
                stage,
                normalizedErrorCode);
        }

        return MailruPostmasterSyncRunResult.Failed(range, normalizedErrorCode);
    }

    private static string NormalizeDomain(string domain) =>
        domain.Trim().TrimEnd('.').ToLowerInvariant();
}

public sealed class MailruPostmasterSyncHostedService(
    IMailruPostmasterSynchronizer synchronizer,
    MailruPostmasterOptions integrationOptions,
    MailruPostmasterSyncOptions syncOptions,
    TimeProvider timeProvider,
    ILogger<MailruPostmasterSyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!integrationOptions.Enabled)
        {
            logger.LogInformation("Mail.ru Postmaster background synchronization is disabled.");
            return;
        }

        if (!integrationOptions.IsConfigured)
        {
            logger.LogWarning(
                "Mail.ru Postmaster background synchronization is not configured. domain={Domain}",
                integrationOptions.Domain);
            return;
        }

        logger.LogInformation(
            "Mail.ru Postmaster background synchronization started. domain={Domain} syncHourMoscow={SyncHourMoscow} backfillDays={BackfillDays} resyncRecentDays={ResyncRecentDays}",
            integrationOptions.Domain,
            syncOptions.SyncHourMoscow,
            syncOptions.BackfillDays,
            syncOptions.ResyncRecentDays);

        if (!await RunOnceSafelyAsync(stoppingToken))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();
            var nextRunAt = MailruPostmasterSyncSchedule.GetNextRunAtUtc(now, syncOptions.SyncHourMoscow);
            var delay = nextRunAt - now;

            logger.LogInformation(
                "Mail.ru Postmaster next synchronization scheduled. domain={Domain} nextRunAtUtc={NextRunAtUtc} delaySeconds={DelaySeconds}",
                integrationOptions.Domain,
                nextRunAt,
                Math.Max(0, delay.TotalSeconds));

            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (!await RunOnceSafelyAsync(stoppingToken))
            {
                return;
            }
        }
    }

    private async Task<bool> RunOnceSafelyAsync(CancellationToken stoppingToken)
    {
        try
        {
            await synchronizer.RunOnceAsync(stoppingToken);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Mail.ru Postmaster background synchronization iteration failed without affecting the application host. domain={Domain}",
                integrationOptions.Domain);
            return true;
        }
    }
}
