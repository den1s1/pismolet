using ClosedXML.Excel;
using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Imports;

public sealed class RecipientImportService(
    IMailingRepository mailings,
    IGlobalSuppressionRepository optOuts,
    IEmailNormalizer normalizer,
    IEmailSyntaxValidator validator,
    IAuditLogger audit) : IRecipientImportService
{
    public const int MaxRows = 1000;

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
            return ImportRecipientsResult.Failure("Загрузите CSV или XLSX-файл с адресами.");
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
            return ImportRecipientsResult.Failure("Не удалось прочитать файл. Проверьте формат таблицы.");
        }

        if (rows.Count == 0)
        {
            Log(command, userEmail, "recipients_import_failed", "empty");
            return ImportRecipientsResult.Failure("Файл пустой.");
        }

        var header = rows[0];
        var emailIndex = FindEmailColumnIndex(header);
        var dataRows = rows.Skip(1);
        var rowNumberOffset = 1;
        if (emailIndex < 0)
        {
            emailIndex = FindBestEmailColumnIndex(rows);
            dataRows = rows;
            rowNumberOffset = 0;
        }

        if (emailIndex < 0)
        {
            Log(command, userEmail, "recipients_import_failed", "no_email_column_or_values");
            return ImportRecipientsResult.Failure("В файле не найдены email-адреса.");
        }

        var accepted = new List<Recipient>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        var duplicates = 0;
        var invalid = 0;
        var optedOut = 0;
        var issues = new List<RecipientImportIssue>();

        foreach (var cells in dataRows)
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
            var rowNumber = total + rowNumberOffset;

            if (!validator.IsValid(email))
            {
                invalid++;
                issues.Add(new RecipientImportIssue(rowNumber, rawEmail, "Невалидный email"));
            }
            else if (!seen.Add(email))
            {
                duplicates++;
                issues.Add(new RecipientImportIssue(rowNumber, email, "Дубль в файле"));
            }
            else if (optOuts.IsSuppressed(email))
            {
                optedOut++;
                issues.Add(new RecipientImportIssue(rowNumber, email, "Глобальная отписка"));
            }
            else
            {
                accepted.Add(Recipient.Accepted(rawEmail, email));
            }
        }

        if (total == 0)
        {
            Log(command, userEmail, "recipients_import_failed", "empty_data");
            return ImportRecipientsResult.Failure("В файле нет строк с адресами.");
        }

        var stats = new ImportStats(total, accepted.Count, duplicates, invalid, optedOut);
        var batch = ImportBatch.Completed(mailing.Id, command.FileName, format.Value, stats, issues);
        var recipients = accepted.Select(recipient => recipient with { ImportBatchId = batch.Id }).ToArray();
        var updated = mailing.WithImportResult(batch, recipients);
        mailings.Update(updated);
        Log(command, userEmail, "recipients_import_completed", $"{{\"mailingId\":\"{mailing.Id}\",\"importBatchId\":\"{batch.Id}\",\"format\":\"{format.Value}\"}}");
        return ImportRecipientsResult.Success(updated, stats);
    }

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

    private int FindBestEmailColumnIndex(IReadOnlyList<string[]> rows)
    {
        var maxColumns = rows.Max(row => row.Length);
        var bestIndex = -1;
        var bestCount = 0;
        for (var index = 0; index < maxColumns; index++)
        {
            var count = rows.Count(row => index < row.Length && validator.IsValid(normalizer.Normalize(row[index])));
            if (count > bestCount)
            {
                bestIndex = index;
                bestCount = count;
            }
        }

        return bestCount > 0 ? bestIndex : -1;
    }

    private static int FindEmailColumnIndex(string[] header) => Array.FindIndex(header, IsEmailColumnName);

    private static bool IsEmailColumnName(string value)
    {
        var normalized = value.Trim('\uFEFF').Trim().ToLowerInvariant();
        return normalized is "email" or "e-mail" or "e mail" or "mail";
    }

    private static string[] SplitCsv(string line) => line.Split(',').Select(x => x.Trim().Trim('"')).ToArray();

    private void Log(ImportRecipientsCommand command, string userEmail, string eventType, string context) => audit.Write(new AuditRecord(
        DateTimeOffset.UtcNow,
        userEmail,
        eventType,
        command.Request.Ip,
        command.Request.UserAgent,
        context));
}
