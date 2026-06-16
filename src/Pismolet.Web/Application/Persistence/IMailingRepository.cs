using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Persistence;

public interface IMailingRepository
{
    bool TryAdd(Mailing mailing);

    Mailing? Get(Guid id);

    Mailing? GetForOwner(Guid id, string ownerEmail);

    IReadOnlyCollection<Mailing> ListForOwner(string ownerEmail);

    void Update(Mailing mailing);
}
