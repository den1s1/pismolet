namespace Pismolet.Web.Domain.Mailings;

public sealed record ImportStats(
    int TotalRows,
    int Accepted,
    int Duplicates,
    int Invalid,
    int GloballySuppressed,
    int ClientSuppressed = 0)
{
    public static ImportStats Empty { get; } = new(0, 0, 0, 0, 0, 0);
}

public sealed record RecipientImportIssue(int RowNumber, string Email, string Message);

public enum RecipientStatus
{
    Accepted,
    Duplicate,
    Invalid,
    GloballySuppressed,
    ClientSuppressed
}

public sealed record Recipient(
    string SourceEmail,
    string Email,
    RecipientStatus Status,
    string? ExclusionReason,
    Guid? ImportBatchId = null,
    int RowNumber = 0)
{
    public static Recipient Accepted(string sourceEmail, string normalizedEmail, Guid? importBatchId = null, int rowNumber = 0) => new(
        sourceEmail,
        normalizedEmail,
        RecipientStatus.Accepted,
        null,
        importBatchId,
        rowNumber);

    public static Recipient Excluded(
        string sourceEmail,
        string normalizedEmail,
        RecipientStatus status,
        string reason,
        Guid? importBatchId = null,
        int rowNumber = 0) => new(sourceEmail, normalizedEmail, status, reason, importBatchId, rowNumber);
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
    IReadOnlyCollection<RecipientImportIssue> Issues,
    int ClientSuppressed = 0)
{
    public ImportStats ToStats() => new(TotalRows, Accepted, Duplicates, Invalid, GloballySuppressed, ClientSuppressed);

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
            issues ?? Array.Empty<RecipientImportIssue>(),
            stats.ClientSuppressed);
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
    public const string CurrentVersion = "2026-06-24-v1";

    public const string Text = "Я подтверждаю, что имею законное основание для обработки загружаемых email-адресов и отправки писем этим адресатам. Я не использую купленные, украденные, спарсенные, чужие или иным образом незаконно полученные базы. Я понимаю, что отвечаю за законность базы и обязан предоставить подтверждения по запросу Письмолёта, адресата или уполномоченных органов. document_key=base_lawfulness_declaration; document_version=2026-06-24-v1; document_url=/legal/base-lawfulness.";
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
    public Guid? ImportBatchId { get; init; }

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

public sealed record MailingAttachment(string FileName, string ContentType, byte[] Content, long Size)
{
    public static MailingAttachment Create(string fileName, string? contentType, byte[] content)
    {
        var safeFileName = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            throw new ArgumentException("Укажите имя файла.", nameof(fileName));
        }

        if (safeFileName.Length > 180)
        {
            throw new ArgumentException("Имя файла должно быть не длиннее 180 символов.", nameof(fileName));
        }

        if (content.Length == 0)
        {
            throw new ArgumentException("Пустые вложения не поддерживаются.", nameof(content));
        }

        var safeContentType = string.IsNullOrWhiteSpace(contentType)
            ? "application/octet-stream"
            : contentType.Trim();

        return new MailingAttachment(safeFileName, safeContentType, content, content.LongLength);
    }
}

public sealed record MailingMessageDraft
{
    public const int MaxSenderNameLength = 80;
    public const int MaxSubjectLength = 160;
    public const long MaxAttachmentsTotalBytes = 10 * 1024 * 1024;

    public MailingMessageDraft(string senderName, string subject, string body, MessageType messageType, DateTimeOffset updatedAt)
        : this(senderName, subject, body, messageType, updatedAt, Array.Empty<MailingAttachment>())
    {
    }

    public MailingMessageDraft(
        string senderName,
        string subject,
        string body,
        MessageType messageType,
        DateTimeOffset updatedAt,
        IReadOnlyCollection<MailingAttachment>? attachments)
    {
        SenderName = senderName;
        Subject = subject;
        Body = body;
        MessageType = messageType;
        UpdatedAt = updatedAt;
        Attachments = attachments ?? Array.Empty<MailingAttachment>();
    }

    public string SenderName { get; init; }

    public string Subject { get; init; }

    public string Body { get; init; }

    public MessageType MessageType { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public IReadOnlyCollection<MailingAttachment> Attachments { get; init; }

    public static MailingMessageDraft Create(
        string senderName,
        string subject,
        string body,
        MessageType messageType,
        DateTimeOffset updatedAt,
        IReadOnlyCollection<MailingAttachment>? attachments = null)
    {
        senderName = senderName.Trim();
        subject = subject.Trim();
        body = body.Trim();
        var normalizedAttachments = attachments?.ToArray() ?? Array.Empty<MailingAttachment>();

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

        var totalAttachmentBytes = normalizedAttachments.Sum(x => x.Size);
        if (totalAttachmentBytes > MaxAttachmentsTotalBytes)
        {
            throw new ArgumentException("Общий размер вложений не должен превышать 10 МБ.", nameof(attachments));
        }

        return new MailingMessageDraft(senderName, subject, body, messageType, updatedAt, normalizedAttachments);
    }
}

public enum MailingStatus
{
    Draft,
    RecipientsImported,
    DeclarationConfirmed,
    MessagePrepared,
    Priced,
    PaymentPending,
    Paid,
    PendingChecks,
    ReviewRequired,
    Approved,
    Rejected,
    Sending,
    Sent,
    Failed,
    Paused,
    Blocked
}

public static class MailingStatusLabels
{
    public static string ToCode(this MailingStatus status) => status switch
    {
        MailingStatus.RecipientsImported => "recipients_imported",
        MailingStatus.DeclarationConfirmed => "declaration_confirmed",
        MailingStatus.MessagePrepared => "message_prepared",
        MailingStatus.PaymentPending => "payment_pending",
        MailingStatus.PendingChecks => "pending_checks",
        MailingStatus.ReviewRequired => "review_required",
        MailingStatus.Approved => "approved",
        MailingStatus.Rejected => "rejected",
        MailingStatus.Sending => "sending",
        MailingStatus.Sent => "sent",
        MailingStatus.Failed => "failed",
        MailingStatus.Paused => "paused",
        MailingStatus.Blocked => "blocked",
        MailingStatus.Priced => "priced",
        MailingStatus.Paid => "paid",
        _ => "draft"
    };

    public static string ToRu(this MailingStatus status) => status switch
    {
        MailingStatus.RecipientsImported => "Адреса загружены",
        MailingStatus.DeclarationConfirmed => "База подтверждена",
        MailingStatus.MessagePrepared => "Письмо подготовлено",
        MailingStatus.Priced => "Стоимость рассчитана",
        MailingStatus.PaymentPending => "Ожидает оплаты",
        MailingStatus.Paid => "Оплачено",
        MailingStatus.PendingChecks => "Проверяем перед отправкой",
        MailingStatus.ReviewRequired => "На ручной проверке",
        MailingStatus.Approved => "Одобрено",
        MailingStatus.Rejected => "Отклонено",
        MailingStatus.Sending => "Отправляется",
        MailingStatus.Sent => "Отправлено",
        MailingStatus.Failed => "Ошибка отправки",
        MailingStatus.Paused => "Приостановлено",
        MailingStatus.Blocked => "Заблокировано администратором",
        _ => "Черновик"
    };

    public static MailingStatus FromRu(string statusRu) => statusRu switch
    {
        "Адреса загружены" => MailingStatus.RecipientsImported,
        "База подтверждена" => MailingStatus.DeclarationConfirmed,
        "Письмо подготовлено" => MailingStatus.MessagePrepared,
        "Стоимость рассчитана" => MailingStatus.Priced,
        "Ожидает оплаты" => MailingStatus.PaymentPending,
        "Оплачено" => MailingStatus.Paid,
        "Проверяем перед отправкой" => MailingStatus.PendingChecks,
        "На ручной проверке" => MailingStatus.ReviewRequired,
        "Одобрено" => MailingStatus.Approved,
        "Отклонено" => MailingStatus.Rejected,
        "Отправляется" => MailingStatus.Sending,
        "Отправлено" => MailingStatus.Sent,
        "Ошибка отправки" => MailingStatus.Failed,
        "Приостановлено" => MailingStatus.Paused,
        "Заблокировано администратором" => MailingStatus.Blocked,
        _ => MailingStatus.Draft
    };

    public static bool CanStartSending(this MailingStatus status) => status == MailingStatus.Approved;

    public static bool CanResumeSending(this MailingStatus status) => status == MailingStatus.Paused;
}

public sealed record Mailing(string Subject, string StatusRu)
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string OwnerEmail { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public MailingStatus Status { get; init; } = MailingStatusLabels.FromRu(StatusRu);

    public ImportStats LastImportStats { get; init; } = ImportStats.Empty;

    public ImportBatch? LastImportBatch { get; init; }

    public List<ImportBatch> ImportBatches { get; init; } = new();

    public List<Recipient> Recipients { get; init; } = new();

    public MailingDeclaration? Declaration { get; init; }

    public MailingMessageDraft? MessageDraft { get; init; }

    public string PublicId { get; init; } = $"PL-{Guid.NewGuid():N}"[..11].ToUpperInvariant();

    public static Mailing Draft(string subject) => new(subject.Trim(), MailingStatus.Draft.ToRu())
    {
        Status = MailingStatus.Draft
    };

    public static Mailing Draft(string ownerEmail, string subject) => Draft(subject) with
    {
        OwnerEmail = ownerEmail.Trim().ToLowerInvariant()
    };

    public Mailing WithStatus(MailingStatus status) => this with
    {
        Status = status,
        StatusRu = status.ToRu()
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
            Status = MailingStatus.RecipientsImported,
            StatusRu = MailingStatus.RecipientsImported.ToRu(),
            LastImportStats = normalizedBatch.ToStats(),
            LastImportBatch = normalizedBatch,
            ImportBatches = batches,
            Recipients = recipients.ToList()
        };
    }

    public Mailing WithDeclaration(MailingDeclaration declaration) => this with
    {
        Status = MailingStatus.DeclarationConfirmed,
        StatusRu = MailingStatus.DeclarationConfirmed.ToRu(),
        Declaration = declaration
    };

    public Mailing WithMessageDraft(MailingMessageDraft draft) => this with
    {
        Status = MailingStatus.MessagePrepared,
        StatusRu = MailingStatus.MessagePrepared.ToRu(),
        MessageDraft = draft
    };
}
