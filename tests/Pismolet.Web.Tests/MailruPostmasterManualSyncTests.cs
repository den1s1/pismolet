using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Pismolet.Web.Infrastructure.Postmaster;

namespace Pismolet.Web.Tests;

public sealed class MailruPostmasterManualSyncTests
{
    [Fact]
    public async Task Executor_WritesCompletedManualRunToJournal()
    {
        var journal = new InMemoryMailruPostmasterSyncRunJournal();
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 10, 7, 0, 0, TimeSpan.Zero));
        var executor = new MailruPostmasterSyncExecutor(
            new SuccessfulSynchronizer(),
            journal,
            EnabledIntegrationOptions(),
            timeProvider,
            NullLogger<MailruPostmasterSyncExecutor>.Instance);

        var result = await executor.RunAsync(
            MailruPostmasterSyncTriggers.Manual,
            "admin@pismolet.ru");

        Assert.Equal(MailruPostmasterSyncRunStatus.Succeeded, result.Status);
        var run = Assert.Single(journal.Snapshot());
        Assert.Equal("pismolet.ru", run.Domain);
        Assert.Equal(MailruPostmasterSyncTriggers.Manual, run.Trigger);
        Assert.Equal("admin@pismolet.ru", run.RequestedBy);
        Assert.Equal("succeeded", run.Status);
        Assert.Equal(new DateOnly(2026, 7, 7), run.DateFrom);
        Assert.Equal(new DateOnly(2026, 7, 9), run.DateTo);
        Assert.Equal(3, run.MetricDays);
        Assert.Equal(0, run.TroubleCount);
        Assert.NotNull(run.CompletedAt);
    }

    [Fact]
    public async Task ManualService_RateLimitsRepeatedRunAndAllowsAfterCooldown()
    {
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 10, 7, 0, 0, TimeSpan.Zero));
        var executor = new RecordingExecutor();
        var service = CreateManualService(
            executor,
            new InMemoryMailruPostmasterSyncRunJournal(),
            timeProvider,
            cooldownSeconds: 60);

        var first = await service.RunAsync("admin@pismolet.ru");
        var second = await service.RunAsync("admin@pismolet.ru");

        Assert.Equal(MailruPostmasterManualSyncStatus.Succeeded, first.Status);
        Assert.Equal(MailruPostmasterManualSyncStatus.RateLimited, second.Status);
        Assert.InRange(second.RetryAfterSeconds ?? 0, 59, 60);
        Assert.Equal(1, executor.CallCount);

        timeProvider.Advance(TimeSpan.FromSeconds(60));
        var third = await service.RunAsync("admin@pismolet.ru");

        Assert.Equal(MailruPostmasterManualSyncStatus.Succeeded, third.Status);
        Assert.Equal(2, executor.CallCount);
    }

    [Fact]
    public async Task ManualService_RejectsParallelManualRequest()
    {
        var executor = new BlockingExecutor();
        var service = CreateManualService(
            executor,
            new InMemoryMailruPostmasterSyncRunJournal(),
            new MutableTimeProvider(new DateTimeOffset(2026, 7, 10, 7, 0, 0, TimeSpan.Zero)),
            cooldownSeconds: 60);

        var firstTask = service.RunAsync("first@pismolet.ru");
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = await service.RunAsync("second@pismolet.ru");

        Assert.Equal(MailruPostmasterManualSyncStatus.AlreadyRunning, second.Status);
        executor.Release.TrySetResult(true);
        var first = await firstTask;
        Assert.Equal(MailruPostmasterManualSyncStatus.Succeeded, first.Status);
        Assert.Equal(1, executor.CallCount);
    }

    [Fact]
    public async Task ManualService_ReturnsDisabledWithoutCallingExecutor()
    {
        var executor = new RecordingExecutor();
        var service = new MailruPostmasterManualSyncService(
            executor,
            new InMemoryMailruPostmasterSyncRunJournal(),
            EnabledIntegrationOptions() with { Enabled = false, RefreshToken = string.Empty },
            new MailruPostmasterManualSyncOptions(60),
            TimeProvider.System,
            NullLogger<MailruPostmasterManualSyncService>.Instance);

        var result = await service.RunAsync("admin@pismolet.ru");

        Assert.Equal(MailruPostmasterManualSyncStatus.Disabled, result.Status);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public void ManualOptions_UseDefaultAndClampConfiguredValue()
    {
        var defaults = MailruPostmasterManualSyncOptions.Read(
            new ConfigurationBuilder().Build());
        var clamped = MailruPostmasterManualSyncOptions.Read(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MailruPostmaster:ManualSyncCooldownSeconds"] = "1"
                })
                .Build());

        Assert.Equal(MailruPostmasterManualSyncOptions.DefaultCooldownSeconds, defaults.CooldownSeconds);
        Assert.Equal(MailruPostmasterManualSyncOptions.MinCooldownSeconds, clamped.CooldownSeconds);
    }

    private static MailruPostmasterManualSyncService CreateManualService(
        IMailruPostmasterSyncExecutor executor,
        IMailruPostmasterSyncRunJournal journal,
        TimeProvider timeProvider,
        int cooldownSeconds) =>
        new(
            executor,
            journal,
            EnabledIntegrationOptions(),
            new MailruPostmasterManualSyncOptions(cooldownSeconds),
            timeProvider,
            NullLogger<MailruPostmasterManualSyncService>.Instance);

    private static MailruPostmasterOptions EnabledIntegrationOptions() => new(
        Enabled: true,
        Domain: "pismolet.ru",
        RefreshToken: "test-refresh-token",
        OAuthBaseUri: new Uri("https://o2.mail.ru/"),
        ApiBaseUri: new Uri("https://postmaster.mail.ru/"),
        RequestTimeout: TimeSpan.FromSeconds(15),
        MaxRateLimitDelay: TimeSpan.FromSeconds(10));

    private static MailruPostmasterSyncRunResult SuccessfulResult() =>
        MailruPostmasterSyncRunResult.Succeeded(
            new MailruPostmasterSyncDateRange(
                new DateOnly(2026, 7, 7),
                new DateOnly(2026, 7, 9),
                IsBackfill: false),
            metricDays: 3,
            troubleCount: 0);

    private sealed class SuccessfulSynchronizer : IMailruPostmasterSynchronizer
    {
        public Task<MailruPostmasterSyncRunResult> RunOnceAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SuccessfulResult());
    }

    private sealed class RecordingExecutor : IMailruPostmasterSyncExecutor
    {
        private int callCount;
        public int CallCount => Volatile.Read(ref callCount);

        public Task<MailruPostmasterSyncRunResult> RunAsync(
            string trigger,
            string? requestedBy = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(SuccessfulResult());
        }
    }

    private sealed class BlockingExecutor : IMailruPostmasterSyncExecutor
    {
        private int callCount;

        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount => Volatile.Read(ref callCount);

        public async Task<MailruPostmasterSyncRunResult> RunAsync(
            string trigger,
            string? requestedBy = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            return SuccessfulResult();
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset current = utcNow;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan value) => current = current.Add(value);
    }
}
