using Pismolet.Web.Application.Persistence;

namespace Pismolet.Web.Application.Mailings;

public interface IMailWarmupSendGate
{
    MailWarmupLimitDecision Evaluate(string ownerEmail, string recipientEmail, DateTimeOffset now);
}

public sealed class MailWarmupSendGate(
    ISendEventRepository sendEvents,
    IMailWarmupThrottle throttle,
    MailWarmupLimitOptions options) : IMailWarmupSendGate
{
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromDays(1);

    public MailWarmupLimitDecision Evaluate(string ownerEmail, string recipientEmail, DateTimeOffset now)
    {
        var utcNow = now.ToUniversalTime();
        var acceptedSends = sendEvents.ListAcceptedForWarmupWindow(ownerEmail, utcNow - HistoryWindow);
        return throttle.Evaluate(options, acceptedSends, recipientEmail, utcNow);
    }
}
