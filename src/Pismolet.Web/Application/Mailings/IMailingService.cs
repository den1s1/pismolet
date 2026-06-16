using Pismolet.Web.Application.Common;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Mailings;

public interface IMailingService
{
    CreateMailingResult CreateDraft(CreateMailingCommand command, RequestMetadata request);

    Mailing? GetForOwner(Guid id, string userEmail);

    IReadOnlyCollection<Mailing> ListForOwner(string userEmail);
}
