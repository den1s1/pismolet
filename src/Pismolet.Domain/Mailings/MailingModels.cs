namespace Pismolet.Web.Domain.Mailings;

public sealed record ImportStats(
    int TotalRows,
    int Accepted,
    int Duplicates,
    int Invalid,
    int GloballySuppressed)
{
    public static ImportStats Empty { get; } = new(0, 0, 0, 0, 0);
}

public sealed record RecipientImportIssue(int RowNumber, string Email, string Message);

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

public enum BaseSource
{
    Customers,
    EventParticipants,
    FormSubscribers,
    OrganizationMembers,
    Other
}

public static class BaseSourceLabels
{
    public static IReadOnlyDictionary<BaseSource, string> All { get; } = new Dictionary<BaseSource, string>
    {
        [BaseSource.Customers] = "Клиенты или покупатели",
        [BaseSource.EventParticipants] = "Участники мероприятия",
        [BaseSource.FormSubscribers] = "Подписчики формы",
        [BaseSource.OrganizationMembers] = "Члены организации или сообщества",
        [BaseSource.Other] = "Другое"
    };

    public static string ToRu(this BaseSource source) => All.GetValueOrDefault(source, "Другое");
}

public static class BaseDeclarationText
{
    public const string CurrentVersion = "2026-06-16-v1";

    public const string Text = "Подтверждаю правомерность использования загруженных адресов для этой рассылки, наличие необходимого основания для обращения к адресатам и достоверность выбранного источника базы.";
}

public sealed record MailingDeclaration(
    Guid MailingId,
    string UserEmail,
    BaseSource BaseSource,
    bool IsBaseLegalityConfirmed,
    bool IsAdvertisingConsentConfirmed,
    string DeclarationVersion,
    DateTimeOffset CreatedAt,
    string Ip,
    string UserAgent)
{
    public bool IsValidFor(MessageType messageType) => IsBaseLegalityConfirmed &&
        (messageType != MessageType.Advertising || IsAdvertisingConsentConfirmed);
}

public enum MessageType
{
    Transactional,
    Advertising
}

public static class MessageTypeLabels
{
    public static string ToRu(this MessageType type) => type == MessageType.Advertising ? "Рекламное" : "Информационное";
}

public sealed record MailingMessageDraft(
    string SenderName,
    string Subject,
    string Body,
    MessageType MessageType,
    DateTimeOffset UpdatedAt)
{
    public const int MaxSenderNameLength = 80;
    public const int MaxSubjectLength = 160;

    public static MailingMessageDraft Create(string senderName, string subject, string body, MessageType messageType, DateTimeOffset updatedAt)
    {
        senderName = senderName.Trim();
        subject = subject.Trim();
        body = body.Trim();

        if (string.IsNullOrWhiteSpace(senderName))
        {
            throw new ArgumentException("Укажите имя отправителя.", nameof(senderName));
        }

        if (senderName.Length > MaxSenderNameLength)
        {
            throw new ArgumentException($"Имя отправителя должно быть не длиннее {MaxSenderNameLength} символов.", nameof(senderName));
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Укажите тему письма.", nameof(subject));
        }

        if (subject.Length > MaxSubjectLength)
        {
            throw new ArgumentException($"Тема письма должна быть не длиннее {MaxSubjectLength} символов.", nameof(subject));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("Напишите текст письма.", nameof(body));
        }

        return new MailingMessageDraft(senderName, subject, body, messageType, updatedAt);
    }
}

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
        var normalizedBatch = batch.MailingId == Guid.Empty ? batch with { MailingId = Id } : batch;
        var batches = ImportBatches.ToList();
        batches.Add(normalizedBatch);

        return this with
        {
            StatusRu = "Адреса загружены",
            LastImportStats = normalizedBatch.ToStats(),
            LastImportBatch = normalizedBatch,
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
