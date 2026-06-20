namespace Pismolet.Web.Domain.Mailings;

public enum GlobalSuppressionSource
{
    UnsubscribeLink,
    Complaint,
    Admin
}

public sealed record GlobalSuppression(
    Guid Id,
    string EmailNormalized,
    string EmailHash,
    GlobalSuppressionSource Source,
    Guid? SourceMailingId,
    string? SourceRecipientKey,
    DateTimeOffset CreatedAt,
    string? CreatedIpHash,
    string? UserAgentHash)
{
    public static GlobalSuppression FromUnsubscribeLink(
        string normalizedEmail,
        string emailHash,
        Guid sourceMailingId,
        string sourceRecipientKey,
        string? ipHash,
        string? userAgentHash) => new(
            Guid.NewGuid(),
            normalizedEmail.Trim().ToLowerInvariant(),
            emailHash,
            GlobalSuppressionSource.UnsubscribeLink,
            sourceMailingId,
            sourceRecipientKey,
            DateTimeOffset.UtcNow,
            ipHash,
            userAgentHash);
}
