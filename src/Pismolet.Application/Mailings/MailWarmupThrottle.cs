namespace Pismolet.Web.Application.Mailings;

public interface IMailWarmupThrottle
{
    MailWarmupLimitDecision Evaluate(
        MailWarmupLimitOptions options,
        IEnumerable<MailWarmupAcceptedSend> acceptedSends,
        string? recipientEmail,
        DateTimeOffset now);
}

public sealed class MailWarmupThrottle : IMailWarmupThrottle
{
    public MailWarmupLimitDecision Evaluate(
        MailWarmupLimitOptions options,
        IEnumerable<MailWarmupAcceptedSend> acceptedSends,
        string? recipientEmail,
        DateTimeOffset now)
    {
        var snapshot = MailWarmupSnapshotFactory.Build(acceptedSends, recipientEmail, now);
        return MailWarmupLimitPolicy.Evaluate(options, snapshot, recipientEmail, now);
    }
}
