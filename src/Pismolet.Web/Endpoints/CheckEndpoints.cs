using System.Net;
using System.Security.Claims;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class CheckEndpoints
{
    public static IEndpointRouteBuilder MapCheckEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mailings/{id:guid}/checks", ShowChecks).RequireAuthorization();
        app.MapPost("/mailings/{id:guid}/checks/start", StartChecks).RequireAuthorization();
        return app;
    }

    private static IResult ShowChecks(Guid id, HttpContext http, IMailingReviewService reviews)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var result = reviews.GetState(email, id);
        return HtmlRenderer.Html(HtmlRenderer.Page("Проверка перед отправкой", ChecksPage(result), authenticated: true));
    }

    private static IResult StartChecks(Guid id, HttpContext http, IMailingReviewService reviews)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var result = reviews.StartChecks(email, id, ToRequestMetadata(http));
        return HtmlRenderer.Html(HtmlRenderer.Page("Проверка перед отправкой", ChecksPage(result), authenticated: true));
    }

    private static string ChecksPage(MailingReviewResult result)
    {
        if (!result.Ok || result.State is null)
        {
            return HtmlRenderer.Error(result.Error);
        }

        var state = result.State;
        var mailing = state.Mailing;
        var risk = state.RiskResult;
        var paid = state.Payment?.Status == PaymentStatus.Paid;

        if (!paid)
        {
            return $"<section class='card'><h1>Проверка перед отправкой</h1><p class='muted'>{H(mailing.Subject)}</p><p>Сначала оплатите рассылку.</p><p><a class='button' href='/mailings/{mailing.Id}/payment'>Перейти к оплате</a></p></section>";
        }

        if (risk is null)
        {
            return $"<section class='card'><h1>Проверка перед отправкой</h1><p class='muted'>{H(mailing.Subject)}</p><p><span class='badge'>{H(mailing.StatusRu)}</span></p><p>Перед отправкой нужно пройти формальную проверку письма.</p><form method='post' action='/mailings/{mailing.Id}/checks/start'><button class='button'>Проверить перед отправкой</button></form><p><a href='/mailings/{mailing.Id}/payment'>Вернуться к оплате</a></p></section>";
        }

        var effectiveDecision = mailing.Status switch
        {
            MailingStatus.Approved or MailingStatus.Sending or MailingStatus.Sent or MailingStatus.Failed or MailingStatus.Paused => RiskDecision.Approved,
            MailingStatus.Rejected => RiskDecision.Rejected,
            _ => risk.Decision
        };

        var message = effectiveDecision switch
        {
            RiskDecision.Approved => "Рассылка одобрена. Можно перейти к запуску отправки.",
            RiskDecision.Rejected => string.IsNullOrWhiteSpace(risk.PublicExplanation) ? "Рассылка отклонена. Проверьте содержание письма и основания для отправки." : risk.PublicExplanation,
            _ => "Рассылка отправлена на ручную проверку. Мы покажем результат здесь."
        };

        var next = effectiveDecision == RiskDecision.Approved
            ? $"<p><a class='button' href='/mailings/{mailing.Id}/send'>Перейти к отправке</a></p>"
            : string.Empty;

        return $"<section class='card'><h1>Проверка перед отправкой</h1><p class='muted'>{H(mailing.Subject)}</p><p><span class='badge'>{H(mailing.StatusRu)}</span></p><p>{H(message)}</p>{next}<p><a href='/mailings/{mailing.Id}'>Вернуться к рассылке</a></p></section>";
    }

    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);

    private static RequestMetadata ToRequestMetadata(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = http.Request.Headers.UserAgent.ToString();
        return new RequestMetadata(ip, string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
