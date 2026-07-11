using Microsoft.Extensions.Logging;

namespace Pismolet.Web.Infrastructure.Postmaster;

public enum MailruPostmasterAlertNotificationKind
{
    Activated = 0,
    Reminder = 1,
    Resolved = 2
}

public sealed record MailruPostmasterAlertNotification(
    MailruPostmasterAlertNotificationKind Kind,
    string Domain,
    string Code,
    MailruPostmasterAlertSeverity Severity,
    string Category,
    MailruPostmasterAlertEventStatus Status,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    long MessagesSent,
    double ObservedValue,
    double ThresholdValue,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt,
    DateTimeOffset? ResolvedAt,
    int OccurrenceCount);

public sealed record MailruPostmasterAlertNotificationDispatchResult(
    int CandidateCount,
    int SentCount,
    int FailedCount,
    int SkippedCount,
    int NotificationStampFailedCount)
{
    public static MailruPostmasterAlertNotificationDispatchResult Skipped(int skippedCount) =>
        new(0, 0, 0, Math.Max(0, skippedCount), 0);
}

public interface IMailruPostmasterAlertNotifier
{
    Task NotifyAsync(
        MailruPostmasterAlertNotification notification,
        CancellationToken cancellationToken = default);
}

public interface IMailruPostmasterAlertNotificationDispatcher
{
    Task<MailruPostmasterAlertNotificationDispatchResult> DispatchAsync(
        IReadOnlyList<MailruPostmasterAlertJournalChange> changes,
        MailruPostmasterAlertOptions options,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}

public sealed class UnconfiguredMailruPostmasterAlertNotifier : IMailruPostmasterAlertNotifier
{
    public Task NotifyAsync(
        MailruPostmasterAlertNotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return Task.FromException(
            new InvalidOperationException("Технический канал уведомлений PM-5 не настроен."));
    }
}

public sealed class MailruPostmasterAlertNotificationDispatcher(
    IMailruPostmasterAlertNotifier notifier,
    IMailruPostmasterAlertJournalStore journalStore,
    ILogger<MailruPostmasterAlertNotificationDispatcher> logger)
    : IMailruPostmasterAlertNotificationDispatcher
{
    public async Task<MailruPostmasterAlertNotificationDispatchResult> DispatchAsync(
        IReadOnlyList<MailruPostmasterAlertJournalChange> changes,
        MailruPostmasterAlertOptions options,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(options);

        var normalizedOptions = options.Normalize();
        if (!normalizedOptions.NotificationsEnabled)
        {
            LogSkippedSummary("disabled", changes.Count);
            return MailruPostmasterAlertNotificationDispatchResult.Skipped(changes.Count);
        }

        if (normalizedOptions.ObservationMode)
        {
            LogSkippedSummary("observation_mode", changes.Count);
            return MailruPostmasterAlertNotificationDispatchResult.Skipped(changes.Count);
        }

        var candidates = changes
            .Select(change => CreateCandidate(change, normalizedOptions, nowUtc))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .ToArray();
        var skippedCount = Math.Max(0, changes.Count - candidates.Length);
        var sentCount = 0;
        var failedCount = 0;
        var notificationStampFailedCount = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await notifier.NotifyAsync(candidate.Notification, cancellationToken);
                sentCount++;

                logger.LogInformation(
                    "Mail.ru Postmaster alert notification sent. domain={Domain} code={Code} severity={Severity} category={Category} status={Status} kind={Kind}",
                    candidate.Notification.Domain,
                    candidate.Notification.Code,
                    candidate.Notification.Severity,
                    candidate.Notification.Category,
                    candidate.Notification.Status,
                    candidate.Notification.Kind);

                var notifiedEvent = candidate.Event with
                {
                    LastNotifiedAt = nowUtc,
                    UpdatedAt = nowUtc
                };

                try
                {
                    await journalStore.ApplyAsync(
                    [
                        new MailruPostmasterAlertJournalChange(
                            MailruPostmasterAlertJournalChangeType.Updated,
                            notifiedEvent)
                    ],
                    cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    notificationStampFailedCount++;
                    logger.LogError(
                        ex,
                        "Mail.ru Postmaster alert notification timestamp failed to persist without affecting synchronization. domain={Domain} code={Code} severity={Severity} status={Status} kind={Kind}",
                        candidate.Notification.Domain,
                        candidate.Notification.Code,
                        candidate.Notification.Severity,
                        candidate.Notification.Status,
                        candidate.Notification.Kind);
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
                    "Mail.ru Postmaster alert notification failed without affecting synchronization or journal state. domain={Domain} code={Code} severity={Severity} category={Category} status={Status} kind={Kind}",
                    candidate.Notification.Domain,
                    candidate.Notification.Code,
                    candidate.Notification.Severity,
                    candidate.Notification.Category,
                    candidate.Notification.Status,
                    candidate.Notification.Kind);
            }
        }

        logger.LogInformation(
            "Mail.ru Postmaster alert notification dispatch completed. candidateCount={CandidateCount} sentCount={SentCount} failedCount={FailedCount} skippedCount={SkippedCount} notificationStampFailedCount={NotificationStampFailedCount}",
            candidates.Length,
            sentCount,
            failedCount,
            skippedCount,
            notificationStampFailedCount);

        return new MailruPostmasterAlertNotificationDispatchResult(
            candidates.Length,
            sentCount,
            failedCount,
            skippedCount,
            notificationStampFailedCount);
    }

    private void LogSkippedSummary(string reason, int changeCount)
    {
        if (changeCount == 0)
        {
            return;
        }

        logger.LogInformation(
            "Mail.ru Postmaster alert notification skipped. reason={Reason} changeCount={ChangeCount}",
            reason,
            changeCount);
    }

    private static NotificationCandidate? CreateCandidate(
        MailruPostmasterAlertJournalChange change,
        MailruPostmasterAlertOptions options,
        DateTimeOffset nowUtc)
    {
        var journalEvent = change.Event;
        if (journalEvent.Severity == MailruPostmasterAlertSeverity.Info)
        {
            return null;
        }

        var kind = change.ChangeType switch
        {
            MailruPostmasterAlertJournalChangeType.Activated => MailruPostmasterAlertNotificationKind.Activated,
            MailruPostmasterAlertJournalChangeType.Updated when IsCooldownElapsed(
                journalEvent.LastNotifiedAt,
                nowUtc,
                options.NotificationCooldownHours) => MailruPostmasterAlertNotificationKind.Reminder,
            MailruPostmasterAlertJournalChangeType.Resolved
                when journalEvent.Severity == MailruPostmasterAlertSeverity.Critical &&
                     journalEvent.LastNotifiedAt is not null => MailruPostmasterAlertNotificationKind.Resolved,
            _ => (MailruPostmasterAlertNotificationKind?)null
        };

        return kind is null
            ? null
            : new NotificationCandidate(
                journalEvent,
                new MailruPostmasterAlertNotification(
                    kind.Value,
                    journalEvent.Domain,
                    journalEvent.Code,
                    journalEvent.Severity,
                    journalEvent.Category,
                    journalEvent.Status,
                    journalEvent.DateFrom,
                    journalEvent.DateTo,
                    journalEvent.MessagesSent,
                    journalEvent.ObservedValue,
                    journalEvent.ThresholdValue,
                    journalEvent.FirstObservedAt,
                    journalEvent.LastObservedAt,
                    journalEvent.ResolvedAt,
                    journalEvent.OccurrenceCount));
    }

    private static bool IsCooldownElapsed(
        DateTimeOffset? lastNotifiedAt,
        DateTimeOffset nowUtc,
        int cooldownHours)
    {
        if (lastNotifiedAt is null)
        {
            return true;
        }

        return nowUtc >= lastNotifiedAt.Value.AddHours(cooldownHours);
    }

    private sealed record NotificationCandidate(
        MailruPostmasterAlertJournalEvent Event,
        MailruPostmasterAlertNotification Notification);
}
