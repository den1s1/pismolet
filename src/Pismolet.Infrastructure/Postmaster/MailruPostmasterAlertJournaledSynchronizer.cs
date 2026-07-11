using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Pismolet.Web.Infrastructure.Postmaster;

public sealed class MailruPostmasterAlertJournaledSynchronizer(
    MailruPostmasterCompositeSynchronizer innerSynchronizer,
    IServiceScopeFactory scopeFactory,
    ILogger<MailruPostmasterAlertJournaledSynchronizer> logger)
    : IMailruPostmasterSynchronizer
{
    public async Task<MailruPostmasterSyncRunResult> RunOnceAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await innerSynchronizer.RunOnceAsync(cancellationToken);
        if (result.Status is not (MailruPostmasterSyncRunStatus.Succeeded or MailruPostmasterSyncRunStatus.Failed))
        {
            return result;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetService<IMailruPostmasterAlertJournalProcessor>();
            if (processor is not null)
            {
                await processor.RunOnceAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Mail.ru Postmaster PM-5 journal stage failed without affecting domain or mailing synchronization or the application host.");
        }

        return result;
    }
}
