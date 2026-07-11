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
    int ResolvedCount)
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

        logger.LogInformation(
            "Mail.ru Postmaster PM-5 journal updated. domain={Domain} status={Status} activated={ActivatedCount} updated={UpdatedCount} resolved={ResolvedCount} observationMode={ObservationMode}",
            domain,
            evaluation.Status,
            reconciliation.Activated.Count,
            reconciliation.Updated.Count,
            reconciliation.Resolved.Count,
            normalizedOptions.ObservationMode);

        return new MailruPostmasterAlertJournalProcessResult(
            MailruPostmasterAlertJournalProcessStatus.Completed,
            evaluation.Status,
            reconciliation.Activated.Count,
            reconciliation.Updated.Count,
            reconciliation.Resolved.Count);
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
