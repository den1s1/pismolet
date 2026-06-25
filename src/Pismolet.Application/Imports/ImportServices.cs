using ClosedXML.Excel;
using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Imports;

public interface IEmailNormalizer
{
    string Normalize(string? value);
}

public sealed class EmailNormalizer : IEmailNormalizer
{
    public string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().ToLowerInvariant();
}

public interface IEmailSyntaxValidator
{
    bool IsValid(string normalizedEmail);
}

public sealed class EmailSyntaxValidator : IEmailSyntaxValidator
{
    public bool IsValid(string normalizedEmail)
    {
        if (string.IsNullOrWhiteSpace(normalizedEmail) || normalizedEmail.Length > 254)
        {
            return false;
        }

        if (normalizedEmail.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var parts = normalizedEmail.Split('@');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        return parts[1].Contains('.', StringComparison.Ordinal) && !parts[1].StartsWith('.') && !parts[1].EndsWith('.');
    }
}

public sealed record ImportRecipientsCommand(
    string UserEmail,
    Guid MailingId,
    string FileName,
    Stream Content,
    RequestMetadata Request);

public sealed record ImportRecipientsResult(bool Ok, string Error, Mailing? Mailing, ImportStats Stats)
{
    public static ImportRecipientsResult Success(Mailing mailing, ImportStats stats) => new(true, string.Empty, mailing, stats);

    public static ImportRecipientsResult Failure(string error) => new(false, error, null, ImportStats.Empty);
}

public interface IRecipientImportService
{
    Task<ImportRecipientsResult> ImportAsync(ImportRecipientsCommand command, CancellationToken cancellationToken = default);

    Task<ImportRecipientsResult> ImportCsvAsync(ImportRecipientsCommand command, CancellationToken cancellationToken = default);
}

public sealed class RecipientImportService(
    IMailingRepository mailings,
    IGlobalSuppressionRepository optOuts,
    IClientSuppressionRepository clientSuppressions,
    ISendEventRepository sendEvents,
    IEmailNormalizer normalizer,
    IEmailSyntaxValidator validator,
    IAuditLogger audit) : IRecipientImportService
{
    public const int MaxRows = 1000;
    public const int SoftBounceWarningThreshold = 2;

    private sealed record ParsedRecipientRow(int RowNumber, string RawEmail, string NormalizedEmail, bool SyntaxValid);

    public Task<ImportRecipientsResult> ImportCsvAsync(ImportRecipientsCommand command, CancellationToken cancellationToken = default) =>
        ImportAsync(command, cancellationToken);

    public async Task<ImportRecipientsResult> ImportAsync(ImportRecipientsCommand command, CancellationToken cancellationToken = default)
    {
        var userEmail = normalizer.Normalize(command.UserEmail);
        var mailing = mailings.GetForOwner(command.MailingId, userEmail);
        if (mailing is null)
        {
            return ImportRecipientsResult.Failure("Рассылка не найдена.");
        }

        var format = DetectFormat(command.FileName);
        if (format is null)
        {
            Log(command, userEmail, "recipients_import_failed", "format");
            return ImportRecipientsResult.Failure("Загрузите CSV или XLSX-файл с колонкой email.");
        }

        Log(command, userEmail, "recipients_import_started", "started");

        IReadOnlyList<string[]> rows;
        try
        {
            rows = format == ImportSourceFormat.Csv
                ? await ReadCsvAsync(command.Content, cancellationToken)
                : ReadXlsx(command.Content);
        }
        catch
        {
            Log(command, userEmail, "recipients_import_failed", "parse_error");
            return ImportRecipientsResult.Failure("Не удалось прочитать файл. Проверьте формат и колонку email.");
        }

        if (rows.Count == 0)
        {
            Log(command, userEmail, "recipients_import_failed", "empty");
            return ImportRecipientsResult.Failure("Файл пустой.");
        }

        var header = rows[0];
        var emailIndex = Array.FindIndex(header, x => string.Equals(x.Trim('\uFEFF'), "email", StringComparison.OrdinalIgnoreCase));
        if (emailIndex < 0)
        {
            Log(command, userEmail, "recipients_import_failed", "no_email_column");
            return ImportRecipientsResult.Failure("В файле должна быть колонка email.");
        }

        var parsedRows = new List<ParsedRecipientRow>();
        var total = 0;
        foreach (var cells in rows.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (cells.Length == 0 || cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            total++;
            if (total > MaxRows)
            {
                Log(command, userEmail, "recipients_import_failed", "too_many_rows");
                return ImportRecipientsResult.Failure($"Файл содержит больше {MaxRows} строк.");
            }

            var rawEmail = emailIndex < cells.Length ? cells[emailIndex] : string.Empty;
            var email = normalizer.Normalize(rawEmail);
            parsedRows.Add(new ParsedRecipientRow(total + 1, rawEmail, email, validator.IsValid(email)));
        }

        if (total == 0)
        {
            Log(command, userEmail, "recipients_import_failed", "empty_data");
            return ImportRecipientsResult.Failure("В файле нет строк с адресами.");
        }

        var validEmails = parsedRows
            .Where(x => x.SyntaxValid)
            .Select(x => x.NormalizedEmail)
            .ToArray();
        var suppressedSet = optOuts.GetSuppressedSet(validEmails);
        var clientSuppressedSet = clientSuppressions.GetSuppressedSet(userEmail, validEmails);
        var softBounceWarnings = sendEvents
            .ListSoftBounceStats(userEmail, validEmails)
            .Where(x => x.SoftBounceCount >= SoftBounceWarningThreshold)
            .ToDictionary(x => x.EmailNormalized, StringComparer.OrdinalIgnoreCase);

        var recipients = new List<Recipient>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = 0;
        var invalid = 0;
        var optedOut = 0;
        var clientSuppressed = 0;
        var issues = new List<RecipientImportIssue>();

        foreach (var row in parsedRows)
        {
            if (!row.SyntaxValid)
            {
                invalid++;
                const string reason = "Невалидный email";
                issues.Add(new RecipientImportIssue(row.RowNumber, row.RawEmail, reason));
                recipients.Add(Recipient.Excluded(row.RawEmail, StoreEmail(row), RecipientStatus.Invalid, reason, rowNumber: row.RowNumber));
            }
            else if (!seen.Add(row.NormalizedEmail))
            {
                duplicates++;
                const string reason = "Дубль в файле";
                issues.Add(new RecipientImportIssue(row.RowNumber, row.NormalizedEmail, reason));
                recipients.Add(Recipient.Excluded(row.RawEmail, row.NormalizedEmail, RecipientStatus.Duplicate, reason, rowNumber: row.RowNumber));
            }
            else if (suppressedSet.Contains(row.NormalizedEmail))
            {
                optedOut++;
                const string reason = "Глобальная отписка";
                issues.Add(new RecipientImportIssue(row.RowNumber, row.NormalizedEmail, reason));
                recipients.Add(Recipient.Excluded(row.RawEmail, row.NormalizedEmail, RecipientStatus.GloballySuppressed, reason, rowNumber: row.RowNumber));
                audit.Write(new AuditRecord(
                    DateTimeOffset.UtcNow,
                    userEmail,
                    "suppressed_email_import_attempted",
                    command.Request.Ip,
                    command.Request.UserAgent,
                    $"mailingId={mailing.Id};emailHash={Hash(row.NormalizedEmail)}"));
            }
            else if (clientSuppressedSet.Contains(row.NormalizedEmail))
            {
                clientSuppressed++;
                const string reason = "Исключено из-за ошибки доставки";
                issues.Add(new RecipientImportIssue(row.RowNumber, row.NormalizedEmail, reason));
                recipients.Add(Recipient.Excluded(row.RawEmail, row.NormalizedEmail, RecipientStatus.ClientSuppressed, reason, rowNumber: row.RowNumber));
                audit.Write(new AuditRecord(
                    DateTimeOffset.UtcNow,
                    userEmail,
                    "client_suppressed_email_import_attempted",
                    command.Request.Ip,
                    command.Request.UserAgent,
                    $"mailingId={mailing.Id};emailHash={Hash(row.NormalizedEmail)}"));
            }
            else
            {
                recipients.Add(Recipient.Accepted(row.RawEmail, row.NormalizedEmail, rowNumber: row.RowNumber));
                if (softBounceWarnings.TryGetValue(row.NormalizedEmail, out var warning))
                {
                    issues.Add(new RecipientImportIssue(
                        row.RowNumber,
                        row.NormalizedEmail,
                        $"Временные ошибки доставки ранее: {warning.SoftBounceCount}. Адрес не исключён."));
                }
            }
        }

        var accepted = recipients.Count(x => x.Status == RecipientStatus.Accepted);
        var stats = new ImportStats(total, accepted, duplicates, invalid, optedOut, clientSuppressed);
        var batch = ImportBatch.Completed(mailing.Id, command.FileName, format.Value, stats, issues);
        var recipientsWithBatch = recipients.Select(recipient => recipient with { ImportBatchId = batch.Id }).ToArray();
        var updated = mailing.WithImportResult(batch, recipientsWithBatch);
        mailings.Update(updated);
        Log(command, userEmail, "recipients_import_completed", $"{{\"mailingId\":\"{mailing.Id}\",\"importBatchId\":\"{batch.Id}\",\"format\":\"{format.Value}\",\"clientSuppressed\":{clientSuppressed}}}");
        return ImportRecipientsResult.Success(updated, stats);
    }

    private static string StoreEmail(ParsedRecipientRow row) => string.IsNullOrWhiteSpace(row.NormalizedEmail)
        ? row.RawEmail.Trim()
        : row.NormalizedEmail;

    private static ImportSourceFormat? DetectFormat(string fileName)
    {
        if (fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return ImportSourceFormat.Csv;
        }

        if (fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return ImportSourceFormat.Xlsx;
        }

        return null;
    }

    private static async Task<IReadOnlyList<string[]>> ReadCsvAsync(Stream content, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(content);
        var rows = new List<string[]>();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            rows.Add(SplitCsv(line));
        }

        return rows;
    }

    private static IReadOnlyList<string[]> ReadXlsx(Stream content)
    {
        using var workbook = new XLWorkbook(content);
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet is null)
        {
            return Array.Empty<string[]>();
        }

        var rows = new List<string[]>();
        foreach (var row in worksheet.RowsUsed())
        {
            var lastCell = row.LastCellUsed();
            if (lastCell is null)
            {
                continue;
            }

            rows.Add(row.Cells(1, lastCell.Address.ColumnNumber).Select(cell => cell.GetString().Trim()).ToArray());
        }

        return rows;
    }

    private static string[] SplitCsv(string line) => line.Split(',').Select(x => x.Trim().Trim('"')).ToArray();

    private void Log(ImportRecipientsCommand command, string userEmail, string eventType, string context) => audit.Write(new AuditRecord(
        DateTimeOffset.UtcNow,
        userEmail,
        eventType,
        command.Request.Ip,
        command.Request.UserAgent,
        context));

    private static string Hash(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
