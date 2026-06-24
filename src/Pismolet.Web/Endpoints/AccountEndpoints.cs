using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Pismolet.Web.Application.Auth;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/account/register", () => HtmlRenderer.Html(HtmlRenderer.Page(
            "Регистрация",
            HtmlRenderer.AccountForm("/account/register", "Создать аккаунт", name: true, registrationConsents: true))));

        app.MapPost("/account/register", Register);

        app.MapGet("/account/confirm-email", (string token, IUserAccountService accounts, HttpContext http) =>
        {
            var ok = accounts.ConfirmEmail(token, ToRequestMetadata(http));
            var body = ok
                ? "<section class='card'><h1>Email подтверждён</h1><p><a class='button' href='/account/login'>Войти</a></p></section>"
                : HtmlRenderer.Error("Ссылка подтверждения недействительна.");

            return HtmlRenderer.Html(HtmlRenderer.Page("Email", body));
        });

        app.MapGet("/account/resend-confirmation", () => HtmlRenderer.Html(HtmlRenderer.Page(
            "Повторить подтверждение",
            HtmlRenderer.AccountForm("/account/resend-confirmation", "Отправить ссылку", name: false, password: false))));

        app.MapPost("/account/resend-confirmation", ResendConfirmation);

        app.MapGet("/account/login", () => HtmlRenderer.Html(HtmlRenderer.Page(
            "Вход",
            HtmlRenderer.AccountForm("/account/login", "Войти", name: false))));

        app.MapPost("/account/login", Login);

        app.MapPost("/account/logout", Logout).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> Register(HttpContext http, IUserAccountService accounts)
    {
        var form = await http.Request.ReadFormAsync();
        if (!IsChecked(form, "acceptOffer") || !IsChecked(form, "acceptPrivacy"))
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Подтвердите обязательные условия регистрации.")));
        }

        var command = new RegisterUserCommand(
            Email: form["email"].ToString(),
            Password: form["password"].ToString(),
            DisplayName: form["displayName"].ToString(),
            Phone: form["phone"].ToString());

        var result = accounts.Register(command, ToRequestMetadata(http));
        if (!result.Ok)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error(result.Error)));
        }

        const string body = "<section class='card'><h1>Аккаунт создан</h1><p>Мы отправили ссылку подтверждения на указанный email. Перейдите по ней, чтобы активировать аккаунт.</p><p><a class='button' href='/account/login'>К странице входа</a></p></section>";
        return HtmlRenderer.Html(HtmlRenderer.Page("Подтверждение", body));
    }

    private static async Task<IResult> ResendConfirmation(HttpContext http, IUserAccountService accounts)
    {
        var form = await http.Request.ReadFormAsync();
        accounts.ResendConfirmation(form["email"].ToString());
        const string message = "Если пользователь существует, мы отправили повторную ссылку подтверждения на email.";

        return HtmlRenderer.Html(HtmlRenderer.Page(
            "Повторить подтверждение",
            $"<section class='card'><h1>Готово</h1><p>{message}</p></section>"));
    }

    private static async Task<IResult> Login(HttpContext http, IUserAccountService accounts)
    {
        var form = await http.Request.ReadFormAsync();
        var command = new LoginUserCommand(
            Email: form["email"].ToString(),
            Password: form["password"].ToString());

        var user = accounts.Authenticate(command, ToRequestMetadata(http));
        if (user is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page(
                "Ошибка входа",
                HtmlRenderer.Error("Неверный email/пароль или email ещё не подтверждён.")));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Email),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName)
        };

        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));

        return Results.Redirect("/dashboard");
    }

    private static async Task<IResult> Logout(HttpContext http, IUserAccountService accounts)
    {
        var email = http.User.FindFirstValue(ClaimTypes.Email);
        if (email is not null)
        {
            accounts.AuditLogout(email, ToRequestMetadata(http));
        }

        await http.SignOutAsync();
        return Results.Redirect("/");
    }

    private static bool IsChecked(IFormCollection form, string key)
    {
        var value = form[key].ToString();
        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.OrdinalIgnoreCase);
    }

    private static RequestMetadata ToRequestMetadata(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = http.Request.Headers.UserAgent.ToString();

        return new RequestMetadata(ip, string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);
    }
}
