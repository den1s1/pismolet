using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Infrastructure.Postmaster;

namespace Pismolet.Web.Tests;

public sealed class MailruPostmasterSyncRunPersistenceTests
{
    [Fact]
    public async Task DashboardReader_ReturnsLatestRunsForRequestedDomainOnly()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MailruPostmasterDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new MailruPostmasterDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var startedAt = new DateTimeOffset(2026, 7, 10, 7, 0, 0, TimeSpan.Zero);
        db.SyncRuns.AddRange(
            Run("pismolet.ru", MailruPostmasterSyncTriggers.Scheduled, startedAt.AddMinutes(-5), "succeeded"),
            Run("pismolet.ru", MailruPostmasterSyncTriggers.Manual, startedAt, "failed", "api_error"),
            Run("other.example", MailruPostmasterSyncTriggers.Manual, startedAt.AddMinutes(1), "succeeded"));
        await db.SaveChangesAsync();

        var reader = new EfMailruPostmasterDashboardReader(db);
        var result = await reader.ReadAsync(
            "PISMOLET.RU.",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 9));

        Assert.Equal(2, result.RecentRuns.Count);
        Assert.Equal(MailruPostmasterSyncTriggers.Manual, result.RecentRuns[0].Trigger);
        Assert.Equal("failed", result.RecentRuns[0].Status);
        Assert.Equal("api_error", result.RecentRuns[0].ErrorCode);
        Assert.Equal(MailruPostmasterSyncTriggers.Scheduled, result.RecentRuns[1].Trigger);
    }

    [Fact]
    public async Task SyncRunEntity_ModelHasExpectedIndexesAndLimits()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MailruPostmasterDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new MailruPostmasterDbContext(options);

        var entity = db.Model.FindEntityType(typeof(MailruPostmasterSyncRunEntity));

        Assert.NotNull(entity);
        Assert.Equal("mailru_postmaster_sync_runs", entity.GetTableName());
        Assert.Equal(253, entity.FindProperty(nameof(MailruPostmasterSyncRunEntity.Domain))?.GetMaxLength());
        Assert.Equal(24, entity.FindProperty(nameof(MailruPostmasterSyncRunEntity.Trigger))?.GetMaxLength());
        Assert.Equal(320, entity.FindProperty(nameof(MailruPostmasterSyncRunEntity.RequestedBy))?.GetMaxLength());
        Assert.Equal(40, entity.FindProperty(nameof(MailruPostmasterSyncRunEntity.Status))?.GetMaxLength());
        Assert.Equal(120, entity.FindProperty(nameof(MailruPostmasterSyncRunEntity.ErrorCode))?.GetMaxLength());
        Assert.Contains(entity.GetIndexes(), index =>
            index.Properties.Select(x => x.Name).SequenceEqual([
                nameof(MailruPostmasterSyncRunEntity.Domain),
                nameof(MailruPostmasterSyncRunEntity.StartedAt)]));
    }

    private static MailruPostmasterSyncRunEntity Run(
        string domain,
        string trigger,
        DateTimeOffset startedAt,
        string status,
        string? errorCode = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Domain = domain,
            Trigger = trigger,
            RequestedBy = trigger == MailruPostmasterSyncTriggers.Manual ? "admin@pismolet.ru" : null,
            StartedAt = startedAt,
            CompletedAt = startedAt.AddSeconds(1),
            DurationMs = 1000,
            Status = status,
            DateFrom = new DateOnly(2026, 7, 7),
            DateTo = new DateOnly(2026, 7, 9),
            MetricDays = 3,
            TroubleCount = 0,
            ErrorCode = errorCode
        };
}
