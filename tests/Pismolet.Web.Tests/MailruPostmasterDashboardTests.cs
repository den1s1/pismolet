using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Endpoints;
using Pismolet.Web.Infrastructure.Postmaster;

namespace Pismolet.Web.Tests;

public sealed class MailruPostmasterDashboardTests
{
    [Fact]
    public void Summarize_UsesAbsoluteTotalsAndWeightedPercentages()
    {
        var days = new[]
        {
            Day(new DateOnly(2026, 7, 8), sent: 100, delivered: 60, probablySpam: 40, spam: 0, complaints: 0),
            Day(new DateOnly(2026, 7, 9), sent: 900, delivered: 810, probablySpam: 72, spam: 9, complaints: 3)
        };

        var summary = MailruPostmasterDashboardCalculator.Summarize(days);

        Assert.Equal(1000, summary.MessagesSent);
        Assert.Equal(870, summary.Delivered);
        Assert.Equal(112, summary.ProbablySpam);
        Assert.Equal(9, summary.Spam);
        Assert.Equal(3, summary.Complaints);
        Assert.Equal(87d, summary.DeliveredPercent, precision: 6);
        Assert.Equal(11.2d, summary.ProbablySpamPercent, precision: 6);
        Assert.Equal(0.9d, summary.SpamPercent, precision: 6);
        Assert.Equal(0.3d, summary.ComplaintsPercent, precision: 6);
        Assert.False(summary.IsLowSample);
        Assert.Equal(2, summary.DataDays);
        Assert.Equal(0.2d, summary.LatestReputation);
        Assert.Equal(0.1d, summary.LatestTrend);
    }

    [Fact]
    public void Summarize_MarksSmallVolumeAsLowSample()
    {
        var summary = MailruPostmasterDashboardCalculator.Summarize(
            [Day(new DateOnly(2026, 7, 9), sent: 999, delivered: 900, probablySpam: 99, spam: 0, complaints: 0)]);

        Assert.True(summary.IsLowSample);
    }

    [Fact]
    public void CalculateCompletedMoscowWindow_ExcludesCurrentMoscowDay()
    {
        var nowUtc = new DateTimeOffset(2026, 7, 10, 0, 30, 0, TimeSpan.Zero);

        var window = MailruPostmasterDashboardCalculator.CalculateCompletedMoscowWindow(nowUtc, 7);

        Assert.Equal(new DateOnly(2026, 7, 3), window.DateFrom);
        Assert.Equal(new DateOnly(2026, 7, 9), window.DateTo);
    }

    [Fact]
    public async Task Reader_ReturnsOnlyRequestedDomainPeriodAndActiveTroubles()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        var updatedAt = new DateTimeOffset(2026, 7, 10, 5, 44, 8, TimeSpan.Zero);
        db.DomainDailyMetrics.AddRange(
            Metric("pismolet.ru", new DateOnly(2026, 7, 8), 10, updatedAt),
            Metric("pismolet.ru", new DateOnly(2026, 7, 9), 20, updatedAt),
            Metric("pismolet.ru", new DateOnly(2026, 7, 1), 30, updatedAt),
            Metric("other.example", new DateOnly(2026, 7, 9), 40, updatedAt));
        db.TroubleSnapshots.AddRange(
            new MailruPostmasterTroubleSnapshotEntity
            {
                Id = Guid.NewGuid(),
                Domain = "pismolet.ru",
                Code = 101,
                Message = "DKIM problem",
                IsActive = true,
                FirstSeenAt = updatedAt,
                LastSeenAt = updatedAt
            },
            new MailruPostmasterTroubleSnapshotEntity
            {
                Id = Guid.NewGuid(),
                Domain = "pismolet.ru",
                Code = 102,
                Message = "Resolved SPF problem",
                IsActive = false,
                FirstSeenAt = updatedAt.AddDays(-1),
                LastSeenAt = updatedAt,
                ResolvedAt = updatedAt
            });
        db.SyncStates.Add(new MailruPostmasterSyncStateEntity
        {
            Domain = "pismolet.ru",
            LastAttemptAt = updatedAt,
            LastSuccessAt = updatedAt,
            LastDomainDate = new DateOnly(2026, 7, 9),
            UpdatedAt = updatedAt
        });
        await db.SaveChangesAsync();

        var reader = new EfMailruPostmasterDashboardReader(db);
        var result = await reader.ReadAsync(
            "PISMOLET.RU.",
            new DateOnly(2026, 7, 8),
            new DateOnly(2026, 7, 9));

        Assert.Equal("pismolet.ru", result.Domain);
        Assert.Equal(2, result.Days.Count);
        Assert.Equal(30, result.Days.Sum(x => x.MessagesSent));
        Assert.Single(result.ActiveTroubles);
        Assert.Equal(101, result.ActiveTroubles[0].Code);
        Assert.NotNull(result.SyncState);
        Assert.Equal(updatedAt, result.SyncState.LastSuccessAt);
    }

    [Fact]
    public void MenuLink_IsInsertedOnceIntoAdminSidebar()
    {
        const string html = "<aside><div class='admin-sidebar-links'><a href='/dashboard'>В ЛК</a></div></aside>";

        var first = AdminMailruDeliverabilityMenuMiddleware.AddDeliverabilityLink(html);
        var second = AdminMailruDeliverabilityMenuMiddleware.AddDeliverabilityLink(first);

        Assert.Contains("/admin/deliverability/mailru", first);
        Assert.Equal(first, second);
        Assert.Equal(1, CountOccurrences(second, "/admin/deliverability/mailru"));
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

    private static MailruPostmasterDomainDailyMetricEntity Metric(
        string domain,
        DateOnly date,
        long sent,
        DateTimeOffset updatedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            Domain = domain,
            Date = date,
            MessagesSent = sent,
            Delivered = sent,
            CollectedAt = updatedAt,
            UpdatedAt = updatedAt
        };

    private static MailruPostmasterDashboardDay Day(
        DateOnly date,
        long sent,
        long delivered,
        long probablySpam,
        long spam,
        long complaints) =>
        new(
            Date: date,
            MessagesSent: sent,
            Delivered: delivered,
            ProbablySpam: probablySpam,
            Spam: spam,
            Complaints: complaints,
            Read: delivered / 2,
            DeletedRead: 1,
            DeletedUnread: 2,
            SpamPercent: MailruPostmasterDashboardCalculator.Percent(spam, sent),
            ProbablySpamPercent: MailruPostmasterDashboardCalculator.Percent(probablySpam, sent),
            Reputation: date.Day == 9 ? 0.2d : 0.1d,
            Trend: date.Day == 9 ? 0.1d : 0d,
            UpdatedAt: new DateTimeOffset(2026, 7, 10, 5, 44, 8, TimeSpan.Zero));

    private static int CountOccurrences(string value, string fragment)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
    }
}
