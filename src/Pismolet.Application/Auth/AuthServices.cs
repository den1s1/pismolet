using System.Text.Json;
using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Legal;
using Pismolet.Web.Application.Mail;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Domain.Legal;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;

namespace Pismolet.Web.Application.Auth;

public sealed record RegisterUserCommand(string Email, string Password, string DisplayName);

public sealed record LoginUserCommand(string Email, string Password);

public sealed record RegisterUserResult(bool Ok, string Error, string? ConfirmLink)
{
    public static RegisterUserResult Success(string confirmLink) => new(true, string.Empty, confirmLink);

    public static RegisterUserResult Failure(string error) => new(false, error, null);
}

public interface IUserAccountService
{
    RegisterUserResult Register(RegisterUserCommand command, RequestMetadata request);

    bool ConfirmEmail(string token, RequestMetadata request);

    string? ResendConfirmation(string email);

    UserAccount? Authenticate(LoginUserCommand command, RequestMetadata request);

    UserAccount? GetByEmail(string email);

    void AuditLogout(string email, RequestMetadata request);
}

public sealed class UserAccountService(
    IUserRepository users,
    IFakeMailer fakeMailer,
    IAuditLogger auditLogger,
    IAdminMvpSettingsRepository settingsRepository,
    ILegalEvidenceService legalEvidence) : IUserAccountService
{
    private const string EmailConfirmationSnapshot = "Клиент подтвердил владение email-адресом переходом по ссылке подтверждения.";

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

        var settings = settingsRepository.Get();
        var token = Guid.NewGuid().ToString("N");
        var user = new UserAccount(
            Email: email,
            PasswordHash: HashPassword(command.Password),
            DisplayName: displayName,
            ConfirmationToken: token,
            EmailConfirmed: false,
            Profile: ClientProfile.NewClientDefault(settings),
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

        var confirmedUser = user with { EmailConfirmed = true };
        users.Update(confirmedUser);
        Audit(user.Email, "email_confirmed", request);
        RecordEmailConfirmation(user, request);
        RecordClientProfileConfirmation(confirmedUser, request);
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

    private void RecordEmailConfirmation(UserAccount user, RequestMetadata request) => legalEvidence.RecordEvent(new LegalEvidenceEventDraft(
        EventType: LegalEventTypes.ClientEmailConfirmed,
        ClientId: user.Email,
        UserId: user.Email,
        ImportBatchId: null,
        MailingId: null,
        DocumentKey: null,
        DocumentVersion: null,
        TextHash: null,
        EventTextSnapshot: EmailConfirmationSnapshot,
        Result: LegalEventResults.Confirmed,
        Ip: request.Ip,
        UserAgent: request.UserAgent,
        Route: "/account/confirm-email",
        MetadataJson: JsonSerializer.Serialize(new
        {
            source = "email_confirmation_link"
        })));

    private void RecordClientProfileConfirmation(UserAccount user, RequestMetadata request)
    {
        var snapshot = LegalEvidenceTextSnapshots.ClientProfileConfirmationText;
        legalEvidence.RecordEvent(new LegalEvidenceEventDraft(
            EventType: LegalEventTypes.ClientProfileConfirmed,
            ClientId: user.Email,
            UserId: user.Email,
            ImportBatchId: null,
            MailingId: null,
            DocumentKey: LegalDocumentKeys.ClientProfileConfirmation,
            DocumentVersion: LegalEvidenceTextSnapshots.CurrentVersion,
            TextHash: legalEvidence.ComputeTextHash(snapshot),
            EventTextSnapshot: snapshot,
            Result: LegalEventResults.Confirmed,
            Ip: request.Ip,
            UserAgent: request.UserAgent,
            Route: "/account/confirm-email",
            MetadataJson: JsonSerializer.Serialize(new
            {
                user.Email,
                user.DisplayName,
                ReplyToEmail = user.Email,
                DefaultSenderName = "Письмолёт",
                user.Profile.Status,
                user.Profile.DailySendLimit,
                user.Profile.TotalSendLimit,
                user.Profile.PremoderationRequired,
                source = "email_confirmation_link"
            })));
    }

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
