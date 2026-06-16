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

    public async Task<ImportRecipientsResult> ImportCsvAsync(ImportRecipientsCommand command, CancellationToken cancellationToken = default)
    {
        var userEmail = normalizer.Normalize(command.UserEmail);
        var mailing = mailings.GetForOwner(command.MailingId, userEmail);
        if (mailing is null)
        {
            return ImportRecipientsResult.Failure("Рассылка не найдена.");
        }

        if (!command.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            Log(command, userEmail, "recipients_import_failed", "format");
            return ImportRecipientsResult.Failure("Загрузите CSV-файл с колонкой email.");
        }

        Log(command, userEmail, "recipients_import_started", "started");

        using var reader = new StreamReader(command.Content);
        var header = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(header))
        {
            Log(command, userEmail, "recipients_import_failed", "empty");
            return ImportRecipientsResult.Failure("Файл пустой.");
        }

        var emailIndex = Array.FindIndex(Split(header), x => string.Equals(x.Trim('\uFEFF'), "email", StringComparison.OrdinalIgnoreCase));
        if (emailIndex < 0)
        {
            Log(command, userEmail, "recipients_import_failed", "no_email_column");
            return ImportRecipientsResult.Failure("В CSV должна быть колонка email.");
        }

        var accepted = new List<Recipient>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        var duplicates = 0;
        var invalid = 0;
        var optedOut = 0;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            total++;
            if (total > MaxRows)
            {
                Log(command, userEmail, "recipients_import_failed", "too_many_rows");
                return ImportRecipientsResult.Failure($"Файл содержит больше {MaxRows} строк.");
            }

            var cells = Split(line);
            var rawEmail = emailIndex < cells.Length ? cells[emailIndex] : string.Empty;
            var email = normalizer.Normalize(rawEmail);

            if (!validator.IsValid(email))
            {
                invalid++;
            }
            else if (!seen.Add(email))
            {
                duplicates++;
            }
            else if (optOuts.IsSuppressed(email))
            {
                optedOut++;
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
        var updated = mailing.WithImportResult(stats, accepted);
        mailings.Update(updated);
        Log(command, userEmail, "recipients_import_completed", "completed");
        return ImportRecipientsResult.Success(updated, stats);
    }

    private static string[] Split(string line) => line.Split(',').Select(x => x.Trim().Trim('"')).ToArray();

    private void Log(ImportRecipientsCommand command, string userEmail, string eventType, string context) => audit.Write(new AuditRecord(
        DateTimeOffset.UtcNow,
        userEmail,
        eventType,
        command.Request.Ip,
        command.Request.UserAgent,
        context));
}
