using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Mailings;

namespace Pismolet.Web.Infrastructure.Mail;

public sealed class InlineMailingSendQueue(IServiceScopeFactory scopeFactory) : IBackgroundMailingSendQueue, IBackgroundReplyQueue
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

    public void EnqueueForward(Guid replyEventId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IInboundReplyProcessingService>();
                await processor.ExecuteForwardAsync(replyEventId, CancellationToken.None);
            }
            catch
            {
                // Dev fallback: ошибки пересылки фиксируются через ReplyEvent, если сервис успел стартовать.
            }
        });
    }

    public void EnqueueCleanup()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IInboundReplyProcessingService>();
                await processor.CleanupExpiredBodiesAsync(CancellationToken.None);
            }
            catch
            {
                // Cleanup должен быть идемпотентен, поэтому dev fallback просто глотает аварийный сбой.
            }
        });
    }
}
