using Microsoft.Extensions.Logging;

namespace Pismolet.Web.Infrastructure.Postmaster;

public enum MailruPostmasterAlertJournalProcessStatus
{
    Disabled = 0,
    NotConfigured = 1,
    Completed = 2,
    Failed = 3
}

public sealed record MailruPostmasterAlertJournalProcessResult(
    MailruPostmasterAlertJournalProcessStatus Status,
    MailruPostmasterAlertOverallStatus? OverallStatus,
    int ActivatedCount,
    int UpdatedCount,
    int ResolvedCount,
    int NotificationSentCount = 0,
    int NotificationFailedCount = 0,
    int NotificationStampFailedCount = 0)
{
    public static MailruPostmasterAlertJournalProcessResult Disabled() =>
        new(MailruPostmasterAlertJournalProcessStatus.Disabled, null, 0, 0, 0);

    public static MailruPostmasterAlertJournalProcessResult NotConfigured() =>
        new(MailruPostmasterAlertJournalProcessStatus.NotConfigured, null, 0, 0, 0);

    public static MailruPostmasterAlertJournalProcessResult Failed() =>
        new(MailruPostmasterAlertJournalProcessStatus.Failed, null, 0, 0, 0);
}

public interface IMailruPostmasterAlertJournalProcessor
{
    Task<MailruPostmasterAlertJournalProcessResult> RunOnceAsync(
        CancellationToken cancellationToken = default);
}

public sealed class MailruPostmasterAlertJournalProcessor(
    MailruPostmasterOptions integrationOptions,
    MailruPostmasterAlertOptions alertOptions,
    IMailruPostmasterDashboardReader dashboardReader,
    IMailruPostmasterAlertEvaluator evaluator,
    IMailruPostmasterAlertJournalReconciler reconciler,
    IMailruPostmasterAlertJournalStore journalStore,
    IMailruPostmasterAlertNotificationDispatcher notificationDispatcher,
    TimeProvider timeProvider,
    ILogger<MailruPostmasterAlertJournalProcessor> logger)
    : IMailruPostmasterAlertJournalProcessor
{
    public async Task<MailruPostmasterAlertJournalProcessResult> RunOnceAsync(
        CancellationToken cancellationToken = default)
    {
        var normalizedOptions = alertOptions.Normalize();
        if (!normalizedOptions.Enabled)
        {
            return MailruPostmasterAlertJournalProcessResult.Disabled();
        }

        if (!integrationOptions.IsConfigured)
        {
            return MailruPostmasterAlertJournalProcessResult.NotConfigured();
        }

        try
        {
            return await RunCoreAsync(normalizedOptions, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Mail.ru Postmaster PM-5 journal stage failed without affecting synchronization or the application host. domain={Domain}",
                NormalizeDomain(integrationOptions.Domain));
            return MailruPostmasterAlertJournalProcessResult.Failed();
        }
    }

    private async Task<MailruPostmasterAlertJournalProcessResult> RunCoreAsync(
        MailruPostmasterAlertOptions normalizedOptions,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow();
        var domain = NormalizeDomain(integrationOptions.Domain);
        var (currentDateFrom, currentDateTo) = MailruPostmasterDashboardCalculator.CalculateCompletedMoscowWindow(
            nowUtc,
            normalizedOptions.WindowDays);
        var previousDateTo = currentDateFrom.AddDays(-1);
        var previousDateFrom = previousDateTo.AddDays(-(normalizedOptions.WindowDays - 1));

        logger.LogInformation(
            "Mail.ru Postmaster alert evaluation started. domain={Domain} dateFrom={DateFrom} dateTo={DateTo} observationMode={ObservationMode}",
            domain,
            currentDateFrom,
            currentDateTo,
            normalizedOptions.ObservationMode);

        var data = await dashboardReader.ReadAsync(
            domain,
            previousDateFrom,
            currentDateTo,
            cancellationToken);
        var input = new MailruPostmasterAlertInput(
            domain,
            integrationOptions.Enabled,
            integrationOptions.IsConfigured,
            currentDateFrom,
            currentDateTo,
            data.Days.Where(x => x.Date >= currentDateFrom && x.Date <= currentDateTo).ToArray(),
            previousDateFrom,
            previousDateTo,
            data.Days.Where(x => x.Date >= previousDateFrom && x.Date <= previousDateTo).ToArray(),
            data.ActiveTroubles,
            data.SyncState);
        var evaluation = evaluator.Evaluate(input, normalizedOptions, nowUtc);
        var activeEvents = await journalStore.ReadActiveAsync(domain, cancellationToken);
        var reconciliation = reconciler.Reconcile(
            domain,
            evaluation,
            activeEvents,
            nowUtc,
            normalizedOptions.Enabled);
        await journalStore.ApplyAsync(reconciliation.Changes, cancellationToken);

        foreach (var change in reconciliation.Changes)
        {
            logger.LogInformation(
                "Mail.ru Postmaster alert journal event changed. domain={Domain} code={Code} severity={Severity} category={Category} status={Status} changeType={ChangeType} dateFrom={DateFrom} dateTo={DateTo} messagesSent={MessagesSent} observedValue={ObservedValue} thresholdValue={ThresholdValue}",
                change.Event.Domain,
                change.Event.Code,
                change.Event.Severity,
                change.Event.Category,
                change.Event.Status,
                change.ChangeType,
                change.Event.DateFrom,
                change.Event.DateTo,
                change.Event.MessagesSent,
                change.Event.ObservedValue,
                change.Event.ThresholdValue);
        }

        var notificationResult = await notificationDispatcher.DispatchAsync(
            reconciliation.Changes,
            normalizedOptions,
            nowUtc,
            cancellationToken);

        logger.LogInformation(
            "Mail.ru Postmaster alert evaluation completed. domain={Domain} status={Status} activated={ActivatedCount} updated={UpdatedCount} resolved={ResolvedCount} observationMode={ObservationMode} notificationSent={NotificationSentCount} notificationFailed={NotificationFailedCount} notificationStampFailed={NotificationStampFailedCount}",
            domain,
            evaluation.Status,
            reconciliation.Activated.Count,
            reconciliation.Updated.Count,
            reconciliation.Resolved.Count,
            normalizedOptions.ObservationMode,
            notificationResult.SentCount,
            notificationResult.FailedCount,
            notificationResult.NotificationStampFailedCount);

        return new MailruPostmasterAlertJournalProcessResult(
            MailruPostmasterAlertJournalProcessStatus.Completed,
            evaluation.Status,
            reconciliation.Activated.Count,
            reconciliation.Updated.Count,
            reconciliation.Resolved.Count,
            notificationResult.SentCount,
            notificationResult.FailedCount,
            notificationResult.NotificationStampFailedCount);
    }

    private static string NormalizeDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new ArgumentException("Домен Mail.ru Postmaster не задан.", nameof(domain));
        }

        return domain.Trim().TrimEnd('.').ToLowerInvariant();
    }
}
