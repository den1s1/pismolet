using Hangfire;
using Pismolet.Web.Application.Mailings;

namespace Pismolet.Web.Infrastructure.Mail;

public sealed class HangfireMailingSendQueue(IBackgroundJobClient jobs) : IBackgroundMailingSendQueue
{
    public void Enqueue(Guid mailingId) => jobs.Enqueue<MailingSendJob>(job => job.ExecuteAsync(mailingId));
}

public sealed class MailingSendJob(IMailingSendService sender)
{
    [AutomaticRetry(Attempts = 3)]
    public Task ExecuteAsync(Guid mailingId) => sender.ExecuteQueuedBatchAsync(mailingId, CancellationToken.None);
}
