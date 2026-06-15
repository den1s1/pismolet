using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Pismolet.Web.Application.Auth;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/account/register", () => HtmlRenderer.Html(HtmlRenderer.Page(
            "Регистрация",
            HtmlRenderer.AccountForm("/account/register", "Создать аккаунт", name: true))));

        app.MapPost("/account/register", Register);

        app.MapGet("/account/confirm-email", (string token, IUserAccountService accounts, HttpContext http) =>
        {
            var ok = accounts.ConfirmEmail(token, http.ToRequestMetadata());
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
        var command = new RegisterUserCommand(
            Email: form["email"].ToString(),
            Password: form["password"].ToString(),
            DisplayName: form["displayName"].ToString());

        var result = accounts.Register(command, http.ToRequestMetadata());
        if (!result.Ok)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error(result.Error)));
        }

        var body = $"<section class='card'><h1>Аккаунт создан</h1><p>Dev/fake mailer подготовил ссылку подтверждения.</p><p><a class='button' href='{result.ConfirmLink}'>Подтвердить email</a></p><p><a href='/dev/fake-mailer'>Открыть fake mailer</a></p></section>";
        return HtmlRenderer.Html(HtmlRenderer.Page("Подтверждение", body));
    }

    private static async Task<IResult> ResendConfirmation(HttpContext http, IUserAccountService accounts)
    {
        var form = await http.Request.ReadFormAsync();
        var link = accounts.ResendConfirmation(form["email"].ToString());
        var message = link is null
            ? "Если пользователь существует, письмо будет подготовлено."
            : $"<a class='button' href='{link}'>Dev-ссылка</a>";

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

        var user = accounts.Authenticate(command, http.ToRequestMetadata());
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
            accounts.AuditLogout(email, http.ToRequestMetadata());
        }

        await http.SignOutAsync();
        return Results.Redirect("/");
    }
}
