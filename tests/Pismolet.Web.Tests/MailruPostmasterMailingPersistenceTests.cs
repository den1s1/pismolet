using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pismolet.Web.Infrastructure.Postmaster;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailruPostmasterMailingPersistenceTests
{
    [Fact]
    public void MailingMetricsOptions_ReadsValuesAndClampsLimits()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailruPostmaster:MailingMetricsEnabled"] = "true",
                ["MailruPostmaster:MailingMetricsFromDate"] = "2026-07-10",
                ["MailruPostmaster:MailingMetricsBatchSize"] = "500",
                ["MailruPostmaster:MailingMetricsResyncDays"] = "0",
                ["MailruPostmaster:MailingMetricsStableRuns"] = "4",
                ["MailruPostmaster:MailingMetricsMaxAgeDays"] = "120"
            })
            .Build();

        var options = MailruPostmasterMailingMetricsOptions.Read(configuration);

        Assert.True(options.Enabled);
        Assert.Equal(new DateOnly(2026, 7, 10), options.FromDate);
        Assert.Equal(MailruPostmasterMailingMetricsOptions.MaxBatchSize, options.BatchSize);
        Assert.Equal(MailruPostmasterMailingMetricsOptions.MinResyncDays, options.ResyncDays);
        Assert.Equal(4, options.StableRuns);
        Assert.Equal(MailruPostmasterMailingMetricsOptions.MaxMaxAgeDays, options.MaxAgeDays);
    }

    [Fact]
    public async Task UpsertDailyMetrics_UpdatesSameMailingDayWithoutMixingMailings()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        var storage = new EfMailruPostmasterMailingStorage(db);
        var firstMailingId = Guid.Parse("64138d5d-0123-4567-89ab-cdef01234567");
        var secondMailingId = Guid.Parse("87462f9c-ae73-4d90-aba8-69b980f49452");
        var date = new DateOnly(2026, 7, 10);
        var firstCollectedAt = new DateTimeOffset(2026, 7, 11, 3, 0, 0, TimeSpan.Zero);
        var secondCollectedAt = firstCollectedAt.AddHours(1);

        await storage.UpsertDailyMetricsAsync(
            firstMailingId,
            "PISMOLET.RU.",
            MailruPostmasterMessageType.Build(firstMailingId),
            [CreateDailyStatistics("pismolet.ru", date, messagesSent: 10, delivered: 4, probablySpam: 6)],
            firstCollectedAt);

        await storage.UpsertDailyMetricsAsync(
            firstMailingId,
            "pismolet.ru",
            MailruPostmasterMessageType.Build(firstMailingId),
            [CreateDailyStatistics("PISMOLET.RU", date, messagesSent: 12, delivered: 9, probablySpam: 3)],
            secondCollectedAt);

        await storage.UpsertDailyMetricsAsync(
            secondMailingId,
            "pismolet.ru",
            MailruPostmasterMessageType.Build(secondMailingId),
            [CreateDailyStatistics("pismolet.ru", date, messagesSent: 7, delivered: 7, probablySpam: 0)],
            secondCollectedAt);

        var metrics = await db.MailingDailyMetrics.OrderBy(x => x.MailingId).ToListAsync();
        Assert.Equal(2, metrics.Count);

        var first = Assert.Single(metrics, x => x.MailingId == firstMailingId);
        Assert.Equal("pismolet.ru", first.Domain);
        Assert.Equal(MailruPostmasterMessageType.Build(firstMailingId), first.MsgType);
        Assert.Equal(12, first.MessagesSent);
        Assert.Equal(9, first.Delivered);
        Assert.Equal(3, first.ProbablySpam);
        Assert.Equal(firstCollectedAt, first.CollectedAt);
        Assert.Equal(secondCollectedAt, first.UpdatedAt);

        var second = Assert.Single(metrics, x => x.MailingId == secondMailingId);
        Assert.Equal(7, second.MessagesSent);
        Assert.Equal(7, second.Delivered);
    }

    [Fact]
    public async Task FailureThenSuccess_UpdatesSingleMailingStateAndClearsError()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        var storage = new EfMailruPostmasterMailingStorage(db);
        var mailingId = Guid.Parse("64138d5d-0123-4567-89ab-cdef01234567");
        var msgType = MailruPostmasterMessageType.Build(mailingId);
        var firstSendAt = new DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);
        var lastSendAt = firstSendAt.AddHours(2);
        var failedAt = lastSendAt.AddDays(1);
        var succeededAt = failedAt.AddMinutes(30);
        var nextAttemptAt = succeededAt.AddDays(1);
        var fingerprint = new string('a', 64);

        await storage.MarkFailureAsync(
            "PISMOLET.RU",
            mailingId,
            msgType,
            firstSendAt,
            lastSendAt,
            failedAt,
            "api_timeout",
            "Mail.ru не ответил вовремя",
            failedAt.AddHours(1));

        var failed = await storage.GetSyncStateAsync("pismolet.ru", mailingId);
        Assert.NotNull(failed);
        Assert.Equal(1, failed.ConsecutiveFailures);
        Assert.Equal("api_timeout", failed.LastErrorCode);
        Assert.Equal(failedAt, failed.LastAttemptAt);
        Assert.Null(failed.LastSuccessAt);

        await storage.MarkSuccessAsync(
            "pismolet.ru",
            mailingId,
            msgType,
            firstSendAt,
            lastSendAt,
            succeededAt,
            new DateOnly(2026, 7, 10),
            fingerprint,
            unchangedSuccessCount: 1,
            hasData: true,
            isStable: false,
            nextAttemptAt,
            completedAt: null);

        var successful = await storage.GetSyncStateAsync("pismolet.ru", mailingId);
        Assert.NotNull(successful);
        Assert.Equal(0, successful.ConsecutiveFailures);
        Assert.Null(successful.LastErrorCode);
        Assert.Null(successful.LastErrorSummary);
        Assert.Equal(succeededAt, successful.LastAttemptAt);
        Assert.Equal(succeededAt, successful.LastSuccessAt);
        Assert.Equal(new DateOnly(2026, 7, 10), successful.LastDateTo);
        Assert.Equal(fingerprint, successful.LastDataFingerprint);
        Assert.Equal(1, successful.UnchangedSuccessCount);
        Assert.True(successful.HasData);
        Assert.False(successful.IsStable);
        Assert.Equal(nextAttemptAt, successful.NextAttemptAt);
        Assert.Equal(1, await db.MailingSyncStates.CountAsync());
    }

    private static async Task<MailruPostmasterDbContext> CreateDbContextAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<MailruPostmasterDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new MailruPostmasterDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static MailruPostmasterDailyStatistics CreateDailyStatistics(
        string domain,
        DateOnly date,
        long messagesSent,
        long delivered,
        long probablySpam) =>
        new(
            Domain: domain,
            Date: date,
            MessagesSent: messagesSent,
            Delivered: delivered,
            ProbablySpam: probablySpam,
            Spam: 0,
            Complaints: 0,
            Read: 1,
            DeletedRead: 0,
            DeletedUnread: 0,
            SpamPercent: 0,
            ProbablySpamPercent: messagesSent == 0 ? 0 : probablySpam * 100d / messagesSent,
            Reputation: 0,
            Trend: 0);
}
