using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pismolet.Web.Infrastructure.Postmaster;

public sealed class MailruPostmasterJournaledSyncHostedService(
    IMailruPostmasterSyncExecutor executor,
    MailruPostmasterOptions integrationOptions,
    MailruPostmasterSyncOptions syncOptions,
    TimeProvider timeProvider,
    ILogger<MailruPostmasterJournaledSyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!integrationOptions.Enabled)
        {
            logger.LogInformation("Mail.ru Postmaster background synchronization is disabled.");
            return;
        }

        if (!integrationOptions.IsConfigured)
        {
            logger.LogWarning(
                "Mail.ru Postmaster background synchronization is not configured. domain={Domain}",
                integrationOptions.Domain);
            return;
        }

        logger.LogInformation(
            "Mail.ru Postmaster background synchronization started. domain={Domain} syncHourMoscow={SyncHourMoscow} backfillDays={BackfillDays} resyncRecentDays={ResyncRecentDays}",
            integrationOptions.Domain,
            syncOptions.SyncHourMoscow,
            syncOptions.BackfillDays,
            syncOptions.ResyncRecentDays);

        if (!await RunOnceSafelyAsync(stoppingToken))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();
            var nextRunAt = MailruPostmasterSyncSchedule.GetNextRunAtUtc(now, syncOptions.SyncHourMoscow);
            var delay = nextRunAt - now;

            logger.LogInformation(
                "Mail.ru Postmaster next synchronization scheduled. domain={Domain} nextRunAtUtc={NextRunAtUtc} delaySeconds={DelaySeconds}",
                integrationOptions.Domain,
                nextRunAt,
                Math.Max(0, delay.TotalSeconds));

            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (!await RunOnceSafelyAsync(stoppingToken))
            {
                return;
            }
        }
    }

    private async Task<bool> RunOnceSafelyAsync(CancellationToken stoppingToken)
    {
        try
        {
            await executor.RunAsync(
                MailruPostmasterSyncTriggers.Scheduled,
                requestedBy: null,
                stoppingToken);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Mail.ru Postmaster background synchronization iteration failed without affecting the application host. domain={Domain}",
                integrationOptions.Domain);
            return true;
        }
    }
}
