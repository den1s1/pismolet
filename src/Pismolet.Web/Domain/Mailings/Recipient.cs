namespace Pismolet.Web.Domain.Mailings;

public enum RecipientStatus
{
    Accepted,
    Duplicate,
    Invalid,
    GloballySuppressed
}

public sealed record Recipient(
    string SourceEmail,
    string Email,
    RecipientStatus Status,
    string? ExclusionReason)
{
    public static Recipient Accepted(string sourceEmail, string normalizedEmail) => new(
        sourceEmail,
        normalizedEmail,
        RecipientStatus.Accepted,
        null);

    public static Recipient Excluded(
        string sourceEmail,
        string normalizedEmail,
        RecipientStatus status,
        string reason) => new(sourceEmail, normalizedEmail, status, reason);
}
