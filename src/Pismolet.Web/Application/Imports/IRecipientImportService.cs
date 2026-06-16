namespace Pismolet.Web.Application.Imports;

public interface IRecipientImportService
{
    Task<ImportRecipientsResult> ImportCsvAsync(ImportRecipientsCommand command, CancellationToken cancellationToken = default);
}
