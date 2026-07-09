using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pismolet.Web.Infrastructure.Postmaster;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailruPostmasterSynchronizationTests
{
    [Fact]
    public void SyncOptions_ReadsAndClampsConfiguredValues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailruPostmaster:SyncHourMoscow"] = "99",
                ["MailruPostmaster:BackfillDays"] = "0",
                ["MailruPostmaster:ResyncRecentDays"] = "99"
            })
            .Build();

        var options = MailruPostmasterSyncOptions.Read(configuration);

        Assert.Equal(23, options.SyncHourMoscow);
        Assert.Equal(1, options.BackfillDays);
        Assert.Equal(30, options.ResyncRecentDays);
    }

    [Fact]
    public void CalculateDateRange_FirstRunUsesExactBackfillEndingYesterdayInMoscow()
    {
        var range = MailruPostmasterSynchronizer.CalculateDateRange(
            new DateTimeOffset(2026, 7, 9, 21, 30, 0, TimeSpan.Zero),
            lastDomainDate: null,
            MailruPostmasterSyncOptions.Default);

        Assert.True(range.IsBackfill);
        Assert.Equal(new DateOnly(2026, 6, 10), range.DateFrom);
        Assert.Equal(new DateOnly(2026, 7, 9), range.DateTo);
    }

    [Theory]
    [InlineData(2026, 7, 8, 2026, 7, 7)]
    [InlineData(2026, 7, 2, 2026, 7, 3)]
    public void CalculateDateRange_ResyncsRecentDaysAndRestoresGaps(
        int lastYear,
        int lastMonth,
        int lastDay,
        int expectedFromYear,
        int expectedFromMonth,
        int expectedFromDay)
    {
        var range = MailruPostmasterSynchronizer.CalculateDateRange(
            new DateTimeOffset(2026, 7, 10, 7, 0, 0, TimeSpan.Zero),
            new DateOnly(lastYear, lastMonth, lastDay),
            MailruPostmasterSyncOptions.Default);

        Assert.False(range.IsBackfill);
        Assert.Equal(new DateOnly(expectedFromYear, expectedFromMonth, expectedFromDay), range.DateFrom);
        Assert.Equal(new DateOnly(2026, 7, 9), range.DateTo);
    }

    [Fact]
    public async Task RunOnce_FirstBackfillPersistsMetricsTroublesAndSyncState()
    {
        var client = new StubMailruPostmasterClient
        {
            TroublesResult = MailruPostmasterResult<IReadOnlyList<MailruPostmasterTrouble>>.Success(
            [
                new MailruPostmasterTrouble("pismolet.ru", -1, "DMARC check failed.")
            ]),
            DetailedStatisticsResult = MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>.Success(
            [
                CreateMetric(new DateOnly(2026, 6, 10), 10, 7),
                CreateMetric(new DateOnly(2026, 7, 9), 20, 18)
            ])
        };
        await using var context = await SyncTestContext.CreateAsync(client);

        var result = await context.Synchronizer.RunOnceAsync();

        Assert.Equal(MailruPostmasterSyncRunStatus.Succeeded, result.Status);
        Assert.Equal(new DateOnly(2026, 6, 10), result.DateFrom);
        Assert.Equal(new DateOnly(2026, 7, 9), result.DateTo);
        Assert.Equal(2, result.MetricDays);
        Assert.Equal(1, result.TroubleCount);
        Assert.Equal(
            (new DateOnly(2026, 6, 10), new DateOnly(2026, 7, 9), "pismolet.ru"),
            Assert.Single(client.DetailedRequests));

        var metrics = await context.LoadMetricsAsync();
        Assert.Equal(2, metrics.Count);
        Assert.Equal(20, metrics.Single(x => x.Date == new DateOnly(2026, 7, 9)).MessagesSent);

        var trouble = Assert.Single(await context.LoadTroublesAsync());
        Assert.True(trouble.IsActive);
        Assert.Equal(-1, trouble.Code);

        var state = await context.LoadStateAsync();
        Assert.NotNull(state);
        Assert.Equal(new DateOnly(2026, 7, 9), state!.LastDomainDate);
        Assert.Equal(0, state.ConsecutiveFailures);
        Assert.NotNull(state.LastSuccessAt);
    }

    [Fact]
    public async Task RunOnce_RepeatedRunResyncsRecentRangeAndDoesNotCreateDuplicates()
    {
        var client = new StubMailruPostmasterClient
        {
            DetailedStatisticsResult = MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>.Success(
            [
                CreateMetric(new DateOnly(2026, 7, 9), 10, 8)
            ])
        };
        await using var context = await SyncTestContext.CreateAsync(client);

        var first = await context.Synchronizer.RunOnceAsync();
        client.DetailedStatisticsResult = MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>.Success(
        [
            CreateMetric(new DateOnly(2026, 7, 9), 12, 11)
        ]);
        var second = await context.Synchronizer.RunOnceAsync();

        Assert.Equal(MailruPostmasterSyncRunStatus.Succeeded, first.Status);
        Assert.Equal(MailruPostmasterSyncRunStatus.Succeeded, second.Status);
        Assert.Equal(2, client.DetailedRequests.Count);
        Assert.Equal(
            (new DateOnly(2026, 7, 7), new DateOnly(2026, 7, 9), "pismolet.ru"),
            client.DetailedRequests[1]);

        var metric = Assert.Single(await context.LoadMetricsAsync());
        Assert.Equal(12, metric.MessagesSent);
        Assert.Equal(11, metric.Delivered);
    }

    [Fact]
    public async Task RunOnce_ApiFailurePersistsFailureAndNextRunRecovers()
    {
        var client = new StubMailruPostmasterClient();
        client.DetailedResults.Enqueue(
            MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>.Failure(
                "api_timeout",
                "Истекло время ожидания ответа Mail.ru."));
        client.DetailedResults.Enqueue(
            MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>.Success(
            [
                CreateMetric(new DateOnly(2026, 7, 9), 5, 4)
            ]));
        await using var context = await SyncTestContext.CreateAsync(client);

        var failed = await context.Synchronizer.RunOnceAsync();
        var failedState = await context.LoadStateAsync();
        Assert.NotNull(failedState);

        var recovered = await context.Synchronizer.RunOnceAsync();
        var recoveredState = await context.LoadStateAsync();
        Assert.NotNull(recoveredState);

        Assert.Equal(MailruPostmasterSyncRunStatus.Failed, failed.Status);
        Assert.Equal("api_timeout", failed.ErrorCode);
        Assert.Equal(1, failedState!.ConsecutiveFailures);
        Assert.Equal("api_timeout", failedState.LastErrorCode);
        Assert.Null(failedState.LastSuccessAt);

        Assert.Equal(MailruPostmasterSyncRunStatus.Succeeded, recovered.Status);
        Assert.Equal(0, recoveredState!.ConsecutiveFailures);
        Assert.Null(recoveredState.LastErrorCode);
        Assert.NotNull(recoveredState.LastSuccessAt);
        Assert.Single(await context.LoadMetricsAsync());
    }

    [Fact]
    public async Task RunOnce_ConcurrentRunIsSkippedWithoutSecondApiRequest()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new StubMailruPostmasterClient
        {
            DetailedHandler = async (dateFrom, dateTo, domain, cancellationToken) =>
            {
                started.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken);
                return MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>.Success(
                [
                    CreateMetric(dateTo ?? dateFrom, 3, 3)
                ]);
            }
        };
        await using var context = await SyncTestContext.CreateAsync(client);

        var firstRun = context.Synchronizer.RunOnceAsync();
        await started.Task;
        var secondRun = await context.Synchronizer.RunOnceAsync();
        release.TrySetResult(true);
        var firstResult = await firstRun;

        Assert.Equal(MailruPostmasterSyncRunStatus.SkippedAlreadyRunning, secondRun.Status);
        Assert.Equal(MailruPostmasterSyncRunStatus.Succeeded, firstResult.Status);
        Assert.Equal(1, client.DetailedCallCount);
    }

    [Fact]
    public async Task RunOnce_DisabledIntegrationDoesNotResolveStorageOrCallApi()
    {
        var client = new StubMailruPostmasterClient();
        var synchronizer = new MailruPostmasterSynchronizer(
            client,
            new ThrowingScopeFactory(),
            EnabledIntegrationOptions() with { Enabled = false, RefreshToken = string.Empty },
            MailruPostmasterSyncOptions.Default,
            new FixedTimeProvider(TestNow),
            NullLogger<MailruPostmasterSynchronizer>.Instance);

        var result = await synchronizer.RunOnceAsync();

        Assert.Equal(MailruPostmasterSyncRunStatus.Disabled, result.Status);
        Assert.Equal(0, client.RegisteredDomainsCallCount);
        Assert.Equal(0, client.DetailedCallCount);
    }

    [Fact]
    public async Task RunOnce_StorageFailureIsContainedAndReturnedAsFailure()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMailruPostmasterStorage, ThrowingStorage>();
        using var provider = services.BuildServiceProvider();
        var synchronizer = new MailruPostmasterSynchronizer(
            new StubMailruPostmasterClient(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            EnabledIntegrationOptions(),
            MailruPostmasterSyncOptions.Default,
            new FixedTimeProvider(TestNow),
            NullLogger<MailruPostmasterSynchronizer>.Instance);

        var result = await synchronizer.RunOnceAsync();

        Assert.Equal(MailruPostmasterSyncRunStatus.Failed, result.Status);
        Assert.Equal("postmaster_storage_error", result.ErrorCode);
    }

    private static readonly DateTimeOffset TestNow =
        new(2026, 7, 10, 7, 0, 0, TimeSpan.Zero);

    private static MailruPostmasterOptions EnabledIntegrationOptions() => new(
        Enabled: true,
        Domain: "pismolet.ru",
        RefreshToken: "test-refresh-token",
        OAuthBaseUri: new Uri("https://o2.mail.ru/"),
        ApiBaseUri: new Uri("https://postmaster.mail.ru/"),
        RequestTimeout: TimeSpan.FromSeconds(15),
        MaxRateLimitDelay: TimeSpan.FromSeconds(10));

    private static MailruPostmasterDailyStatistics CreateMetric(
        DateOnly date,
        long messagesSent,
        long delivered) =>
        new(
            Domain: "pismolet.ru",
            Date: date,
            MessagesSent: messagesSent,
            Delivered: delivered,
            ProbablySpam: messagesSent - delivered,
            Spam: 0,
            Complaints: 0,
            Read: delivered,
            DeletedRead: 0,
            DeletedUnread: 0,
            SpamPercent: 0,
            ProbablySpamPercent: messagesSent == 0 ? 0 : (messagesSent - delivered) * 100d / messagesSent,
            Reputation: 0,
            Trend: 0);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StubMailruPostmasterClient : IMailruPostmasterClient
    {
        public MailruPostmasterResult<IReadOnlyList<MailruPostmasterRegisteredDomain>> RegisteredDomainsResult { get; set; } =
            MailruPostmasterResult<IReadOnlyList<MailruPostmasterRegisteredDomain>>.Success(
            [
                new MailruPostmasterRegisteredDomain("pismolet.ru")
            ]);

        public MailruPostmasterResult<IReadOnlyList<MailruPostmasterTrouble>> TroublesResult { get; set; } =
            MailruPostmasterResult<IReadOnlyList<MailruPostmasterTrouble>>.Success([]);

        public MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>> DetailedStatisticsResult { get; set; } =
            MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>.Success([]);

        public Queue<MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>> DetailedResults { get; } = new();

        public Func<DateOnly, DateOnly?, string?, CancellationToken, Task<MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>>>? DetailedHandler { get; set; }

        public int RegisteredDomainsCallCount { get; private set; }
        public int DetailedCallCount { get; private set; }
        public List<(DateOnly DateFrom, DateOnly DateTo, string Domain)> DetailedRequests { get; } = [];

        public Task<MailruPostmasterResult<IReadOnlyList<MailruPostmasterRegisteredDomain>>> GetRegisteredDomainsAsync(
            CancellationToken cancellationToken = default)
        {
            RegisteredDomainsCallCount++;
            return Task.FromResult(RegisteredDomainsResult);
        }

        public Task<MailruPostmasterResult<IReadOnlyList<MailruPostmasterTrouble>>> GetTroublesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(TroublesResult);

        public Task<MailruPostmasterResult<IReadOnlyList<MailruPostmasterStatistics>>> GetStatisticsAsync(
            DateOnly dateFrom,
            DateOnly? dateTo = null,
            string? domain = null,
            string? messageType = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                MailruPostmasterResult<IReadOnlyList<MailruPostmasterStatistics>>.Success([]));

        public async Task<MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>> GetDetailedStatisticsAsync(
            DateOnly dateFrom,
            DateOnly? dateTo = null,
            string? domain = null,
            string? messageType = null,
            CancellationToken cancellationToken = default)
        {
            DetailedCallCount++;
            DetailedRequests.Add((dateFrom, dateTo ?? dateFrom, domain ?? string.Empty));

            if (DetailedHandler is not null)
            {
                return await DetailedHandler(dateFrom, dateTo, domain, cancellationToken);
            }

            return DetailedResults.Count > 0
                ? DetailedResults.Dequeue()
                : DetailedStatisticsResult;
        }
    }

    private sealed class SyncTestContext : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider provider;

        private SyncTestContext(
            SqliteConnection connection,
            ServiceProvider provider,
            IMailruPostmasterSynchronizer synchronizer)
        {
            this.connection = connection;
            this.provider = provider;
            Synchronizer = synchronizer;
        }

        public IMailruPostmasterSynchronizer Synchronizer { get; }

        public static async Task<SyncTestContext> CreateAsync(StubMailruPostmasterClient client)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddScoped(_ => new MailruPostmasterDbContext(
                new DbContextOptionsBuilder<MailruPostmasterDbContext>()
                    .UseSqlite(connection)
                    .Options));
            services.AddScoped<IMailruPostmasterStorage, EfMailruPostmasterStorage>();
            var provider = services.BuildServiceProvider();

            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MailruPostmasterDbContext>();
                await db.Database.EnsureCreatedAsync();
            }

            var synchronizer = new MailruPostmasterSynchronizer(
                client,
                provider.GetRequiredService<IServiceScopeFactory>(),
                EnabledIntegrationOptions(),
                MailruPostmasterSyncOptions.Default,
                new FixedTimeProvider(TestNow),
                NullLogger<MailruPostmasterSynchronizer>.Instance);

            return new SyncTestContext(connection, provider, synchronizer);
        }

        public async Task<List<MailruPostmasterDomainDailyMetricEntity>> LoadMetricsAsync()
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MailruPostmasterDbContext>();
            return await db.DomainDailyMetrics.AsNoTracking().OrderBy(x => x.Date).ToListAsync();
        }

        public async Task<List<MailruPostmasterTroubleSnapshotEntity>> LoadTroublesAsync()
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MailruPostmasterDbContext>();
            return await db.TroubleSnapshots.AsNoTracking().OrderBy(x => x.Code).ToListAsync();
        }

        public async Task<MailruPostmasterSyncStateEntity?> LoadStateAsync()
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MailruPostmasterDbContext>();
            return await db.SyncStates.AsNoTracking().SingleOrDefaultAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() =>
            throw new InvalidOperationException("Storage не должен разрешаться при отключённой интеграции.");
    }

    private sealed class ThrowingStorage : IMailruPostmasterStorage
    {
        public Task UpsertDomainDailyMetricsAsync(
            IReadOnlyCollection<MailruPostmasterDailyStatistics> metrics,
            DateTimeOffset collectedAt,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Тестовая ошибка storage.");

        public Task SynchronizeTroublesAsync(
            string domain,
            IReadOnlyCollection<MailruPostmasterTrouble> troubles,
            DateTimeOffset observedAt,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Тестовая ошибка storage.");

        public Task<MailruPostmasterSyncStateEntity?> GetSyncStateAsync(
            string domain,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Тестовая ошибка storage.");

        public Task MarkAttemptAsync(
            string domain,
            DateTimeOffset attemptedAt,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Тестовая ошибка storage.");

        public Task MarkSuccessAsync(
            string domain,
            DateTimeOffset succeededAt,
            DateOnly? lastDomainDate,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Тестовая ошибка storage.");

        public Task MarkFailureAsync(
            string domain,
            DateTimeOffset failedAt,
            string errorCode,
            string errorSummary,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Тестовая ошибка storage.");
    }
}
