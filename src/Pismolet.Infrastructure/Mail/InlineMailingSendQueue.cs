using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Mailings;

namespace Pismolet.Web.Infrastructure.Mail;

public sealed class InlineMailingSendQueue(IServiceScopeFactory scopeFactory) : IBackgroundMailingSendQueue
{
    public void Enqueue(Guid mailingId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<IMailingSendService>();
                await sender.ExecuteQueuedBatchAsync(mailingId, CancellationToken.None);
            }
            catch
            {
                // Dev fallback: ошибки batch-job фиксируются application service через SendEvent/audit, если сервис успел стартовать.
            }
        });
    }
}
