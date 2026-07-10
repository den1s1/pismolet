using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Pismolet.Web.Infrastructure.Postmaster;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailruPostmasterSyncHostedServiceTests
{
    [Fact]
    public void Schedule_UsesConfiguredMoscowHourAndMovesExactTimeToNextDay()
    {
        var beforeConfiguredHour = MailruPostmasterSyncSchedule.GetNextRunAtUtc(
            new DateTimeOffset(2026, 7, 10, 2, 0, 0, TimeSpan.Zero),
            syncHourMoscow: 6);
        var exactlyConfiguredHour = MailruPostmasterSyncSchedule.GetNextRunAtUtc(
            new DateTimeOffset(2026, 7, 10, 3, 0, 0, TimeSpan.Zero),
            syncHourMoscow: 6);
        var afterConfiguredHour = MailruPostmasterSyncSchedule.GetNextRunAtUtc(
            new DateTimeOffset(2026, 7, 10, 20, 0, 0, TimeSpan.Zero),
            syncHourMoscow: 6);

        Assert.Equal(
            new DateTimeOffset(2026, 7, 10, 3, 0, 0, TimeSpan.Zero),
            beforeConfiguredHour);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 11, 3, 0, 0, TimeSpan.Zero),
            exactlyConfiguredHour);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 11, 3, 0, 0, TimeSpan.Zero),
            afterConfiguredHour);
    }

    [Fact]
    public async Task HostedService_RunsImmediatelyThenStopsDuringScheduledDelay()
    {
        var executor = new RecordingExecutor();
        using var service = CreateService(executor);

        await service.StartAsync(CancellationToken.None);
        await executor.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StopAsync(stopTimeout.Token);

        Assert.Equal(1, executor.CallCount);
        Assert.Equal(MailruPostmasterSyncTriggers.Scheduled, executor.LastTrigger);
    }

    [Fact]
    public async Task HostedService_DisabledIntegrationDoesNotRunExecutor()
    {
        var executor = new RecordingExecutor();
        using var service = CreateService(
            executor,
            EnabledIntegrationOptions() with
            {
                Enabled = false,
                RefreshToken = string.Empty
            });

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task HostedService_UnexpectedExecutorErrorDoesNotFailHost()
    {
        var executor = new ThrowingExecutor();
        using var service = CreateService(executor);

        await service.StartAsync(CancellationToken.None);
        await executor.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StopAsync(stopTimeout.Token);

        Assert.Equal(1, executor.CallCount);
    }

    [Fact]
    public async Task HostedService_StopCancelsActiveSynchronization()
    {
        var executor = new CancellableExecutor();
        using var service = CreateService(executor);

        await service.StartAsync(CancellationToken.None);
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StopAsync(stopTimeout.Token);
        await executor.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, executor.CallCount);
    }

    [Fact]
    public void PersistenceRegistration_AddsJournaledHostedServiceOnlyOutsideInMemoryMode()
    {
        var productionServices = new ServiceCollection();
        var productionConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PismoletDb"] =
                    "Host=localhost;Database=pismolet;Username=pismolet;Password=test"
            })
            .Build();

        productionServices.AddMailruPostmasterPersistence(productionConfiguration);

        Assert.Contains(
            productionServices,
            descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(MailruPostmasterJournaledSyncHostedService));
        Assert.Contains(
            productionServices,
            descriptor => descriptor.ServiceType == typeof(IMailruPostmasterManualSyncService));
        Assert.Contains(
            productionServices,
            descriptor => descriptor.ServiceType == typeof(IMailruPostmasterSyncRunJournal));

        var inMemoryServices = new ServiceCollection();
        var inMemoryConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "InMemory"
            })
            .Build();

        inMemoryServices.AddMailruPostmasterPersistence(inMemoryConfiguration);

        Assert.DoesNotContain(
            inMemoryServices,
            descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(MailruPostmasterJournaledSyncHostedService));
        Assert.Contains(
            inMemoryServices,
            descriptor => descriptor.ServiceType == typeof(IMailruPostmasterManualSyncService));
    }

    private static MailruPostmasterJournaledSyncHostedService CreateService(
        IMailruPostmasterSyncExecutor executor,
        MailruPostmasterOptions? integrationOptions = null) =>
        new(
            executor,
            integrationOptions ?? EnabledIntegrationOptions(),
            MailruPostmasterSyncOptions.Default,
            TimeProvider.System,
            NullLogger<MailruPostmasterJournaledSyncHostedService>.Instance);

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
            metricDays: 0,
            troubleCount: 0);

    private sealed class RecordingExecutor : IMailruPostmasterSyncExecutor
    {
        private int callCount;

        public TaskCompletionSource<bool> Called { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount => Volatile.Read(ref callCount);
        public string? LastTrigger { get; private set; }

        public Task<MailruPostmasterSyncRunResult> RunAsync(
            string trigger,
            string? requestedBy = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            LastTrigger = trigger;
            Called.TrySetResult(true);
            return Task.FromResult(SuccessfulResult());
        }
    }

    private sealed class ThrowingExecutor : IMailruPostmasterSyncExecutor
    {
        private int callCount;

        public TaskCompletionSource<bool> Called { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount => Volatile.Read(ref callCount);

        public Task<MailruPostmasterSyncRunResult> RunAsync(
            string trigger,
            string? requestedBy = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            Called.TrySetResult(true);
            throw new InvalidOperationException("Тестовая неожиданная ошибка синхронизации.");
        }
    }

    private sealed class CancellableExecutor : IMailruPostmasterSyncExecutor
    {
        private int callCount;

        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount => Volatile.Read(ref callCount);

        public async Task<MailruPostmasterSyncRunResult> RunAsync(
            string trigger,
            string? requestedBy = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            Started.TrySetResult(true);

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return SuccessfulResult();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled.TrySetResult(true);
                throw;
            }
        }
    }
}
