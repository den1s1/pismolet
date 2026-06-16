namespace Pismolet.Web.Domain.Mailings;

public sealed record Mailing(string Subject, string StatusRu)
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string OwnerEmail { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public ImportStats LastImportStats { get; init; } = ImportStats.Empty;

    public List<Recipient> Recipients { get; init; } = [];

    public static Mailing Draft(string subject) => new(subject.Trim(), "Черновик");

    public static Mailing Draft(string ownerEmail, string subject) => Draft(subject) with
    {
        OwnerEmail = ownerEmail.Trim().ToLowerInvariant()
    };

    public Mailing WithImportResult(ImportStats stats, IReadOnlyCollection<Recipient> recipients) => this with
    {
        StatusRu = "Адреса загружены",
        LastImportStats = stats,
        Recipients = recipients.ToList()
    };
}
