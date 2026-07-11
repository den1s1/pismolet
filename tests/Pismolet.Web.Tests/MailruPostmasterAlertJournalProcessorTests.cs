using Microsoft.Extensions.Logging.Abstractions;
using Pismolet.Web.Infrastructure.Postmaster;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailruPostmasterAlertJournalProcessorTests
{
    private static readonly DateTimeOffset TestNow =
        new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunOnce_Disabled_DoesNotReadOrChangeJournal()
    {
        var reader = new StubDashboardReader(CreateData(spamDetected: true));
        var store = new MemoryJournalStore();
        var processor = CreateProcessor(
            reader,
            store,
            new MailruPostmasterAlertOptions { Enabled = false });

        var result = await processor.RunOnceAsync();

        Assert.Equal(MailruPostmasterAlertJournalProcessStatus.Disabled, result.Status);
        Assert.Equal(0, reader.CallCount);
        Assert.Equal(0, store.ApplyCount);
        Assert.Empty(store.Events);
    }

    [Fact]
    public async Task RunOnce_NewSignal_ActivatesJournalEvent()
    {
        var reader = new StubDashboardReader(CreateData(spamDetected: true));
        var store = new MemoryJournalStore();
        var processor = CreateProcessor(reader, store, EnabledOptions());

        var result = await processor.RunOnceAsync();

        Assert.Equal(MailruPostmasterAlertJournalProcessStatus.Completed, result.Status);
        Assert.Equal(MailruPostmasterAlertOverallStatus.Critical, result.OverallStatus);
        Assert.Equal(1, result.ActivatedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(0, result.ResolvedCount);
        var journalEvent = Assert.Single(store.Events);
        Assert.Equal("pismolet.ru", journalEvent.Domain);
        Assert.Equal("spam_detected", journalEvent.Code);
        Assert.Equal(MailruPostmasterAlertEventStatus.Active, journalEvent.Status);
        Assert.Equal(1, journalEvent.OccurrenceCount);
    }

    [Fact]
    public async Task RunOnce_RepeatedSignal_UpdatesExistingEventWithoutDuplicate()
    {
        var reader = new StubDashboardReader(CreateData(spamDetected: true));
        var store = new MemoryJournalStore();
        var processor = CreateProcessor(reader, store, EnabledOptions());

        await processor.RunOnceAsync();
        var result = await processor.RunOnceAsync();

        Assert.Equal(MailruPostmasterAlertJournalProcessStatus.Completed, result.Status);
        Assert.Equal(0, result.ActivatedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(0, result.ResolvedCount);
        var journalEvent = Assert.Single(store.Events);
        Assert.Equal(MailruPostmasterAlertEventStatus.Active, journalEvent.Status);
        Assert.Equal(2, journalEvent.OccurrenceCount);
    }

    [Fact]
    public async Task RunOnce_DisappearedSignal_ResolvesExistingEvent()
    {
        var reader = new StubDashboardReader(CreateData(spamDetected: true));
        var store = new MemoryJournalStore();
        var processor = CreateProcessor(reader, store, EnabledOptions());
        await processor.RunOnceAsync();

        reader.Data = CreateData(spamDetected: false);
        var result = await processor.RunOnceAsync();

        Assert.Equal(MailruPostmasterAlertJournalProcessStatus.Completed, result.Status);
        Assert.Equal(MailruPostmasterAlertOverallStatus.Calm, result.OverallStatus);
        Assert.Equal(0, result.ActivatedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(1, result.ResolvedCount);
        var journalEvent = Assert.Single(store.Events);
        Assert.Equal(MailruPostmasterAlertEventStatus.Resolved, journalEvent.Status);
        Assert.Equal(TestNow, journalEvent.ResolvedAt);
        Assert.Empty(await store.ReadActiveAsync("pismolet.ru"));
    }

    [Fact]
    public async Task RunOnce_ReaderFailure_IsIsolatedAndReported()
    {
        var reader = new StubDashboardReader(CreateData(spamDetected: true))
        {
            Exception = new InvalidOperationException("read failed")
        };
        var store = new MemoryJournalStore();
        var processor = CreateProcessor(reader, store, EnabledOptions());

        var result = await processor.RunOnceAsync();

        Assert.Equal(MailruPostmasterAlertJournalProcessStatus.Failed, result.Status);
        Assert.Equal(1, reader.CallCount);
        Assert.Equal(0, store.ApplyCount);
        Assert.Empty(store.Events);
    }

    private static MailruPostmasterAlertJournalProcessor CreateProcessor(
        IMailruPostmasterDashboardReader reader,
        IMailruPostmasterAlertJournalStore store,
        MailruPostmasterAlertOptions options) => new(
        EnabledIntegrationOptions(),
        options,
        reader,
        new MailruPostmasterAlertEvaluator(),
        new MailruPostmasterAlertJournalReconciler(),
        store,
        new FixedTimeProvider(TestNow),
        NullLogger<MailruPostmasterAlertJournalProcessor>.Instance);

    private static MailruPostmasterAlertOptions EnabledOptions() => new()
    {
        Enabled = true,
        ObservationMode = true,
        WindowDays = 7,
        MinimumMessagesForRates = 100,
        SpamCriticalCount = 1
    };

    private static MailruPostmasterOptions EnabledIntegrationOptions() => new(
        Enabled: true,
        Domain: "PISMOLET.RU.",
        RefreshToken: "test-token",
        OAuthBaseUri: new Uri("https://o2.mail.ru/", UriKind.Absolute),
        ApiBaseUri: new Uri("https://postmaster.mail.ru/", UriKind.Absolute),
        RequestTimeout: TimeSpan.FromSeconds(15),
        MaxRateLimitDelay: TimeSpan.FromSeconds(10));

    private static MailruPostmasterDashboardData CreateData(bool spamDetected)
    {
        var date = new DateOnly(2026, 7, 10);
        return new MailruPostmasterDashboardData(
            "pismolet.ru",
            new DateOnly(2026, 6, 27),
            date,
            new MailruPostmasterDashboardSyncState(
                TestNow,
                TestNow.AddHours(-1),
                null,
                null,
                0,
                date,
                TestNow),
            Array.Empty<MailruPostmasterDashboardTrouble>(),
            [
                new MailruPostmasterDashboardDay(
                    date,
                    10,
                    9,
                    0,
                    spamDetected ? 1 : 0,
                    0,
                    5,
                    0,
                    0,
                    spamDetected ? 10d : 0d,
                    0d,
                    1d,
                    0d,
                    TestNow)
            ],
            Array.Empty<MailruPostmasterDashboardRun>());
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StubDashboardReader(MailruPostmasterDashboardData data)
        : IMailruPostmasterDashboardReader
    {
        public MailruPostmasterDashboardData Data { get; set; } = data;
        public Exception? Exception { get; init; }
        public int CallCount { get; private set; }

        public Task<MailruPostmasterDashboardData> ReadAsync(
            string domain,
            DateOnly dateFrom,
            DateOnly dateTo,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Data);
        }
    }

    private sealed class MemoryJournalStore : IMailruPostmasterAlertJournalStore
    {
        public List<MailruPostmasterAlertJournalEvent> Events { get; } = [];
        public int ApplyCount { get; private set; }

        public Task<IReadOnlyList<MailruPostmasterAlertJournalEvent>> ReadActiveAsync(
            string domain,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MailruPostmasterAlertJournalEvent>>(
                Events
                    .Where(x => x.Status == MailruPostmasterAlertEventStatus.Active)
                    .Where(x => string.Equals(x.Domain, domain, StringComparison.OrdinalIgnoreCase))
                    .ToArray());

        public Task<IReadOnlyList<MailruPostmasterAlertJournalEvent>> ReadRecentAsync(
            string domain,
            MailruPostmasterAlertEventStatus? status = null,
            MailruPostmasterAlertSeverity? severity = null,
            int take = 100,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MailruPostmasterAlertJournalEvent>>(
                Events
                    .Where(x => string.Equals(x.Domain, domain, StringComparison.OrdinalIgnoreCase))
                    .Where(x => status is null || x.Status == status)
                    .Where(x => severity is null || x.Severity == severity)
                    .OrderByDescending(x => x.UpdatedAt)
                    .Take(Math.Clamp(take, 1, 500))
                    .ToArray());

        public Task ApplyAsync(
            IReadOnlyList<MailruPostmasterAlertJournalChange> changes,
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            foreach (var change in changes)
            {
                var index = Events.FindIndex(x => x.Id == change.Event.Id);
                if (index >= 0)
                {
                    Events[index] = change.Event;
                }
                else
                {
                    Events.Add(change.Event);
                }
            }

            return Task.CompletedTask;
        }
    }
}
