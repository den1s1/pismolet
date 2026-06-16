namespace Pismolet.Web.Domain.Mailings;

public enum ImportSourceFormat
{
    Csv,
    Xlsx
}

public enum ImportBatchStatus
{
    Completed,
    Failed
}

public sealed record ImportBatch(
    Guid Id,
    Guid MailingId,
    string FileName,
    ImportSourceFormat SourceFormat,
    DateTimeOffset CreatedAt,
    int TotalRows,
    int Accepted,
    int Duplicates,
    int Invalid,
    int GloballySuppressed,
    ImportBatchStatus Status,
    IReadOnlyCollection<RecipientImportIssue> Issues)
{
    public ImportStats ToStats() => new(TotalRows, Accepted, Duplicates, Invalid, GloballySuppressed);

    public static ImportBatch Completed(
        Guid mailingId,
        string fileName,
        ImportSourceFormat sourceFormat,
        ImportStats stats,
        IReadOnlyCollection<RecipientImportIssue>? issues = null) => new(
            Guid.NewGuid(),
            mailingId,
            fileName,
            sourceFormat,
            DateTimeOffset.UtcNow,
            stats.TotalRows,
            stats.Accepted,
            stats.Duplicates,
            stats.Invalid,
            stats.GloballySuppressed,
            ImportBatchStatus.Completed,
            issues ?? Array.Empty<RecipientImportIssue>());
}
