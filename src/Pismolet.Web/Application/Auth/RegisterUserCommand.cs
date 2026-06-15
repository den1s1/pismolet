namespace Pismolet.Web.Application.Auth;

public sealed record RegisterUserCommand(string Email, string Password, string DisplayName);
