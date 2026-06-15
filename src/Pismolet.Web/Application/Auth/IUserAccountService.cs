using Pismolet.Web.Application.Common;
using Pismolet.Web.Domain.Users;

namespace Pismolet.Web.Application.Auth;

public interface IUserAccountService
{
    RegisterUserResult Register(RegisterUserCommand command, RequestMetadata request);

    bool ConfirmEmail(string token, RequestMetadata request);

    string? ResendConfirmation(string email);

    UserAccount? Authenticate(LoginUserCommand command, RequestMetadata request);

    UserAccount? GetByEmail(string email);

    void AuditLogout(string email, RequestMetadata request);
}
