using Hangfire;
using Pismolet.Web.Application.Mailings;

namespace Pismolet.Web.Infrastructure.Mail;

public sealed class HangfireMailingSendQueue(IBackgroundJobClient jobs) : IBackgroundMailingSendQueue, IBackgroundReplyQueue
{
    public void Enqueue(Guid mailingId) => jobs.Enqueue<MailingSendJob>(job => job.ExecuteAsync(mailingId));

    public void EnqueueForward(Guid replyEventId) => jobs.Enqueue<ReplyForwardJob>(job => job.ExecuteAsync(replyEventId));

    public void EnqueueCleanup() => jobs.Enqueue<ReplyCleanupJob>(job => job.ExecuteAsync());
}

public sealed class MailingSendJob(IMailingSendService sender)
{
    [AutomaticRetry(Attempts = 3)]
    public Task ExecuteAsync(Guid mailingId) => sender.ExecuteQueuedBatchAsync(mailingId, CancellationToken.None);
}

public sealed class ReplyForwardJob(IInboundReplyProcessingService processor)
{
    [AutomaticRetry(Attempts = 3)]
    [Queue("reply")]
    public Task ExecuteAsync(Guid replyEventId) => processor.ExecuteForwardAsync(replyEventId, CancellationToken.None);
}

public sealed class ReplyCleanupJob(IInboundReplyProcessingService processor)
{
    [AutomaticRetry(Attempts = 1)]
    [Queue("cleanup")]
    public Task ExecuteAsync() => processor.CleanupExpiredBodiesAsync(CancellationToken.None);
}
