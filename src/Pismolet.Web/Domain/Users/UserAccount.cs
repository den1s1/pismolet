namespace Pismolet.Web.Domain.Users;

public sealed record UserAccount(
    string Email,
    string PasswordHash,
    string DisplayName,
    string ConfirmationToken,
    bool EmailConfirmed,
    ClientProfile Profile,
    List<Mailing> Mailings);
