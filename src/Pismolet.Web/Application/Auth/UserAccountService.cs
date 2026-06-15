using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mail;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;

namespace Pismolet.Web.Application.Auth;

public sealed class UserAccountService(
    IUserRepository users,
    IFakeMailer fakeMailer,
    IAuditLogger auditLogger) : IUserAccountService
{
    public RegisterUserResult Register(RegisterUserCommand command, RequestMetadata request)
    {
        var email = NormalizeEmail(command.Email);
        var displayName = string.IsNullOrWhiteSpace(command.DisplayName)
            ? email
            : command.DisplayName.Trim();

        if (string.IsNullOrWhiteSpace(email) || command.Password.Length < 8)
        {
            return RegisterUserResult.Failure("Укажите email и пароль от 8 символов.");
        }

        if (users.Exists(email))
        {
            return RegisterUserResult.Failure("Пользователь уже существует.");
        }

        var token = Guid.NewGuid().ToString("N");
        var user = new UserAccount(
            Email: email,
            PasswordHash: HashPassword(command.Password),
            DisplayName: displayName,
            ConfirmationToken: token,
            EmailConfirmed: false,
            Profile: ClientProfile.NewClientDefault(),
            Mailings: [Mailing.Draft("Первая рассылка")]);

        if (!users.TryAdd(user))
        {
            return RegisterUserResult.Failure("Пользователь уже существует.");
        }

        Audit(email, "registration", request);

        var link = "/account/confirm-email?token=" + token;
        fakeMailer.SendConfirmation(email, "Подтверждение email", link);

        return RegisterUserResult.Success(link);
    }

    public bool ConfirmEmail(string token, RequestMetadata request)
    {
        var user = users.FindByConfirmationToken(token);
        if (user is null)
        {
            return false;
        }

        users.Update(user with { EmailConfirmed = true });
        Audit(user.Email, "email_confirmed", request);
        return true;
    }

    public string? ResendConfirmation(string email)
    {
        var normalizedEmail = NormalizeEmail(email);
        var user = users.GetByEmail(normalizedEmail);
        if (user is null)
        {
            return null;
        }

        var link = "/account/confirm-email?token=" + user.ConfirmationToken;
        fakeMailer.SendConfirmation(normalizedEmail, "Повторное подтверждение email", link);
        return link;
    }

    public UserAccount? Authenticate(LoginUserCommand command, RequestMetadata request)
    {
        var email = NormalizeEmail(command.Email);
        var user = users.GetByEmail(email);
        if (user is null || !VerifyPassword(command.Password, user.PasswordHash) || !user.EmailConfirmed)
        {
            return null;
        }

        Audit(email, "login", request);
        return user;
    }

    public UserAccount? GetByEmail(string email) => users.GetByEmail(NormalizeEmail(email));

    public void AuditLogout(string email, RequestMetadata request) => Audit(NormalizeEmail(email), "logout", request);

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string HashPassword(string password) => "dev:" + password;

    private static bool VerifyPassword(string password, string passwordHash) => passwordHash == HashPassword(password);

    private void Audit(string email, string eventType, RequestMetadata request) => auditLogger.Write(new AuditRecord(
        CreatedAt: DateTimeOffset.UtcNow,
        User: email,
        EventType: eventType,
        Ip: request.Ip,
        UserAgent: request.UserAgent,
        Context: "{}"));
}
