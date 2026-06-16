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
    string? ExclusionReason,
    Guid? ImportBatchId = null)
{
    public static Recipient Accepted(string sourceEmail, string normalizedEmail, Guid? importBatchId = null) => new(
        sourceEmail,
        normalizedEmail,
        RecipientStatus.Accepted,
        null,
        importBatchId);

    public static Recipient Excluded(
        string sourceEmail,
        string normalizedEmail,
        RecipientStatus status,
        string reason,
        Guid? importBatchId = null) => new(sourceEmail, normalizedEmail, status, reason, importBatchId);
}
