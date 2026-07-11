using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Infrastructure.Postmaster;

namespace Pismolet.Web.Tests;

public sealed class MailruPostmasterAlertJournalStorageTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApplyAsync_ActivatedEvent_IsPersistedAndNormalized()
    {
        await using var fixture = await StorageFixture.CreateAsync();
        var source = Event(
            id: Guid.NewGuid(),
            domain: " PISMOLET.RU. ",
            code: " SPAM_DETECTED ",
            severity: MailruPostmasterAlertSeverity.Critical,
            status: MailruPostmasterAlertEventStatus.Active,
            fingerprint: new string('A', 64));

        await fixture.Store.ApplyAsync([
            new MailruPostmasterAlertJournalChange(
                MailruPostmasterAlertJournalChangeType.Activated,
                source)
        ]);

        var active = await fixture.Store.ReadActiveAsync("pismolet.ru");
        var saved = Assert.Single(active);
        Assert.Equal(source.Id, saved.Id);
        Assert.Equal("pismolet.ru", saved.Domain);
        Assert.Equal("spam_detected", saved.Code);
        Assert.Equal(new string('a', 64), saved.Fingerprint);
        Assert.Equal(MailruPostmasterAlertSeverity.Critical, saved.Severity);
        Assert.Equal(MailruPostmasterAlertEventStatus.Active, saved.Status);
        Assert.Equal(1, saved.OccurrenceCount);
    }

    [Fact]
    public async Task ApplyAsync_UpdatedEvent_UpdatesExistingRowWithoutDuplicate()
    {
        await using var fixture = await StorageFixture.CreateAsync();
        var id = Guid.NewGuid();
        var first = Event(id: id, observedValue: 10, occurrenceCount: 1);
        await fixture.Store.ApplyAsync([
            new MailruPostmasterAlertJournalChange(
                MailruPostmasterAlertJournalChangeType.Activated,
                first)
        ]);

        var updatedAt = Now.AddHours(1);
        var updated = first with
        {
            ObservedValue = 25,
            MessagesSent = 200,
            LastObservedAt = updatedAt,
            OccurrenceCount = 2,
            UpdatedAt = updatedAt
        };
        await fixture.Store.ApplyAsync([
            new MailruPostmasterAlertJournalChange(
                MailruPostmasterAlertJournalChangeType.Updated,
                updated)
        ]);

        var rows = await fixture.Store.ReadRecentAsync("pismolet.ru");
        var saved = Assert.Single(rows);
        Assert.Equal(25, saved.ObservedValue);
        Assert.Equal(200, saved.MessagesSent);
        Assert.Equal(2, saved.OccurrenceCount);
        Assert.Equal(first.FirstObservedAt, saved.FirstObservedAt);
        Assert.Equal(first.CreatedAt, saved.CreatedAt);
        Assert.Equal(updatedAt, saved.UpdatedAt);
        Assert.Equal(1, await fixture.Db.AlertEvents.CountAsync());
    }

    [Fact]
    public async Task ApplyAsync_ResolvedEvent_IsRemovedFromActiveAndKeptInHistory()
    {
        await using var fixture = await StorageFixture.CreateAsync();
        var first = Event();
        await fixture.Store.ApplyAsync([
            new MailruPostmasterAlertJournalChange(
                MailruPostmasterAlertJournalChangeType.Activated,
                first)
        ]);

        var resolvedAt = Now.AddHours(2);
        var resolved = first with
        {
            Status = MailruPostmasterAlertEventStatus.Resolved,
            ResolvedAt = resolvedAt,
            UpdatedAt = resolvedAt
        };
        await fixture.Store.ApplyAsync([
            new MailruPostmasterAlertJournalChange(
                MailruPostmasterAlertJournalChangeType.Resolved,
                resolved)
        ]);

        Assert.Empty(await fixture.Store.ReadActiveAsync("pismolet.ru"));
        var history = await fixture.Store.ReadRecentAsync(
            "pismolet.ru",
            status: MailruPostmasterAlertEventStatus.Resolved);
        var saved = Assert.Single(history);
        Assert.Equal(MailruPostmasterAlertEventStatus.Resolved, saved.Status);
        Assert.Equal(resolvedAt, saved.ResolvedAt);
    }

    [Fact]
    public async Task ReadRecentAsync_FiltersByStatusSeverityAndDomain()
    {
        await using var fixture = await StorageFixture.CreateAsync();
        var critical = Event(
            id: Guid.NewGuid(),
            code: "spam_detected",
            severity: MailruPostmasterAlertSeverity.Critical,
            status: MailruPostmasterAlertEventStatus.Active,
            fingerprint: new string('1', 64));
        var warning = Event(
            id: Guid.NewGuid(),
            code: "probably_spam_high",
            severity: MailruPostmasterAlertSeverity.Warning,
            status: MailruPostmasterAlertEventStatus.Active,
            fingerprint: new string('2', 64));
        var foreign = Event(
            id: Guid.NewGuid(),
            domain: "example.org",
            code: "spam_detected",
            severity: MailruPostmasterAlertSeverity.Critical,
            status: MailruPostmasterAlertEventStatus.Active,
            fingerprint: new string('3', 64));

        await fixture.Store.ApplyAsync([
            new MailruPostmasterAlertJournalChange(MailruPostmasterAlertJournalChangeType.Activated, critical),
            new MailruPostmasterAlertJournalChange(MailruPostmasterAlertJournalChangeType.Activated, warning),
            new MailruPostmasterAlertJournalChange(MailruPostmasterAlertJournalChangeType.Activated, foreign)
        ]);

        var result = await fixture.Store.ReadRecentAsync(
            "pismolet.ru",
            status: MailruPostmasterAlertEventStatus.Active,
            severity: MailruPostmasterAlertSeverity.Critical,
            take: 100);

        var saved = Assert.Single(result);
        Assert.Equal(critical.Id, saved.Id);
    }

    [Fact]
    public async Task ReadRecentAsync_ClampsTakeAndReturnsNewestFirst()
    {
        await using var fixture = await StorageFixture.CreateAsync();
        var older = Event(
            id: Guid.NewGuid(),
            code: "older",
            fingerprint: new string('4', 64),
            updatedAt: Now);
        var newer = Event(
            id: Guid.NewGuid(),
            code: "newer",
            fingerprint: new string('5', 64),
            updatedAt: Now.AddMinutes(1));

        await fixture.Store.ApplyAsync([
            new MailruPostmasterAlertJournalChange(MailruPostmasterAlertJournalChangeType.Activated, older),
            new MailruPostmasterAlertJournalChange(MailruPostmasterAlertJournalChangeType.Activated, newer)
        ]);

        var result = await fixture.Store.ReadRecentAsync("pismolet.ru", take: 1);

        var saved = Assert.Single(result);
        Assert.Equal(newer.Id, saved.Id);
    }

    [Fact]
    public async Task ApplyAsync_EmptyChangeSet_DoesNotWriteRows()
    {
        await using var fixture = await StorageFixture.CreateAsync();

        await fixture.Store.ApplyAsync(Array.Empty<MailruPostmasterAlertJournalChange>());

        Assert.Equal(0, await fixture.Db.AlertEvents.CountAsync());
    }

    [Fact]
    public async Task EmptyStore_IsSafeForInMemoryApplicationMode()
    {
        var store = new EmptyMailruPostmasterAlertJournalStore();

        Assert.Empty(await store.ReadActiveAsync("pismolet.ru"));
        Assert.Empty(await store.ReadRecentAsync("pismolet.ru"));
        await store.ApplyAsync(Array.Empty<MailruPostmasterAlertJournalChange>());
    }

    private static MailruPostmasterAlertJournalEvent Event(
        Guid? id = null,
        string domain = "pismolet.ru",
        string code = "probably_spam_high",
        MailruPostmasterAlertSeverity severity = MailruPostmasterAlertSeverity.Warning,
        MailruPostmasterAlertEventStatus status = MailruPostmasterAlertEventStatus.Active,
        string? fingerprint = null,
        long messagesSent = 100,
        double observedValue = 10,
        int occurrenceCount = 1,
        DateTimeOffset? updatedAt = null)
    {
        var timestamp = updatedAt ?? Now;
        return new MailruPostmasterAlertJournalEvent(
            id ?? Guid.NewGuid(),
            domain,
            code,
            severity,
            "probably_spam",
            status,
            fingerprint ?? new string('f', 64),
            new DateOnly(2026, 7, 5),
            new DateOnly(2026, 7, 11),
            messagesSent,
            observedValue,
            10,
            Now,
            timestamp,
            occurrenceCount,
            status == MailruPostmasterAlertEventStatus.Resolved ? timestamp : null,
            null,
            Now,
            timestamp);
    }

    private sealed class StorageFixture : IAsyncDisposable
    {
        private StorageFixture(
            SqliteConnection connection,
            MailruPostmasterDbContext db)
        {
            Connection = connection;
            Db = db;
            Store = new EfMailruPostmasterAlertJournalStore(db);
        }

        public SqliteConnection Connection { get; }
        public MailruPostmasterDbContext Db { get; }
        public EfMailruPostmasterAlertJournalStore Store { get; }

        public static async Task<StorageFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<MailruPostmasterDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new MailruPostmasterDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new StorageFixture(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
