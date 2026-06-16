namespace Pismolet.Web.Domain.Mailings;

public sealed record ImportBatch(
    Guid Id,
    Guid MailingId,
    DateTimeOffset ImportedAt,
    ImportStats Stats,
    IReadOnlyCollection<RecipientImportIssue> Issues)
{
    public static ImportBatch Create(Guid mailingId, ImportStats stats, IReadOnlyCollection<RecipientImportIssue> issues) => new(
        Guid.NewGuid(),
        mailingId,
        DateTimeOffset.UtcNow,
        stats,
        issues);
}
