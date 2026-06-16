using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Mailings;

public sealed record CreateMailingResult(bool Ok, string Error, Mailing? Mailing)
{
    public static CreateMailingResult Success(Mailing mailing) => new(true, string.Empty, mailing);

    public static CreateMailingResult Failure(string error) => new(false, error, null);
}
