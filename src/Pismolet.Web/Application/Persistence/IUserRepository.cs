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
