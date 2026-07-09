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
        var synchronizer = new RecordingSynchronizer();
        using var service = CreateService(synchronizer);

        await service.StartAsync(CancellationToken.None);
        await synchronizer.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StopAsync(stopTimeout.Token);

        Assert.Equal(1, synchronizer.CallCount);
    }

    [Fact]
    public async Task HostedService_DisabledIntegrationDoesNotRunSynchronizer()
    {
        var synchronizer = new RecordingSynchronizer();
        using var service = CreateService(
            synchronizer,
            EnabledIntegrationOptions() with
            {
                Enabled = false,
                RefreshToken = string.Empty
            });

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, synchronizer.CallCount);
    }

    [Fact]
    public async Task HostedService_UnexpectedSynchronizerErrorDoesNotFailHost()
    {
        var synchronizer = new ThrowingSynchronizer();
        using var service = CreateService(synchronizer);

        await service.StartAsync(CancellationToken.None);
        await synchronizer.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StopAsync(stopTimeout.Token);

        Assert.Equal(1, synchronizer.CallCount);
    }

    [Fact]
    public async Task HostedService_StopCancelsActiveSynchronization()
    {
        var synchronizer = new CancellableSynchronizer();
        using var service = CreateService(synchronizer);

        await service.StartAsync(CancellationToken.None);
        await synchronizer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StopAsync(stopTimeout.Token);
        await synchronizer.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, synchronizer.CallCount);
    }

    [Fact]
    public void PersistenceRegistration_AddsHostedServiceOnlyOutsideInMemoryMode()
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
                descriptor.ImplementationType == typeof(MailruPostmasterSyncHostedService));

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
                descriptor.ImplementationType == typeof(MailruPostmasterSyncHostedService));
    }

    private static MailruPostmasterSyncHostedService CreateService(
        IMailruPostmasterSynchronizer synchronizer,
        MailruPostmasterOptions? integrationOptions = null) =>
        new(
            synchronizer,
            integrationOptions ?? EnabledIntegrationOptions(),
            MailruPostmasterSyncOptions.Default,
            TimeProvider.System,
            NullLogger<MailruPostmasterSyncHostedService>.Instance);

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

    private sealed class RecordingSynchronizer : IMailruPostmasterSynchronizer
    {
        private int callCount;

        public TaskCompletionSource<bool> Called { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount => Volatile.Read(ref callCount);

        public Task<MailruPostmasterSyncRunResult> RunOnceAsync(
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            Called.TrySetResult(true);
            return Task.FromResult(SuccessfulResult());
        }
    }

    private sealed class ThrowingSynchronizer : IMailruPostmasterSynchronizer
    {
        private int callCount;

        public TaskCompletionSource<bool> Called { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount => Volatile.Read(ref callCount);

        public Task<MailruPostmasterSyncRunResult> RunOnceAsync(
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            Called.TrySetResult(true);
            throw new InvalidOperationException("Тестовая неожиданная ошибка синхронизации.");
        }
    }

    private sealed class CancellableSynchronizer : IMailruPostmasterSynchronizer
    {
        private int callCount;

        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount => Volatile.Read(ref callCount);

        public async Task<MailruPostmasterSyncRunResult> RunOnceAsync(
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
