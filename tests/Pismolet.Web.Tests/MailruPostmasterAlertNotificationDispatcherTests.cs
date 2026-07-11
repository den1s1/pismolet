using Microsoft.Extensions.Logging.Abstractions;
using Pismolet.Web.Infrastructure.Postmaster;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailruPostmasterAlertNotificationDispatcherTests
{
    private static readonly DateTimeOffset TestNow =
        new(2026, 7, 11, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Dispatch_NotificationsDisabled_DoesNotCallNotifier()
    {
        var notifier = new RecordingNotifier();
        var store = new MemoryJournalStore();
        var dispatcher = CreateDispatcher(notifier, store);

        var result = await dispatcher.DispatchAsync(
            [Activated(CreateEvent())],
            Options(notificationsEnabled: false, observationMode: false),
            TestNow);

        Assert.Equal(0, result.CandidateCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Empty(notifier.Notifications);
        Assert.Equal(0, store.ApplyCount);
    }

    [Fact]
    public async Task Dispatch_ObservationMode_DoesNotCallNotifier()
    {
        var notifier = new RecordingNotifier();
        var store = new MemoryJournalStore();
        var dispatcher = CreateDispatcher(notifier, store);

        var result = await dispatcher.DispatchAsync(
            [Activated(CreateEvent())],
            Options(notificationsEnabled: true, observationMode: true),
            TestNow);

        Assert.Equal(0, result.CandidateCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Empty(notifier.Notifications);
        Assert.Equal(0, store.ApplyCount);
    }

    [Fact]
    public async Task Dispatch_ActivatedCritical_SendsAndPersistsNotificationTimestamp()
    {
        var notifier = new RecordingNotifier();
        var store = new MemoryJournalStore();
        var dispatcher = CreateDispatcher(notifier, store);
        var journalEvent = CreateEvent(severity: MailruPostmasterAlertSeverity.Critical);

        var result = await dispatcher.DispatchAsync(
            [Activated(journalEvent)],
            Options(),
            TestNow);

        Assert.Equal(1, result.CandidateCount);
        Assert.Equal(1, result.SentCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(0, result.NotificationStampFailedCount);
        var notification = Assert.Single(notifier.Notifications);
        Assert.Equal(MailruPostmasterAlertNotificationKind.Activated, notification.Kind);
        Assert.Equal("pismolet.ru", notification.Domain);
        Assert.Equal("spam_detected", notification.Code);
        Assert.Equal(MailruPostmasterAlertSeverity.Critical, notification.Severity);
        var persisted = Assert.Single(store.AppliedEvents);
        Assert.Equal(TestNow, persisted.LastNotifiedAt);
        Assert.Equal(TestNow, persisted.UpdatedAt);
    }

    [Fact]
    public async Task Dispatch_InfoSignal_IsSkipped()
    {
        var notifier = new RecordingNotifier();
        var store = new MemoryJournalStore();
        var dispatcher = CreateDispatcher(notifier, store);

        var result = await dispatcher.DispatchAsync(
            [Activated(CreateEvent(severity: MailruPostmasterAlertSeverity.Info))],
            Options(),
            TestNow);

        Assert.Equal(0, result.CandidateCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Empty(notifier.Notifications);
        Assert.Equal(0, store.ApplyCount);
    }

    [Fact]
    public async Task Dispatch_UpdatedSignalWithinCooldown_IsSkipped()
    {
        var notifier = new RecordingNotifier();
        var store = new MemoryJournalStore();
        var dispatcher = CreateDispatcher(notifier, store);
        var journalEvent = CreateEvent(lastNotifiedAt: TestNow.AddHours(-23));

        var result = await dispatcher.DispatchAsync(
            [Updated(journalEvent)],
            Options(cooldownHours: 24),
            TestNow);

        Assert.Equal(0, result.CandidateCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Empty(notifier.Notifications);
    }

    [Fact]
    public async Task Dispatch_UpdatedSignalAtCooldownBoundary_SendsReminder()
    {
        var notifier = new RecordingNotifier();
        var store = new MemoryJournalStore();
        var dispatcher = CreateDispatcher(notifier, store);
        var journalEvent = CreateEvent(lastNotifiedAt: TestNow.AddHours(-24));

        var result = await dispatcher.DispatchAsync(
            [Updated(journalEvent)],
            Options(cooldownHours: 24),
            TestNow);

        Assert.Equal(1, result.SentCount);
        Assert.Equal(
            MailruPostmasterAlertNotificationKind.Reminder,
            Assert.Single(notifier.Notifications).Kind);
        Assert.Equal(TestNow, Assert.Single(store.AppliedEvents).LastNotifiedAt);
    }

    [Fact]
    public async Task Dispatch_ResolvedPreviouslyNotifiedCritical_SendsRecovery()
    {
        var notifier = new RecordingNotifier();
        var store = new MemoryJournalStore();
        var dispatcher = CreateDispatcher(notifier, store);
        var journalEvent = CreateEvent(
            severity: MailruPostmasterAlertSeverity.Critical,
            status: MailruPostmasterAlertEventStatus.Resolved,
            lastNotifiedAt: TestNow.AddHours(-1),
            resolvedAt: TestNow);

        var result = await dispatcher.DispatchAsync(
            [Resolved(journalEvent)],
            Options(),
            TestNow);

        Assert.Equal(1, result.SentCount);
        var notification = Assert.Single(notifier.Notifications);
        Assert.Equal(MailruPostmasterAlertNotificationKind.Resolved, notification.Kind);
        Assert.Equal(MailruPostmasterAlertEventStatus.Resolved, notification.Status);
        Assert.Equal(TestNow, notification.ResolvedAt);
    }

    [Fact]
    public async Task Dispatch_ResolvedWarning_IsSkipped()
    {
        var notifier = new RecordingNotifier();
        var store = new MemoryJournalStore();
        var dispatcher = CreateDispatcher(notifier, store);
        var journalEvent = CreateEvent(
            severity: MailruPostmasterAlertSeverity.Warning,
            status: MailruPostmasterAlertEventStatus.Resolved,
            lastNotifiedAt: TestNow.AddHours(-1),
            resolvedAt: TestNow);

        var result = await dispatcher.DispatchAsync(
            [Resolved(journalEvent)],
            Options(),
            TestNow);

        Assert.Equal(0, result.CandidateCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Empty(notifier.Notifications);
    }

    [Fact]
    public async Task Dispatch_NotifierFailure_IsIsolatedAndDoesNotStampEvent()
    {
        var notifier = new RecordingNotifier
        {
            Exception = new InvalidOperationException("channel failed")
        };
        var store = new MemoryJournalStore();
        var dispatcher = CreateDispatcher(notifier, store);

        var result = await dispatcher.DispatchAsync(
            [Activated(CreateEvent())],
            Options(),
            TestNow);

        Assert.Equal(1, result.CandidateCount);
        Assert.Equal(0, result.SentCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(0, result.NotificationStampFailedCount);
        Assert.Equal(0, store.ApplyCount);
    }

    [Fact]
    public async Task Dispatch_NotificationStampFailure_IsIsolatedAfterSuccessfulSend()
    {
        var notifier = new RecordingNotifier();
        var store = new MemoryJournalStore
        {
            ApplyException = new InvalidOperationException("storage failed")
        };
        var dispatcher = CreateDispatcher(notifier, store);

        var result = await dispatcher.DispatchAsync(
            [Activated(CreateEvent())],
            Options(),
            TestNow);

        Assert.Equal(1, result.SentCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(1, result.NotificationStampFailedCount);
        Assert.Single(notifier.Notifications);
    }

    [Fact]
    public void NotificationPayload_ContainsOnlySafeOperationalFields()
    {
        var propertyNames = typeof(MailruPostmasterAlertNotification)
            .GetProperties()
            .Select(x => x.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, x => x.Contains("Email", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, x => x.Contains("Token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, x => x.Contains("Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, x => x.Contains("Fingerprint", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, x => x.Contains("Raw", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Domain", propertyNames);
        Assert.Contains("Code", propertyNames);
        Assert.Contains("MessagesSent", propertyNames);
        Assert.Contains("ObservedValue", propertyNames);
        Assert.Contains("ThresholdValue", propertyNames);
    }

    private static MailruPostmasterAlertNotificationDispatcher CreateDispatcher(
        IMailruPostmasterAlertNotifier notifier,
        IMailruPostmasterAlertJournalStore store) => new(
        notifier,
        store,
        NullLogger<MailruPostmasterAlertNotificationDispatcher>.Instance);

    private static MailruPostmasterAlertOptions Options(
        bool notificationsEnabled = true,
        bool observationMode = false,
        int cooldownHours = 24) => new()
    {
        Enabled = true,
        NotificationsEnabled = notificationsEnabled,
        ObservationMode = observationMode,
        NotificationCooldownHours = cooldownHours
    };

    private static MailruPostmasterAlertJournalEvent CreateEvent(
        MailruPostmasterAlertSeverity severity = MailruPostmasterAlertSeverity.Warning,
        MailruPostmasterAlertEventStatus status = MailruPostmasterAlertEventStatus.Active,
        DateTimeOffset? lastNotifiedAt = null,
        DateTimeOffset? resolvedAt = null) => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "pismolet.ru",
        "spam_detected",
        severity,
        "spam",
        status,
        new string('a', 64),
        new DateOnly(2026, 7, 5),
        new DateOnly(2026, 7, 11),
        250,
        3,
        1,
        TestNow.AddHours(-48),
        TestNow.AddHours(-1),
        2,
        resolvedAt,
        lastNotifiedAt,
        TestNow.AddHours(-48),
        TestNow.AddHours(-1));

    private static MailruPostmasterAlertJournalChange Activated(
        MailruPostmasterAlertJournalEvent journalEvent) => new(
        MailruPostmasterAlertJournalChangeType.Activated,
        journalEvent);

    private static MailruPostmasterAlertJournalChange Updated(
        MailruPostmasterAlertJournalEvent journalEvent) => new(
        MailruPostmasterAlertJournalChangeType.Updated,
        journalEvent);

    private static MailruPostmasterAlertJournalChange Resolved(
        MailruPostmasterAlertJournalEvent journalEvent) => new(
        MailruPostmasterAlertJournalChangeType.Resolved,
        journalEvent);

    private sealed class RecordingNotifier : IMailruPostmasterAlertNotifier
    {
        public List<MailruPostmasterAlertNotification> Notifications { get; } = [];
        public Exception? Exception { get; init; }

        public Task NotifyAsync(
            MailruPostmasterAlertNotification notification,
            CancellationToken cancellationToken = default)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryJournalStore : IMailruPostmasterAlertJournalStore
    {
        public List<MailruPostmasterAlertJournalEvent> AppliedEvents { get; } = [];
        public int ApplyCount { get; private set; }
        public Exception? ApplyException { get; init; }

        public Task<IReadOnlyList<MailruPostmasterAlertJournalEvent>> ReadActiveAsync(
            string domain,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MailruPostmasterAlertJournalEvent>>([]);

        public Task<IReadOnlyList<MailruPostmasterAlertJournalEvent>> ReadRecentAsync(
            string domain,
            MailruPostmasterAlertEventStatus? status = null,
            MailruPostmasterAlertSeverity? severity = null,
            int take = 100,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MailruPostmasterAlertJournalEvent>>([]);

        public Task ApplyAsync(
            IReadOnlyList<MailruPostmasterAlertJournalChange> changes,
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            if (ApplyException is not null)
            {
                throw ApplyException;
            }

            AppliedEvents.AddRange(changes.Select(x => x.Event));
            return Task.CompletedTask;
        }
    }
}
