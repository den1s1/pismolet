using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Pismolet.Web.Infrastructure.Postmaster;

public enum MailruPostmasterMailingSyncBatchStatus
{
    Disabled,
    NotConfigured,
    Completed,
    Failed
}

public sealed record MailruPostmasterMailingSyncBatchResult(
    MailruPostmasterMailingSyncBatchStatus Status,
    int CandidateCount,
    int ProcessedCount,
    int UpdatedCount,
    int EmptyCount,
    int FailedCount,
    bool RateLimited)
{
    public static MailruPostmasterMailingSyncBatchResult Disabled() =>
        new(MailruPostmasterMailingSyncBatchStatus.Disabled, 0, 0, 0, 0, 0, false);

    public static MailruPostmasterMailingSyncBatchResult NotConfigured() =>
        new(MailruPostmasterMailingSyncBatchStatus.NotConfigured, 0, 0, 0, 0, 0, false);

    public static MailruPostmasterMailingSyncBatchResult Completed(
        int candidateCount,
        int processedCount,
        int updatedCount,
        int emptyCount,
        int failedCount,
        bool rateLimited) =>
        new(
            MailruPostmasterMailingSyncBatchStatus.Completed,
            candidateCount,
            processedCount,
            updatedCount,
            emptyCount,
            failedCount,
            rateLimited);

    public static MailruPostmasterMailingSyncBatchResult Failed() =>
        new(MailruPostmasterMailingSyncBatchStatus.Failed, 0, 0, 0, 0, 1, false);
}

public interface IMailruPostmasterMailingStatisticsSynchronizer
{
    Task<MailruPostmasterMailingSyncBatchResult> RunOnceAsync(
        CancellationToken cancellationToken = default);
}

public sealed class MailruPostmasterMailingStatisticsSynchronizer(
    IMailruPostmasterMailingCandidateReader candidateReader,
    IMailruPostmasterMailingStorage storage,
    IMailruPostmasterClient client,
    MailruPostmasterOptions integrationOptions,
    MailruPostmasterMailingMetricsOptions mailingOptions,
    MailruPostmasterSyncOptions syncOptions,
    TimeProvider timeProvider,
    ILogger<MailruPostmasterMailingStatisticsSynchronizer> logger)
    : IMailruPostmasterMailingStatisticsSynchronizer
{
    public async Task<MailruPostmasterMailingSyncBatchResult> RunOnceAsync(
        CancellationToken cancellationToken = default)
    {
        if (!integrationOptions.Enabled || !mailingOptions.Enabled)
        {
            return MailruPostmasterMailingSyncBatchResult.Disabled();
        }

        if (!integrationOptions.IsConfigured)
        {
            logger.LogWarning(
                "Mail.ru Postmaster mailing statistics synchronization skipped because integration is not configured. domain={Domain}",
                integrationOptions.Domain);
            return MailruPostmasterMailingSyncBatchResult.NotConfigured();
        }

        try
        {
            return await RunCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Mail.ru Postmaster mailing statistics batch failed without affecting domain synchronization or the application host. domain={Domain}",
                NormalizeDomain(integrationOptions.Domain));
            return MailruPostmasterMailingSyncBatchResult.Failed();
        }
    }

    private async Task<MailruPostmasterMailingSyncBatchResult> RunCoreAsync(
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        var domain = NormalizeDomain(integrationOptions.Domain);
        var candidates = await candidateReader.GetCandidatesAsync(
            domain,
            mailingOptions.FromDate,
            startedAt,
            mailingOptions.BatchSize,
            cancellationToken);

        logger.LogInformation(
            "Mail.ru Postmaster mailing statistics batch started. domain={Domain} candidateCount={CandidateCount} batchSize={BatchSize}",
            domain,
            candidates.Count,
            mailingOptions.BatchSize);

        var processedCount = 0;
        var updatedCount = 0;
        var emptyCount = 0;
        var failedCount = 0;
        var rateLimited = false;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processedCount++;

            var msgType = MailruPostmasterMessageType.Build(candidate.MailingId);
            var dateFrom = MailruPostmasterSyncSchedule.GetMoscowDate(candidate.FirstSendAt);
            var dateTo = MailruPostmasterSyncSchedule.GetMoscowDate(startedAt).AddDays(-1);
            var nextDailyAttemptAt = MailruPostmasterSyncSchedule.GetNextRunAtUtc(
                startedAt,
                syncOptions.SyncHourMoscow);

            try
            {
                await storage.MarkAttemptAsync(
                    domain,
                    candidate.MailingId,
                    msgType,
                    candidate.FirstSendAt,
                    candidate.LastSendAt,
                    startedAt,
                    cancellationToken);

                var statisticsResult = await client.GetDetailedStatisticsAsync(
                    dateFrom,
                    dateTo,
                    domain,
                    msgType,
                    cancellationToken);
                if (!statisticsResult.IsSuccess)
                {
                    failedCount++;
                    var isRateLimited = IsRateLimited(statisticsResult);
                    var nextAttemptAt = isRateLimited
                        ? CalculateRateLimitRetryAt(startedAt, statisticsResult.RetryAfter, nextDailyAttemptAt)
                        : nextDailyAttemptAt;
                    var errorCode = NormalizeErrorCode(statisticsResult.ErrorCode);
                    var errorSummary = NormalizeErrorSummary(statisticsResult.ErrorMessage);

                    await PersistFailureSafelyAsync(
                        candidate,
                        domain,
                        msgType,
                        startedAt,
                        errorCode,
                        errorSummary,
                        nextAttemptAt,
                        cancellationToken);

                    if (isRateLimited)
                    {
                        rateLimited = true;
                        logger.LogWarning(
                            "Mail.ru Postmaster mailing statistics batch rate limited. domain={Domain} mailingId={MailingId} msgtype={MsgType} retryAfterSeconds={RetryAfterSeconds} processedCount={ProcessedCount}",
                            domain,
                            candidate.MailingId,
                            msgType,
                            statisticsResult.RetryAfter?.TotalSeconds,
                            processedCount);
                        break;
                    }

                    logger.LogWarning(
                        "Mail.ru Postmaster mailing statistics failed. domain={Domain} mailingId={MailingId} msgtype={MsgType} errorCode={ErrorCode} dateFrom={DateFrom} dateTo={DateTo}",
                        domain,
                        candidate.MailingId,
                        msgType,
                        errorCode,
                        dateFrom,
                        dateTo);
                    continue;
                }

                var observedAt = timeProvider.GetUtcNow();
                var metrics = NormalizeMetrics(
                    statisticsResult.Data ?? Array.Empty<MailruPostmasterDailyStatistics>(),
                    domain,
                    dateFrom,
                    dateTo);
                var hasData = metrics.Length > 0;
                var fingerprint = hasData
                    ? MailruPostmasterMailingMetricsFingerprint.Build(metrics)
                    : null;
                var unchangedSuccessCount = CalculateUnchangedSuccessCount(
                    candidate.SyncState,
                    fingerprint,
                    hasData);
                var isStable = CalculateIsStable(
                    candidate.LastSendAt,
                    dateTo,
                    hasData,
                    unchangedSuccessCount,
                    mailingOptions.StableRuns);
                var isMaxAgeReached = CalculateIsMaxAgeReached(
                    candidate.LastSendAt,
                    startedAt,
                    mailingOptions.MaxAgeDays);
                var completedAt = isMaxAgeReached ? observedAt : null;
                var nextAttemptAt = completedAt is not null
                    ? null
                    : isStable
                        ? observedAt.AddDays(7)
                        : nextDailyAttemptAt;

                if (hasData)
                {
                    await storage.UpsertDailyMetricsAsync(
                        candidate.MailingId,
                        domain,
                        msgType,
                        metrics,
                        observedAt,
                        cancellationToken);
                }

                await storage.MarkSuccessAsync(
                    domain,
                    candidate.MailingId,
                    msgType,
                    candidate.FirstSendAt,
                    candidate.LastSendAt,
                    observedAt,
                    dateTo,
                    fingerprint,
                    unchangedSuccessCount,
                    hasData,
                    isStable,
                    nextAttemptAt,
                    completedAt,
                    cancellationToken);

                if (hasData)
                {
                    updatedCount++;
                    logger.LogInformation(
                        "Mail.ru Postmaster mailing statistics updated. domain={Domain} mailingId={MailingId} msgtype={MsgType} dateFrom={DateFrom} dateTo={DateTo} metricDays={MetricDays} hasData={HasData} isStable={IsStable} completed={Completed}",
                        domain,
                        candidate.MailingId,
                        msgType,
                        dateFrom,
                        dateTo,
                        metrics.Length,
                        hasData,
                        isStable,
                        completedAt is not null);
                }
                else
                {
                    emptyCount++;
                    logger.LogInformation(
                        "Mail.ru Postmaster mailing statistics unavailable yet. domain={Domain} mailingId={MailingId} msgtype={MsgType} dateFrom={DateFrom} dateTo={DateTo} hasData={HasData} completed={Completed}",
                        domain,
                        candidate.MailingId,
                        msgType,
                        dateFrom,
                        dateTo,
                        hasData,
                        completedAt is not null);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedCount++;
                logger.LogError(
                    ex,
                    "Mail.ru Postmaster mailing statistics failed without affecting the remaining batch, domain synchronization or the application host. domain={Domain} mailingId={MailingId} msgtype={MsgType}",
                    domain,
                    candidate.MailingId,
                    msgType);

                await PersistFailureSafelyAsync(
                    candidate,
                    domain,
                    msgType,
                    timeProvider.GetUtcNow(),
                    "mailing_statistics_unexpected_error",
                    "Внутренняя ошибка синхронизации статистики рассылки.",
                    nextDailyAttemptAt,
                    cancellationToken);
            }
        }

        stopwatch.Stop();
        logger.LogInformation(
            "Mail.ru Postmaster mailing statistics batch completed. domain={Domain} candidateCount={CandidateCount} processedCount={ProcessedCount} updatedCount={UpdatedCount} emptyCount={EmptyCount} failedCount={FailedCount} rateLimited={RateLimited} durationMs={DurationMs}",
            domain,
            candidates.Count,
            processedCount,
            updatedCount,
            emptyCount,
            failedCount,
            rateLimited,
            stopwatch.ElapsedMilliseconds);

        return MailruPostmasterMailingSyncBatchResult.Completed(
            candidates.Count,
            processedCount,
            updatedCount,
            emptyCount,
            failedCount,
            rateLimited);
    }

    private async Task PersistFailureSafelyAsync(
        MailruPostmasterMailingCandidate candidate,
        string domain,
        string msgType,
        DateTimeOffset failedAt,
        string errorCode,
        string errorSummary,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken)
    {
        try
        {
            await storage.MarkFailureAsync(
                domain,
                candidate.MailingId,
                msgType,
                candidate.FirstSendAt,
                candidate.LastSendAt,
                failedAt,
                errorCode,
                errorSummary,
                nextAttemptAt,
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
                "Mail.ru Postmaster failed to persist mailing statistics error without affecting the remaining batch, domain synchronization or the application host. domain={Domain} mailingId={MailingId} msgtype={MsgType} errorCode={ErrorCode}",
                domain,
                candidate.MailingId,
                msgType,
                errorCode);
        }
    }

    public static int CalculateUnchangedSuccessCount(
        MailruPostmasterMailingSyncStateEntity? previousState,
        string? fingerprint,
        bool hasData)
    {
        if (!hasData ||
            string.IsNullOrWhiteSpace(fingerprint) ||
            previousState is not { HasData: true } ||
            string.IsNullOrWhiteSpace(previousState.LastDataFingerprint) ||
            !string.Equals(previousState.LastDataFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return previousState.UnchangedSuccessCount == int.MaxValue
            ? int.MaxValue
            : previousState.UnchangedSuccessCount + 1;
    }

    public static bool CalculateIsStable(
        DateTimeOffset lastSendAt,
        DateOnly latestCompletedDate,
        bool hasData,
        int unchangedSuccessCount,
        int stableRuns)
    {
        var lastSendDate = MailruPostmasterSyncSchedule.GetMoscowDate(lastSendAt);
        var completedDaysAfterLastSend = latestCompletedDate.DayNumber - lastSendDate.DayNumber;
        return hasData &&
               completedDaysAfterLastSend >= 2 &&
               unchangedSuccessCount >= Math.Clamp(
                   stableRuns,
                   MailruPostmasterMailingMetricsOptions.MinStableRuns,
                   MailruPostmasterMailingMetricsOptions.MaxStableRuns);
    }

    public static bool CalculateIsMaxAgeReached(
        DateTimeOffset lastSendAt,
        DateTimeOffset nowUtc,
        int maxAgeDays)
    {
        var lastSendDate = MailruPostmasterSyncSchedule.GetMoscowDate(lastSendAt);
        var currentDate = MailruPostmasterSyncSchedule.GetMoscowDate(nowUtc);
        return currentDate.DayNumber - lastSendDate.DayNumber >= Math.Clamp(
            maxAgeDays,
            MailruPostmasterMailingMetricsOptions.MinMaxAgeDays,
            MailruPostmasterMailingMetricsOptions.MaxMaxAgeDays);
    }

    private static MailruPostmasterDailyStatistics[] NormalizeMetrics(
        IEnumerable<MailruPostmasterDailyStatistics> metrics,
        string domain,
        DateOnly dateFrom,
        DateOnly dateTo) =>
        metrics
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Domain) &&
                string.Equals(NormalizeDomain(x.Domain), domain, StringComparison.Ordinal) &&
                x.Date >= dateFrom &&
                x.Date <= dateTo)
            .GroupBy(x => x.Date)
            .Select(x => x.Last())
            .OrderBy(x => x.Date)
            .ToArray();

    private static bool IsRateLimited<T>(MailruPostmasterResult<T> result) =>
        result.HttpStatusCode == 429 ||
        string.Equals(result.ErrorCode, "rate_limited", StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset CalculateRateLimitRetryAt(
        DateTimeOffset nowUtc,
        TimeSpan? retryAfter,
        DateTimeOffset fallback)
    {
        if (retryAfter is not { } delay || delay <= TimeSpan.Zero)
        {
            return fallback;
        }

        return nowUtc.Add(delay);
    }

    private static string NormalizeErrorCode(string? errorCode) =>
        string.IsNullOrWhiteSpace(errorCode)
            ? "postmaster_api_error"
            : errorCode.Trim();

    private static string NormalizeErrorSummary(string? errorSummary) =>
        string.IsNullOrWhiteSpace(errorSummary)
            ? "Ошибка Mail.ru Postmaster API."
            : errorSummary.Trim();

    private static string NormalizeDomain(string domain) =>
        domain.Trim().TrimEnd('.').ToLowerInvariant();
}

public static class MailruPostmasterMailingMetricsFingerprint
{
    public static string Build(IEnumerable<MailruPostmasterDailyStatistics> metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        var builder = new StringBuilder();
        foreach (var metric in metrics
                     .GroupBy(x => x.Date)
                     .Select(x => x.Last())
                     .OrderBy(x => x.Date))
        {
            builder
                .Append(metric.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('|')
                .Append(metric.MessagesSent.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(metric.Delivered.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(metric.ProbablySpam.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(metric.Spam.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(metric.Complaints.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(metric.Read.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(metric.DeletedRead.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(metric.DeletedUnread.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(NormalizeDouble(metric.SpamPercent)).Append('|')
                .Append(NormalizeDouble(metric.ProbablySpamPercent)).Append('|')
                .Append(NormalizeDouble(metric.Reputation)).Append('|')
                .Append(NormalizeDouble(metric.Trend)).Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeDouble(double value) =>
        (value == 0d ? 0d : value).ToString("R", CultureInfo.InvariantCulture);
}

public sealed class DisabledMailruPostmasterMailingStatisticsSynchronizer
    : IMailruPostmasterMailingStatisticsSynchronizer
{
    public Task<MailruPostmasterMailingSyncBatchResult> RunOnceAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(MailruPostmasterMailingSyncBatchResult.Disabled());
}

public sealed class MailruPostmasterCompositeSynchronizer(
    MailruPostmasterSynchronizer domainSynchronizer,
    IServiceScopeFactory scopeFactory,
    ILogger<MailruPostmasterCompositeSynchronizer> logger)
    : IMailruPostmasterSynchronizer
{
    public async Task<MailruPostmasterSyncRunResult> RunOnceAsync(
        CancellationToken cancellationToken = default)
    {
        var domainResult = await domainSynchronizer.RunOnceAsync(cancellationToken);
        if (domainResult.Status != MailruPostmasterSyncRunStatus.Succeeded)
        {
            return domainResult;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var mailingSynchronizer = scope.ServiceProvider
                .GetService<IMailruPostmasterMailingStatisticsSynchronizer>();
            if (mailingSynchronizer is not null)
            {
                await mailingSynchronizer.RunOnceAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Mail.ru Postmaster mailing statistics stage failed without affecting completed domain synchronization or the application host.");
        }

        return domainResult;
    }
}
