using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Pismolet.Web.Application.Auth;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Legal;
using Pismolet.Web.Domain.Legal;
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

        app.MapGet("/account/login", LoginPage);

        app.MapPost("/account/login", Login);

        app.MapPost("/account/logout", Logout).RequireAuthorization();

        return app;
    }

    private static IResult LoginPage(HttpContext http)
    {
        var returnUrl = SafeLocalReturnUrl(FirstNonEmpty(http.Request.Query["returnUrl"].ToString(), http.Request.Query["ReturnUrl"].ToString()));
        return HtmlRenderer.Html(HtmlRenderer.Page("Вход", LoginForm(returnUrl)));
    }

    private static async Task<IResult> Register(
        HttpContext http,
        IUserAccountService accounts,
        ILegalEvidenceService legalEvidence)
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

        var request = ToRequestMetadata(http);
        var result = accounts.Register(command, request);
        if (!result.Ok)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error(result.Error)));
        }

        RecordRegistrationConsentEvents(command, request, legalEvidence);

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
        var returnUrl = SafeLocalReturnUrl(FirstNonEmpty(form["returnUrl"].ToString(), http.Request.Query["returnUrl"].ToString(), http.Request.Query["ReturnUrl"].ToString()));

        var user = accounts.Authenticate(command, ToRequestMetadata(http));
        if (user is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page(
                "Ошибка входа",
                LoginForm(returnUrl, "Неверный email/пароль или email ещё не подтверждён.")));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Email),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName)
        };

        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

        return Results.Redirect(returnUrl ?? "/dashboard");
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

    private static void RecordRegistrationConsentEvents(
        RegisterUserCommand command,
        RequestMetadata request,
        ILegalEvidenceService legalEvidence)
    {
        var email = NormalizeEmail(command.Email);
        var metadata = JsonSerializer.Serialize(new
        {
            command.DisplayName,
            command.Phone,
            source = "registration_form",
            offerAccepted = true,
            personalDataConsentAccepted = true
        });

        RecordRegistrationConsentEvent(
            legalEvidence,
            LegalEventTypes.OfferAndRulesAccepted,
            LegalDocumentKeys.OfferAndRules,
            LegalEvidenceTextSnapshots.OfferAndRulesAcceptanceText,
            email,
            request,
            metadata);

        RecordRegistrationConsentEvent(
            legalEvidence,
            LegalEventTypes.ClientPersonalDataConsentAccepted,
            LegalDocumentKeys.ClientPersonalDataConsent,
            LegalEvidenceTextSnapshots.ClientPersonalDataConsentText,
            email,
            request,
            metadata);
    }

    private static void RecordRegistrationConsentEvent(
        ILegalEvidenceService legalEvidence,
        string eventType,
        string documentKey,
        string snapshot,
        string email,
        RequestMetadata request,
        string metadataJson) => legalEvidence.RecordEvent(new LegalEvidenceEventDraft(
            EventType: eventType,
            ClientId: email,
            UserId: email,
            ImportBatchId: null,
            MailingId: null,
            DocumentKey: documentKey,
            DocumentVersion: LegalEvidenceTextSnapshots.CurrentVersion,
            TextHash: legalEvidence.ComputeTextHash(snapshot),
            EventTextSnapshot: snapshot,
            Result: LegalEventResults.Accepted,
            Ip: request.Ip,
            UserAgent: request.UserAgent,
            Route: "/account/register",
            MetadataJson: metadataJson));

    private static string LoginForm(string? returnUrl, string? error = null)
    {
        var errorHtml = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";
        var returnUrlField = string.IsNullOrWhiteSpace(returnUrl) ? string.Empty : $"<input type='hidden' name='returnUrl' value='{H(returnUrl)}'>";
        return $"<section class='panel form-card'><h1>Войти</h1>{errorHtml}<form method='post' action='/account/login'>{returnUrlField}<label>Email<input type='email' name='email' autocomplete='email' required></label><label>Пароль<input type='password' minlength='8' name='password' required></label><button class='btn'>Войти</button></form><p><a href='/account/resend-confirmation'>Повторить подтверждение email</a></p></section>";
    }

    private static bool IsChecked(IFormCollection form, string key)
    {
        var value = form[key].ToString();
        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.OrdinalIgnoreCase);
    }

    private static string? SafeLocalReturnUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("/", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.IndexOf((char)92) >= 0) return null;
        return trimmed;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }

    private static RequestMetadata ToRequestMetadata(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = http.Request.Headers.UserAgent.ToString();

        return new RequestMetadata(ip, string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
