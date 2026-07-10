using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Database;
using Pismolet.Web.Infrastructure.Postmaster;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailruPostmasterMailingSynchronizationTests
{
    [Fact]
    public async Task CandidateReader_UsesActualAttemptsFiltersStatesAndAppliesFairBatchOrder()
    {
        await using var applicationConnection = new SqliteConnection("Data Source=:memory:");
        await using var postmasterConnection = new SqliteConnection("Data Source=:memory:");
        await applicationConnection.OpenAsync();
        await postmasterConnection.OpenAsync();
        await using var applicationDb = await CreateApplicationDbContextAsync(applicationConnection);
        await using var postmasterDb = await CreatePostmasterDbContextAsync(postmasterConnection);

        var now = new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
        var overdueId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var newId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var successfulId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var noAttemptId = Guid.Parse("10000000-0000-0000-0000-000000000004");
        var beforeFromDateId = Guid.Parse("10000000-0000-0000-0000-000000000005");
        var currentMoscowDayId = Guid.Parse("10000000-0000-0000-0000-000000000006");
        var completedId = Guid.Parse("10000000-0000-0000-0000-000000000007");
        var futureId = Guid.Parse("10000000-0000-0000-0000-000000000008");

        applicationDb.Mailings.AddRange(
            Mailing(overdueId, MailingStatus.Sent),
            Mailing(newId, MailingStatus.Failed),
            Mailing(successfulId, MailingStatus.Paused),
            Mailing(noAttemptId, MailingStatus.Sending),
            Mailing(beforeFromDateId, MailingStatus.Sent),
            Mailing(currentMoscowDayId, MailingStatus.Sending),
            Mailing(completedId, MailingStatus.Sent),
            Mailing(futureId, MailingStatus.Sent));
        applicationDb.SendEvents.AddRange(
            Attempt(overdueId, new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.Zero)),
            Attempt(newId, new DateTimeOffset(2026, 7, 11, 9, 0, 0, TimeSpan.Zero)),
            Attempt(successfulId, new DateTimeOffset(2026, 7, 9, 9, 0, 0, TimeSpan.Zero)),
            Pending(noAttemptId, new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero)),
            Attempt(beforeFromDateId, new DateTimeOffset(2026, 7, 8, 9, 0, 0, TimeSpan.Zero)),
            Attempt(currentMoscowDayId, new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero)),
            Attempt(completedId, new DateTimeOffset(2026, 7, 10, 11, 0, 0, TimeSpan.Zero)),
            Attempt(futureId, new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero)));
        await applicationDb.SaveChangesAsync();

        postmasterDb.MailingSyncStates.AddRange(
            State(
                overdueId,
                firstSendAt: new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.Zero),
                lastSendAt: new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.Zero),
                nextAttemptAt: now.AddHours(-2),
                lastSuccessAt: now.AddDays(-4)),
            State(
                successfulId,
                firstSendAt: new DateTimeOffset(2026, 7, 9, 9, 0, 0, TimeSpan.Zero),
                lastSendAt: new DateTimeOffset(2026, 7, 9, 9, 0, 0, TimeSpan.Zero),
                nextAttemptAt: null,
                lastSuccessAt: now.AddDays(-5)),
            State(
                completedId,
                firstSendAt: new DateTimeOffset(2026, 7, 10, 11, 0, 0, TimeSpan.Zero),
                lastSendAt: new DateTimeOffset(2026, 7, 10, 11, 0, 0, TimeSpan.Zero),
                nextAttemptAt: null,
                lastSuccessAt: now.AddDays(-3),
                completedAt: now.AddDays(-1)),
            State(
                futureId,
                firstSendAt: new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
                lastSendAt: new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero),
                nextAttemptAt: now.AddDays(1),
                lastSuccessAt: now.AddDays(-2)));
        await postmasterDb.SaveChangesAsync();

        var reader = new EfMailruPostmasterMailingCandidateReader(applicationDb, postmasterDb);

        var result = await reader.GetCandidatesAsync(
            "PISMOLET.RU.",
            new DateOnly(2026, 7, 9),
            now,
            batchSize: 3);

        Assert.Equal(3, result.Count);
        Assert.Equal(overdueId, result[0].MailingId);
        Assert.Equal(newId, result[1].MailingId);
        Assert.Equal(successfulId, result[2].MailingId);
        Assert.DoesNotContain(result, x => x.MailingId == noAttemptId);
        Assert.DoesNotContain(result, x => x.MailingId == beforeFromDateId);
        Assert.DoesNotContain(result, x => x.MailingId == currentMoscowDayId);
        Assert.DoesNotContain(result, x => x.MailingId == completedId);
        Assert.DoesNotContain(result, x => x.MailingId == futureId);
    }

    [Fact]
    public async Task Synchronizer_WhenDisabled_DoesNotReadCandidatesCallApiOrTouchStorage()
    {
        var candidateReader = new FakeCandidateReader([Candidate(Guid.NewGuid())]);
        var storage = new RecordingMailingStorage();
        var client = new FakePostmasterClient();
        var synchronizer = CreateSynchronizer(
            candidateReader,
            storage,
            client,
            MailingOptions(enabled: false),
            Utc(2026, 7, 15, 9));

        var result = await synchronizer.RunOnceAsync();

        Assert.Equal(MailruPostmasterMailingSyncBatchStatus.Disabled, result.Status);
        Assert.Equal(0, candidateReader.CallCount);
        Assert.Empty(client.DetailedCalls);
        Assert.Empty(storage.Attempts);
        Assert.Empty(storage.Successes);
        Assert.Empty(storage.Failures);
    }

    [Fact]
    public async Task Synchronizer_EmptyResponseStoresNoFakeMetricsAndKeepsDailyRetry()
    {
        var mailingId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var now = Utc(2026, 7, 11, 9);
        var candidateReader = new FakeCandidateReader(
        [
            Candidate(
                mailingId,
                firstSendAt: Utc(2026, 7, 10, 8),
                lastSendAt: Utc(2026, 7, 10, 10))
        ]);
        var storage = new RecordingMailingStorage();
        var client = new FakePostmasterClient(
            MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>.Success(
                Array.Empty<MailruPostmasterDailyStatistics>()));
        var synchronizer = CreateSynchronizer(
            candidateReader,
            storage,
            client,
            MailingOptions(enabled: true),
            now);

        var result = await synchronizer.RunOnceAsync();

        Assert.Equal(MailruPostmasterMailingSyncBatchStatus.Completed, result.Status);
        Assert.Equal(1, result.ProcessedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(1, result.EmptyCount);
        Assert.Equal(0, result.FailedCount);
        Assert.False(result.RateLimited);
        Assert.Empty(storage.Upserts);

        var apiCall = Assert.Single(client.DetailedCalls);
        Assert.Equal(new DateOnly(2026, 7, 10), apiCall.DateFrom);
        Assert.Equal(new DateOnly(2026, 7, 10), apiCall.DateTo);
        Assert.Equal("pismolet.ru", apiCall.Domain);
        Assert.Equal(MailruPostmasterMessageType.Build(mailingId), apiCall.MsgType);

        var success = Assert.Single(storage.Successes);
        Assert.False(success.HasData);
        Assert.False(success.IsStable);
        Assert.Null(success.Fingerprint);
        Assert.Equal(0, success.UnchangedSuccessCount);
        Assert.Null(success.CompletedAt);
        Assert.Equal(Utc(2026, 7, 12, 3), success.NextAttemptAt);
    }

    [Fact]
    public async Task Synchronizer_UnchangedMetricsBecomeStableAndFingerprintIsDeterministic()
    {
        var mailingId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var now = Utc(2026, 7, 15, 4);
        var metrics = new[]
        {
            Metric(new DateOnly(2026, 7, 11), messagesSent: 5, delivered: 4),
            Metric(new DateOnly(2026, 7, 10), messagesSent: 10, delivered: 9)
        };
        var fingerprint = MailruPostmasterMailingMetricsFingerprint.Build(metrics);
        var previousState = State(
            mailingId,
            firstSendAt: Utc(2026, 7, 10, 8),
            lastSendAt: Utc(2026, 7, 10, 10),
            nextAttemptAt: now.AddHours(-1),
            lastSuccessAt: now.AddDays(-1),
            fingerprint: fingerprint,
            unchangedSuccessCount: 1,
            hasData: true);
        var candidateReader = new FakeCandidateReader(
        [
            Candidate(
                mailingId,
                firstSendAt: Utc(2026, 7, 10, 8),
                lastSendAt: Utc(2026, 7, 10, 10),
                state: previousState)
        ]);
        var storage = new RecordingMailingStorage();
        var client = new FakePostmasterClient(
            MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>.Success(metrics));
        var synchronizer = CreateSynchronizer(
            candidateReader,
            storage,
            client,
            MailingOptions(enabled: true, stableRuns: 2),
            now);

        var result = await synchronizer.RunOnceAsync();

        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(0, result.FailedCount);
        var upsert = Assert.Single(storage.Upserts);
        Assert.Equal(2, upsert.Metrics.Count);
        Assert.Equal(new DateOnly(2026, 7, 10), upsert.Metrics[0].Date);
        Assert.Equal(new DateOnly(2026, 7, 11), upsert.Metrics[1].Date);

        var success = Assert.Single(storage.Successes);
        Assert.Equal(fingerprint, success.Fingerprint);
        Assert.Equal(2, success.UnchangedSuccessCount);
        Assert.True(success.HasData);
        Assert.True(success.IsStable);
        Assert.Null(success.CompletedAt);
        Assert.Equal(now.AddDays(7), success.NextAttemptAt);

        Assert.Equal(
            fingerprint,
            MailruPostmasterMailingMetricsFingerprint.Build(metrics.Reverse()));
        Assert.NotEqual(
            fingerprint,
            MailruPostmasterMailingMetricsFingerprint.Build(
            [
                Metric(new DateOnly(2026, 7, 10), messagesSent: 11, delivered: 9),
                metrics[0]
            ]));
    }

    [Fact]
    public async Task Synchronizer_RateLimitStopsOnlyCurrentPm4BatchAndUsesRetryAfter()
    {
        var firstId = Guid.Parse("20000000-0000-0000-0000-000000000003");
        var secondId = Guid.Parse("20000000-0000-0000-0000-000000000004");
        var now = Utc(2026, 7, 15, 9);
        var candidateReader = new FakeCandidateReader([Candidate(firstId), Candidate(secondId)]);
        var storage = new RecordingMailingStorage();
        var client = new FakePostmasterClient(
            MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>.Failure(
                "rate_limited",
                "Mail.ru временно ограничил частоту запросов.",
                httpStatusCode: 429,
                retryAfter: TimeSpan.FromMinutes(30)),
            MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>.Success(
            [
                Metric(new DateOnly(2026, 7, 10), 1, 1)
            ]));
        var synchronizer = CreateSynchronizer(
            candidateReader,
            storage,
            client,
            MailingOptions(enabled: true),
            now);

        var result = await synchronizer.RunOnceAsync();

        Assert.Equal(MailruPostmasterMailingSyncBatchStatus.Completed, result.Status);
        Assert.Equal(2, result.CandidateCount);
        Assert.Equal(1, result.ProcessedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.True(result.RateLimited);
        Assert.Single(client.DetailedCalls);
        var failure = Assert.Single(storage.Failures);
        Assert.Equal(firstId, failure.MailingId);
        Assert.Equal("rate_limited", failure.ErrorCode);
        Assert.Equal(now.AddMinutes(30), failure.NextAttemptAt);
        Assert.Empty(storage.Successes);
    }

    [Fact]
    public async Task Synchronizer_NonRateErrorDoesNotBlockNextMailingAndOldEmptyMailingCompletes()
    {
        var failedId = Guid.Parse("20000000-0000-0000-0000-000000000005");
        var completedId = Guid.Parse("20000000-0000-0000-0000-000000000006");
        var now = Utc(2026, 7, 20, 9);
        var candidateReader = new FakeCandidateReader(
        [
            Candidate(failedId, firstSendAt: Utc(2026, 7, 10, 8), lastSendAt: Utc(2026, 7, 10, 9)),
            Candidate(completedId, firstSendAt: Utc(2026, 7, 10, 8), lastSendAt: Utc(2026, 7, 10, 9))
        ]);
        var storage = new RecordingMailingStorage();
        var client = new FakePostmasterClient(
            MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>.Failure(
                "api_unavailable",
                "Mail.ru временно недоступен.",
                httpStatusCode: 503),
            MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>.Success(
                Array.Empty<MailruPostmasterDailyStatistics>()));
        var synchronizer = CreateSynchronizer(
            candidateReader,
            storage,
            client,
            MailingOptions(enabled: true, maxAgeDays: 7),
            now);

        var result = await synchronizer.RunOnceAsync();

        Assert.Equal(2, result.ProcessedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.EmptyCount);
        Assert.False(result.RateLimited);
        Assert.Equal(2, client.DetailedCalls.Count);
        Assert.Single(storage.Failures);
        var success = Assert.Single(storage.Successes);
        Assert.Equal(completedId, success.MailingId);
        Assert.False(success.HasData);
        Assert.False(success.IsStable);
        Assert.Equal(now, success.CompletedAt);
        Assert.Null(success.NextAttemptAt);
    }

    [Fact]
    public void ChangedFingerprintResetsUnchangedCounter()
    {
        var state = State(
            Guid.NewGuid(),
            firstSendAt: Utc(2026, 7, 10, 8),
            lastSendAt: Utc(2026, 7, 10, 9),
            nextAttemptAt: null,
            lastSuccessAt: Utc(2026, 7, 14, 9),
            fingerprint: new string('a', 64),
            unchangedSuccessCount: 8,
            hasData: true);

        var result = MailruPostmasterMailingStatisticsSynchronizer.CalculateUnchangedSuccessCount(
            state,
            new string('b', 64),
            hasData: true);

        Assert.Equal(0, result);
    }

    private static MailruPostmasterMailingStatisticsSynchronizer CreateSynchronizer(
        IMailruPostmasterMailingCandidateReader candidateReader,
        IMailruPostmasterMailingStorage storage,
        IMailruPostmasterClient client,
        MailruPostmasterMailingMetricsOptions mailingOptions,
        DateTimeOffset now) =>
        new(
            candidateReader,
            storage,
            client,
            IntegrationOptions(),
            mailingOptions,
            new MailruPostmasterSyncOptions(6, 30, 3),
            new FixedTimeProvider(now),
            NullLogger<MailruPostmasterMailingStatisticsSynchronizer>.Instance);

    private static MailruPostmasterOptions IntegrationOptions() =>
        new(
            Enabled: true,
            Domain: "pismolet.ru",
            RefreshToken: "test-refresh-token",
            OAuthBaseUri: new Uri("https://o2.mail.ru/"),
            ApiBaseUri: new Uri("https://postmaster.mail.ru/"),
            RequestTimeout: TimeSpan.FromSeconds(15),
            MaxRateLimitDelay: TimeSpan.FromSeconds(10));

    private static MailruPostmasterMailingMetricsOptions MailingOptions(
        bool enabled,
        int stableRuns = 2,
        int maxAgeDays = 30) =>
        new(
            Enabled: enabled,
            FromDate: new DateOnly(2026, 7, 9),
            BatchSize: 20,
            ResyncDays: 3,
            StableRuns: stableRuns,
            MaxAgeDays: maxAgeDays);

    private static MailruPostmasterMailingCandidate Candidate(
        Guid mailingId,
        DateTimeOffset? firstSendAt = null,
        DateTimeOffset? lastSendAt = null,
        MailruPostmasterMailingSyncStateEntity? state = null) =>
        new(
            mailingId,
            MailingStatus.Sent,
            firstSendAt ?? Utc(2026, 7, 10, 8),
            lastSendAt ?? Utc(2026, 7, 10, 9),
            ProcessedRecipients: 1,
            state);

    private static MailingEntity Mailing(Guid mailingId, MailingStatus status) =>
        new()
        {
            Id = mailingId,
            OwnerEmail = "owner@example.test",
            Subject = $"Рассылка {mailingId:N}",
            StatusRu = status.ToRu(),
            PublicId = $"PL-{mailingId:N}"[..11].ToUpperInvariant(),
            CreatedAt = Utc(2026, 7, 8, 9)
        };

    private static SendEventEntity Attempt(Guid mailingId, DateTimeOffset at) =>
        SendEvent(mailingId, at, attempt: 1, SendEventStatus.Accepted);

    private static SendEventEntity Pending(Guid mailingId, DateTimeOffset at) =>
        SendEvent(mailingId, at, attempt: 0, SendEventStatus.Pending);

    private static SendEventEntity SendEvent(
        Guid mailingId,
        DateTimeOffset at,
        int attempt,
        SendEventStatus status) =>
        new()
        {
            Id = Guid.NewGuid(),
            MailingId = mailingId,
            OwnerEmail = "owner@example.test",
            RecipientEmail = $"recipient-{Guid.NewGuid():N}@example.test",
            Status = status.ToString(),
            Reason = SendSkipReason.None.ToString(),
            CreatedAt = at.AddMinutes(-1),
            UpdatedAt = at,
            AcceptedAt = status == SendEventStatus.Accepted ? at : null,
            AcceptedUtcDay = status == SendEventStatus.Accepted
                ? at.UtcDateTime.Year * 10000 + at.UtcDateTime.Month * 100 + at.UtcDateTime.Day
                : null,
            Provider = "Smtp",
            ProviderMessageId = status == SendEventStatus.Accepted ? $"message-{Guid.NewGuid():N}" : null,
            Attempt = attempt,
            DeliveryStatus = DeliveryStatus.NotReported.ToString()
        };

    private static MailruPostmasterMailingSyncStateEntity State(
        Guid mailingId,
        DateTimeOffset firstSendAt,
        DateTimeOffset lastSendAt,
        DateTimeOffset? nextAttemptAt,
        DateTimeOffset? lastSuccessAt,
        DateTimeOffset? completedAt = null,
        string? fingerprint = null,
        int unchangedSuccessCount = 0,
        bool hasData = false) =>
        new()
        {
            MailingId = mailingId,
            Domain = "pismolet.ru",
            MsgType = MailruPostmasterMessageType.Build(mailingId),
            FirstSendAt = firstSendAt,
            LastSendAt = lastSendAt,
            LastAttemptAt = lastSuccessAt,
            LastSuccessAt = lastSuccessAt,
            NextAttemptAt = nextAttemptAt,
            LastDataFingerprint = fingerprint,
            UnchangedSuccessCount = unchangedSuccessCount,
            HasData = hasData,
            IsStable = false,
            CompletedAt = completedAt,
            UpdatedAt = lastSuccessAt ?? firstSendAt
        };

    private static MailruPostmasterDailyStatistics Metric(
        DateOnly date,
        long messagesSent,
        long delivered) =>
        new(
            Domain: "pismolet.ru",
            Date: date,
            MessagesSent: messagesSent,
            Delivered: delivered,
            ProbablySpam: Math.Max(0, messagesSent - delivered),
            Spam: 0,
            Complaints: 0,
            Read: delivered,
            DeletedRead: 0,
            DeletedUnread: 0,
            SpamPercent: 0,
            ProbablySpamPercent: messagesSent == 0 ? 0 : Math.Max(0, messagesSent - delivered) * 100d / messagesSent,
            Reputation: 1,
            Trend: 0);

    private static DateTimeOffset Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    private static async Task<PismoletDbContext> CreateApplicationDbContextAsync(
        SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<PismoletDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new PismoletDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static async Task<MailruPostmasterDbContext> CreatePostmasterDbContextAsync(
        SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<MailruPostmasterDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new MailruPostmasterDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeCandidateReader(IReadOnlyList<MailruPostmasterMailingCandidate> candidates)
        : IMailruPostmasterMailingCandidateReader
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<MailruPostmasterMailingCandidate>> GetCandidatesAsync(
            string domain,
            DateOnly fromDate,
            DateTimeOffset nowUtc,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(candidates);
        }
    }

    private sealed class FakePostmasterClient(
        params MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>[] detailedResults)
        : IMailruPostmasterClient
    {
        private readonly Queue<MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>> results =
            new(detailedResults);

        public List<DetailedCall> DetailedCalls { get; } = new();

        public Task<MailruPostmasterResult<IReadOnlyList<MailruPostmasterRegisteredDomain>>> GetRegisteredDomainsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                MailruPostmasterResult<IReadOnlyList<MailruPostmasterRegisteredDomain>>.Success(
                    Array.Empty<MailruPostmasterRegisteredDomain>()));

        public Task<MailruPostmasterResult<IReadOnlyList<MailruPostmasterTrouble>>> GetTroublesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                MailruPostmasterResult<IReadOnlyList<MailruPostmasterTrouble>>.Success(
                    Array.Empty<MailruPostmasterTrouble>()));

        public Task<MailruPostmasterResult<IReadOnlyList<MailruPostmasterStatistics>>> GetStatisticsAsync(
            DateOnly dateFrom,
            DateOnly? dateTo = null,
            string? domain = null,
            string? messageType = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                MailruPostmasterResult<IReadOnlyList<MailruPostmasterStatistics>>.Success(
                    Array.Empty<MailruPostmasterStatistics>()));

        public Task<MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>> GetDetailedStatisticsAsync(
            DateOnly dateFrom,
            DateOnly? dateTo = null,
            string? domain = null,
            string? messageType = null,
            CancellationToken cancellationToken = default)
        {
            DetailedCalls.Add(new DetailedCall(dateFrom, dateTo, domain, messageType));
            return Task.FromResult(
                results.Count > 0
                    ? results.Dequeue()
                    : MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>.Success(
                        Array.Empty<MailruPostmasterDailyStatistics>()));
        }
    }

    private sealed record DetailedCall(
        DateOnly DateFrom,
        DateOnly? DateTo,
        string? Domain,
        string? MsgType);

    private sealed class RecordingMailingStorage : IMailruPostmasterMailingStorage
    {
        public List<AttemptCall> Attempts { get; } = new();
        public List<UpsertCall> Upserts { get; } = new();
        public List<SuccessCall> Successes { get; } = new();
        public List<FailureCall> Failures { get; } = new();

        public Task UpsertDailyMetricsAsync(
            Guid mailingId,
            string domain,
            string msgType,
            IReadOnlyCollection<MailruPostmasterDailyStatistics> metrics,
            DateTimeOffset collectedAt,
            CancellationToken cancellationToken = default)
        {
            Upserts.Add(new UpsertCall(mailingId, domain, msgType, metrics.OrderBy(x => x.Date).ToArray(), collectedAt));
            return Task.CompletedTask;
        }

        public Task<MailruPostmasterMailingSyncStateEntity?> GetSyncStateAsync(
            string domain,
            Guid mailingId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<MailruPostmasterMailingSyncStateEntity?>(null);

        public Task MarkAttemptAsync(
            string domain,
            Guid mailingId,
            string msgType,
            DateTimeOffset firstSendAt,
            DateTimeOffset lastSendAt,
            DateTimeOffset attemptedAt,
            CancellationToken cancellationToken = default)
        {
            Attempts.Add(new AttemptCall(mailingId, domain, msgType, firstSendAt, lastSendAt, attemptedAt));
            return Task.CompletedTask;
        }

        public Task MarkSuccessAsync(
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
            Successes.Add(new SuccessCall(
                mailingId,
                domain,
                msgType,
                succeededAt,
                dateTo,
                dataFingerprint,
                unchangedSuccessCount,
                hasData,
                isStable,
                nextAttemptAt,
                completedAt));
            return Task.CompletedTask;
        }

        public Task MarkFailureAsync(
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
            Failures.Add(new FailureCall(
                mailingId,
                domain,
                msgType,
                failedAt,
                errorCode,
                errorSummary,
                nextAttemptAt));
            return Task.CompletedTask;
        }
    }

    private sealed record AttemptCall(
        Guid MailingId,
        string Domain,
        string MsgType,
        DateTimeOffset FirstSendAt,
        DateTimeOffset LastSendAt,
        DateTimeOffset AttemptedAt);

    private sealed record UpsertCall(
        Guid MailingId,
        string Domain,
        string MsgType,
        IReadOnlyList<MailruPostmasterDailyStatistics> Metrics,
        DateTimeOffset CollectedAt);

    private sealed record SuccessCall(
        Guid MailingId,
        string Domain,
        string MsgType,
        DateTimeOffset SucceededAt,
        DateOnly DateTo,
        string? Fingerprint,
        int UnchangedSuccessCount,
        bool HasData,
        bool IsStable,
        DateTimeOffset? NextAttemptAt,
        DateTimeOffset? CompletedAt);

    private sealed record FailureCall(
        Guid MailingId,
        string Domain,
        string MsgType,
        DateTimeOffset FailedAt,
        string ErrorCode,
        string ErrorSummary,
        DateTimeOffset? NextAttemptAt);
}
