namespace Pismolet.Web.Application.Auth;

public sealed record RegisterUserResult(bool Ok, string Error, string? ConfirmLink)
{
    public static RegisterUserResult Success(string confirmLink) => new(true, string.Empty, confirmLink);

    public static RegisterUserResult Failure(string error) => new(false, error, null);
}
