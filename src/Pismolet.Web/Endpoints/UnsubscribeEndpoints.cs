using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Legal;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Legal;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class UnsubscribeEndpoints
{
    public static IEndpointRouteBuilder MapUnsubscribeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/unsubscribe/{token}", Show);
        app.MapPost("/unsubscribe/{token}", Confirm);
        app.MapGet("/u/{token}", Show);
        app.MapPost("/u/{token}", Confirm);
        return app;
    }

    private static IResult Show(string token, HttpContext http, [FromServices] IGlobalUnsubscribeService service)
    {
        var result = service.GetView(token, ToRequestMetadata(http));
        var legalHref = LegalHref(http, token);
        var body = result.TokenValid
            ? $"<section class='card'><h1>Отписка от рассылок</h1><p>Вы можете отписать адрес <b>{H(result.MaskedEmail)}</b> от всех писем, которые отправляются через сервис «Письмолёт».</p><p class='muted'>Отписка действует глобально: этот адрес больше не будет получать письма через сервис ни от одного клиента. <a href='{H(legalHref)}'>Правила отписки</a></p><form method='post'><button class='button'>Отписаться</button></form></section>"
            : $"<section class='card'><h1>Отписка от рассылок</h1><p>{H(result.Error)}</p><p class='muted'>Если вы уже отписывались раньше, повторных действий не требуется. <a href='{H(legalHref)}'>Правила отписки</a></p></section>";

        return HtmlRenderer.Html(HtmlRenderer.Page("Отписка", body));
    }

    private static IResult Confirm(
        string token,
        HttpContext http,
        [FromServices] IGlobalUnsubscribeService service,
        [FromServices] IUnsubscribeTokenService tokens,
        [FromServices] ILegalEvidenceService legalEvidence)
    {
        var result = service.Confirm(token, ToRequestMetadata(http));
        if (result.Ok)
        {
            RecordUnsubscribeEvidence(token, http, tokens, legalEvidence, result);
        }

        var title = result.Ok ? "Вы отписаны" : "Отписка не выполнена";
        var legalHref = LegalHref(http, token);
        var body = result.Ok
            ? $"<section class='card'><h1>Вы отписаны</h1><p>{H(result.Message)}</p><p class='muted'>Повторный переход по этой ссылке безопасен и не создаёт дублей. <a href='{H(legalHref)}'>Правила отписки</a></p></section>"
            : $"<section class='card'><h1>Отписка от рассылок</h1><p>{H(result.Message)}</p><p class='muted'>Мы не раскрываем сведения о существовании адреса или рассылки по невалидной ссылке. <a href='{H(legalHref)}'>Правила отписки</a></p></section>";

        return HtmlRenderer.Html(HtmlRenderer.Page(title, body));
    }

    private static void RecordUnsubscribeEvidence(
        string token,
        HttpContext http,
        IUnsubscribeTokenService tokens,
        ILegalEvidenceService legalEvidence,
        UnsubscribeConfirmResult result)
    {
        var validation = tokens.Validate(token);
        if (!validation.Ok || validation.Payload is null)
        {
            return;
        }

        var payload = validation.Payload;
        var route = http.Request.Path.Value ?? $"/unsubscribe/{token}";
        var snapshot = $"{LegalEvidenceTextSnapshots.GlobalUnsubscribeConfirmationText} Адрес: {payload.Email}. Рассылка: {payload.MailingId}.";

        legalEvidence.RecordEvent(new LegalEvidenceEventDraft(
            LegalEventTypes.GlobalUnsubscribeConfirmed,
            payload.Email,
            null,
            null,
            payload.MailingId,
            LegalDocumentKeys.GlobalUnsubscribeConfirmation,
            LegalEvidenceTextSnapshots.CurrentVersion,
            legalEvidence.ComputeTextHash(LegalEvidenceTextSnapshots.GlobalUnsubscribeConfirmationText),
            snapshot,
            LegalEventResults.Confirmed,
            http.Connection.RemoteIpAddress?.ToString(),
            http.Request.Headers.UserAgent.ToString(),
            route,
            JsonSerializer.Serialize(new
            {
                mailing_id = payload.MailingId,
                recipient_key = payload.RecipientKey,
                already_suppressed = result.AlreadySuppressed,
                token_purpose = payload.Purpose
            })));
    }

    private static RequestMetadata ToRequestMetadata(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = http.Request.Headers.UserAgent.ToString();
        return new RequestMetadata(ip, string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);
    }

    private static string LegalHref(HttpContext http, string token)
    {
        var returnUrl = http.Request.Path.Value ?? $"/unsubscribe/{token}";
        return $"/legal/unsubscribe?returnUrl={WebUtility.UrlEncode(returnUrl)}";
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
