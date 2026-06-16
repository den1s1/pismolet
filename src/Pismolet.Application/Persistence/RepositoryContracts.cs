using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;

namespace Pismolet.Web.Application.Persistence;

public interface IUserRepository
{
    bool Exists(string email);

    bool TryAdd(UserAccount user);

    UserAccount? GetByEmail(string email);

    UserAccount? FindByConfirmationToken(string token);

    void Update(UserAccount user);
}

public interface IMailingRepository
{
    bool TryAdd(Mailing mailing);

    Mailing? Get(Guid id);

    Mailing? GetForOwner(Guid id, string ownerEmail);

    IReadOnlyCollection<Mailing> ListForOwner(string ownerEmail);

    void Update(Mailing mailing);
}

public interface IGlobalSuppressionRepository
{
    bool IsSuppressed(string normalizedEmail);

    void Add(string normalizedEmail);
}
