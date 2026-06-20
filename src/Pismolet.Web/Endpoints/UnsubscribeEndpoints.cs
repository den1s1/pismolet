using System.Net;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
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

    private static IResult Show(string token, HttpContext http, IGlobalUnsubscribeService service)
    {
        var result = service.GetView(token, ToRequestMetadata(http));
        var body = result.TokenValid
            ? $"<section class='card'><h1>Отписка от рассылок</h1><p>Вы можете отписать адрес <b>{H(result.MaskedEmail)}</b> от всех писем, которые отправляются через сервис «Письмолёт».</p><p class='muted'>Отписка действует глобально: этот адрес больше не будет получать письма через сервис ни от одного клиента.</p><form method='post'><button class='button'>Отписаться</button></form></section>"
            : $"<section class='card'><h1>Отписка от рассылок</h1><p>{H(result.Error)}</p><p class='muted'>Если вы уже отписывались раньше, повторных действий не требуется.</p></section>";

        return HtmlRenderer.Html(HtmlRenderer.Page("Отписка", body));
    }

    private static IResult Confirm(string token, HttpContext http, IGlobalUnsubscribeService service)
    {
        var result = service.Confirm(token, ToRequestMetadata(http));
        var title = result.Ok ? "Вы отписаны" : "Отписка не выполнена";
        var body = result.Ok
            ? $"<section class='card'><h1>Вы отписаны</h1><p>{H(result.Message)}</p><p class='muted'>Повторный переход по этой ссылке безопасен и не создаёт дублей.</p></section>"
            : $"<section class='card'><h1>Отписка от рассылок</h1><p>{H(result.Message)}</p><p class='muted'>Мы не раскрываем сведения о существовании адреса или рассылки по невалидной ссылке.</p></section>";

        return HtmlRenderer.Html(HtmlRenderer.Page(title, body));
    }

    private static RequestMetadata ToRequestMetadata(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = http.Request.Headers.UserAgent.ToString();
        return new RequestMetadata(ip, string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
