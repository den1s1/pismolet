namespace Pismolet.Web.Domain.Mailings;

public sealed record Mailing(string Subject, string StatusRu)
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string OwnerEmail { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public ImportStats LastImportStats { get; init; } = ImportStats.Empty;

    public ImportBatch? LastImportBatch { get; init; }

    public List<ImportBatch> ImportBatches { get; init; } = new();

    public List<Recipient> Recipients { get; init; } = new();

    public MailingDeclaration? Declaration { get; init; }

    public MailingMessageDraft? MessageDraft { get; init; }

    public string PublicId { get; init; } = $"PL-{Guid.NewGuid():N}"[..11].ToUpperInvariant();

    public static Mailing Draft(string subject) => new(subject.Trim(), "Черновик");

    public static Mailing Draft(string ownerEmail, string subject) => Draft(subject) with
    {
        OwnerEmail = ownerEmail.Trim().ToLowerInvariant()
    };

    public Mailing WithImportResult(ImportStats stats, IReadOnlyCollection<Recipient> recipients)
    {
        var batch = ImportBatch.Completed(Id, "import.csv", ImportSourceFormat.Csv, stats);
        return WithImportResult(batch, recipients);
    }

    public Mailing WithImportResult(ImportBatch batch, IReadOnlyCollection<Recipient> recipients)
    {
        var batches = ImportBatches.ToList();
        batches.Add(batch);

        return this with
        {
            StatusRu = "Адреса загружены",
            LastImportStats = batch.ToStats(),
            LastImportBatch = batch,
            ImportBatches = batches,
            Recipients = recipients.ToList()
        };
    }

    public Mailing WithDeclaration(MailingDeclaration declaration) => this with
    {
        StatusRu = "База подтверждена",
        Declaration = declaration
    };

    public Mailing WithMessageDraft(MailingMessageDraft draft) => this with
    {
        StatusRu = "Письмо подготовлено",
        MessageDraft = draft
    };
}
