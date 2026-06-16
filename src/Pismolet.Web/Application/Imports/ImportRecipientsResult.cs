using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Imports;

public sealed record ImportRecipientsResult(bool Ok, string Error, Mailing? Mailing, ImportStats Stats)
{
    public static ImportRecipientsResult Success(Mailing mailing, ImportStats stats) => new(true, string.Empty, mailing, stats);

    public static ImportRecipientsResult Failure(string error) => new(false, error, null, ImportStats.Empty);
}
