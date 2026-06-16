namespace Pismolet.Web.Application.Imports;

public interface IRecipientImportService
{
    Task<ImportRecipientsResult> ImportAsync(ImportRecipientsCommand command, CancellationToken cancellationToken = default);

    Task<ImportRecipientsResult> ImportCsvAsync(ImportRecipientsCommand command, CancellationToken cancellationToken = default);
}
