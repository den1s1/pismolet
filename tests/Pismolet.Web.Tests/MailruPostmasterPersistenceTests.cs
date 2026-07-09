using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Infrastructure.Postmaster;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailruPostmasterPersistenceTests
{
    [Fact]
    public void DesignTimeFactory_PrefersProductionConnectionString()
    {
        const string configuredConnectionString = "Host=production;Database=pismolet;Username=pismolet;Password=configured";
        const string legacyConnectionString = "Host=legacy;Database=pismolet;Username=pismolet;Password=legacy";

        var actual = MailruPostmasterDbContextFactory.ResolveConnectionString(
            configuredConnectionString,
            legacyConnectionString);

        Assert.Equal(configuredConnectionString, actual);
    }

    [Fact]
    public void DesignTimeFactory_UsesLegacyConnectionStringAsFallback()
    {
        const string legacyConnectionString = "Host=legacy;Database=pismolet;Username=pismolet;Password=legacy";

        var actual = MailruPostmasterDbContextFactory.ResolveConnectionString(null, legacyConnectionString);

        Assert.Equal(legacyConnectionString, actual);
    }

    [Fact]
    public void DesignTimeFactory_RejectsMissingConnectionString()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MailruPostmasterDbContextFactory.ResolveConnectionString(null, null));

        Assert.Contains("ConnectionStrings__PismoletDb", exception.Message);
    }

    [Fact]
    public async Task UpsertDomainDailyMetricsAsync_UpdatesExistingDayWithoutDuplicate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        var storage = new EfMailruPostmasterStorage(db);
        var date = new DateOnly(2026, 7, 9);
        var firstCollectedAt = new DateTimeOffset(2026, 7, 9, 18, 0, 0, TimeSpan.Zero);
        var secondCollectedAt = firstCollectedAt.AddHours(1);

        await storage.UpsertDomainDailyMetricsAsync(
            [CreateDailyStatistics("pismolet.ru", date, messagesSent: 10, delivered: 3, probablySpam: 7)],
            firstCollectedAt);

        await storage.UpsertDomainDailyMetricsAsync(
            [CreateDailyStatistics("PISMOLET.RU.", date, messagesSent: 12, delivered: 8, probablySpam: 4)],
            secondCollectedAt);

        var metric = await db.DomainDailyMetrics.SingleAsync();
        Assert.Equal("pismolet.ru", metric.Domain);
        Assert.Equal(12, metric.MessagesSent);
        Assert.Equal(8, metric.Delivered);
        Assert.Equal(4, metric.ProbablySpam);
        Assert.Equal(firstCollectedAt, metric.CollectedAt);
        Assert.Equal(secondCollectedAt, metric.UpdatedAt);
    }

    [Fact]
    public async Task SynchronizeTroublesAsync_ResolvesMissingAndReactivatesReturnedTroubles()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        var storage = new EfMailruPostmasterStorage(db);
        var firstSeenAt = new DateTimeOffset(2026, 7, 9, 18, 0, 0, TimeSpan.Zero);
        var secondSeenAt = firstSeenAt.AddHours(1);
        var thirdSeenAt = firstSeenAt.AddHours(2);

        await storage.SynchronizeTroublesAsync(
            "pismolet.ru",
            [
                new MailruPostmasterTrouble("pismolet.ru", 1, "Первая проблема"),
                new MailruPostmasterTrouble("pismolet.ru", 2, "Вторая проблема")
            ],
            firstSeenAt);

        await storage.SynchronizeTroublesAsync(
            "pismolet.ru",
            [new MailruPostmasterTrouble("pismolet.ru", 2, "Вторая проблема уточнена")],
            secondSeenAt);

        var first = await db.TroubleSnapshots.SingleAsync(x => x.Code == 1);
        var second = await db.TroubleSnapshots.SingleAsync(x => x.Code == 2);
        Assert.False(first.IsActive);
        Assert.Equal(secondSeenAt, first.ResolvedAt);
        Assert.True(second.IsActive);
        Assert.Equal("Вторая проблема уточнена", second.Message);
        Assert.Equal(firstSeenAt, second.FirstSeenAt);
        Assert.Equal(secondSeenAt, second.LastSeenAt);

        await storage.SynchronizeTroublesAsync(
            "pismolet.ru",
            [new MailruPostmasterTrouble("pismolet.ru", 1, "Первая проблема вернулась")],
            thirdSeenAt);

        first = await db.TroubleSnapshots.SingleAsync(x => x.Code == 1);
        second = await db.TroubleSnapshots.SingleAsync(x => x.Code == 2);
        Assert.True(first.IsActive);
        Assert.Null(first.ResolvedAt);
        Assert.Equal(firstSeenAt, first.FirstSeenAt);
        Assert.Equal(thirdSeenAt, first.LastSeenAt);
        Assert.False(second.IsActive);
        Assert.Equal(thirdSeenAt, second.ResolvedAt);
    }

    [Fact]
    public async Task MarkFailureAndSuccess_UpdateSingleSyncState()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        var storage = new EfMailruPostmasterStorage(db);
        var firstAttempt = new DateTimeOffset(2026, 7, 9, 18, 0, 0, TimeSpan.Zero);
        var secondAttempt = firstAttempt.AddMinutes(5);
        var successAt = firstAttempt.AddMinutes(10);
        var lastDomainDate = new DateOnly(2026, 7, 8);

        await storage.MarkFailureAsync("PISMOLET.RU", firstAttempt, "api_error", "Первая ошибка");
        await storage.MarkFailureAsync("pismolet.ru", secondAttempt, "timeout", "Вторая ошибка");

        var failedState = await storage.GetSyncStateAsync("pismolet.ru");
        Assert.NotNull(failedState);
        Assert.Equal(2, failedState.ConsecutiveFailures);
        Assert.Equal("timeout", failedState.LastErrorCode);
        Assert.Equal("Вторая ошибка", failedState.LastErrorSummary);
        Assert.Equal(secondAttempt, failedState.LastAttemptAt);
        Assert.Null(failedState.LastSuccessAt);

        await storage.MarkSuccessAsync("pismolet.ru", successAt, lastDomainDate);

        var successfulState = await storage.GetSyncStateAsync("pismolet.ru");
        Assert.NotNull(successfulState);
        Assert.Equal(0, successfulState.ConsecutiveFailures);
        Assert.Null(successfulState.LastErrorCode);
        Assert.Null(successfulState.LastErrorSummary);
        Assert.Equal(successAt, successfulState.LastAttemptAt);
        Assert.Equal(successAt, successfulState.LastSuccessAt);
        Assert.Equal(lastDomainDate, successfulState.LastDomainDate);
        Assert.Equal(1, await db.SyncStates.CountAsync());
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
