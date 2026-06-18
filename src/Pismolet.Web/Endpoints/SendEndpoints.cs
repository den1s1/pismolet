using System.Net;
using System.Security.Claims;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class SendEndpoints
{
    public static IEndpointRouteBuilder MapSendEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mailings/{id:guid}/send", ShowSend).RequireAuthorization();
        app.MapPost("/mailings/{id:guid}/send/start", StartSend).RequireAuthorization();
        app.MapPost("/mailings/{id:guid}/send/resume", ResumeSend).RequireAuthorization();
        return app;
    }

    private static IResult ShowSend(Guid id, HttpContext http, IMailingSendService sender)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var result = sender.GetState(email, id);
        return HtmlRenderer.Html(HtmlRenderer.Page("Отправка рассылки", SendPage(result, null)));
    }

    private static IResult StartSend(Guid id, HttpContext http, IMailingSendService sender)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var result = sender.StartSending(email, id, ToRequestMetadata(http));
        return HtmlRenderer.Html(HtmlRenderer.Page("Отправка рассылки", SendPage(result, result.Ok ? "Отправка поставлена в очередь." : result.Error)));
    }

    private static IResult ResumeSend(Guid id, HttpContext http, IMailingSendService sender)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var result = sender.ResumeSending(email, id, ToRequestMetadata(http));
        return HtmlRenderer.Html(HtmlRenderer.Page("Отправка рассылки", SendPage(result, result.Ok ? "Продолжение отправки поставлено в очередь." : result.Error)));
    }

    private static string SendPage(MailingSendResult result, string? message)
    {
        if (result.State is null)
        {
            return HtmlRenderer.Error(result.Error);
        }

        var state = result.State;
        var mailing = state.Mailing;
        var summary = state.Summary;
        var alert = string.IsNullOrWhiteSpace(message) ? string.Empty : result.Ok ? $"<p class='success'>{H(message)}</p>" : $"<p class='error'>{H(message)}</p>";
        var action = mailing.Status switch
        {
            MailingStatus.Approved => $"<form method='post' action='/mailings/{mailing.Id}/send/start'><button class='button'>Запустить отправку</button></form>",
            MailingStatus.Paused => $"<form method='post' action='/mailings/{mailing.Id}/send/resume'><button class='button'>Продолжить отправку</button></form><p class='muted'>Достигнут дневной лимит отправки. Продолжение возможно после смены дня или изменения лимита администратором.</p>",
            MailingStatus.Sending => $"<p><span class='badge'>Отправка выполняется</span></p><p><a class='button' href='/mailings/{mailing.Id}/send'>Обновить статус</a></p>",
            MailingStatus.Sent => "<p><span class='badge'>Отправка завершена</span></p>",
            MailingStatus.Failed => "<p><span class='badge'>Есть ошибки отправки</span></p><p class='muted'>Подробности ошибок доступны администратору; пользователю показываем только безопасную сводку.</p>",
            _ => "<p class='muted'>Отправка будет доступна после оплаты и одобрения рассылки.</p>"
        };

        var devRows = state.Events.Count == 0
            ? "<tr><td colspan='4'>Событий отправки пока нет.</td></tr>"
            : string.Join(string.Empty, state.Events.OrderBy(x => x.RecipientEmail).Select(x => $"<tr><td>{H(MaskEmail(x.RecipientEmail))}</td><td>{H(x.Status.ToRu())}</td><td>{H(x.Reason == SendSkipReason.None ? "" : x.Reason.ToString())}</td><td>{H(x.ErrorCode ?? "")}</td></tr>"));

        return $"<section class='card'><h1>Отправка рассылки</h1><p class='muted'>{H(mailing.Subject)}</p><p><span class='badge'>{H(mailing.StatusRu)}</span></p>{alert}<table><thead><tr><th>Показатель</th><th>Значение</th></tr></thead><tbody><tr><td>Принято к отправке</td><td>{summary.AcceptedForSending}</td></tr><tr><td>Отправлено</td><td>{summary.Sent}</td></tr><tr><td>Ошибки</td><td>{summary.Failed}</td></tr><tr><td>Отписано / исключено</td><td>{summary.Suppressed}</td></tr><tr><td>Приостановлено по лимиту</td><td>{summary.PausedByLimit}</td></tr><tr><td>Ожидает отправки</td><td>{summary.Pending}</td></tr><tr><td>Всего принятых адресов</td><td>{summary.TotalAcceptedRecipients}</td></tr></tbody></table>{action}<details><summary>Dev-сводка событий</summary><table><thead><tr><th>Email</th><th>Статус</th><th>Причина</th><th>Ошибка</th></tr></thead><tbody>{devRows}</tbody></table></details><p><a href='/mailings/{mailing.Id}'>Вернуться к рассылке</a></p></section>";
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? email : $"{email[..1]}***{email[at..]}";
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
