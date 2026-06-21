using System.Net;
using System.Security.Claims;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
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

    private static IResult ShowSend(Guid id, HttpContext http, IMailingSendService sender, IReplyEventRepository replies)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var result = sender.GetState(email, id);
        return HtmlRenderer.Html(HtmlRenderer.Page("Запуск рассылки", SendPage(result, replies.GetSummary(id), null), authenticated: true));
    }

    private static IResult StartSend(Guid id, HttpContext http, IMailingSendService sender, IReplyEventRepository replies)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var result = sender.StartSending(email, id, ToRequestMetadata(http));
        return HtmlRenderer.Html(HtmlRenderer.Page("Рассылка запущена", SendPage(result, replies.GetSummary(id), result.Ok ? "Отправка поставлена в очередь." : result.Error), authenticated: true));
    }

    private static IResult ResumeSend(Guid id, HttpContext http, IMailingSendService sender, IReplyEventRepository replies)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var result = sender.ResumeSending(email, id, ToRequestMetadata(http));
        return HtmlRenderer.Html(HtmlRenderer.Page("Рассылка запущена", SendPage(result, replies.GetSummary(id), result.Ok ? "Продолжение отправки поставлено в очередь." : result.Error), authenticated: true));
    }

    private static string SendPage(MailingSendResult result, ReplySummary replySummary, string? message)
    {
        if (result.State is null)
        {
            return HtmlRenderer.Error(result.Error);
        }

        var state = result.State;
        var mailing = state.Mailing;
        var summary = state.Summary;
        var launched = result.Ok && !string.IsNullOrWhiteSpace(message) && message.Contains("поставлен", StringComparison.OrdinalIgnoreCase);
        var title = launched ? "Рассылка запущена" : mailing.Status switch
        {
            MailingStatus.Approved => "Запуск рассылки",
            MailingStatus.Sending => "Рассылка отправляется",
            MailingStatus.Sent => "Рассылка отправлена",
            MailingStatus.Paused => "Отправка приостановлена",
            MailingStatus.Failed => "Есть ошибки отправки",
            _ => "Отправка рассылки"
        };
        var alert = string.IsNullOrWhiteSpace(message)
            ? string.Empty
            : result.Ok
                ? $"<p class='notice'>{H(message)}</p>"
                : $"<p class='error-message'>{H(message)}</p>";
        var action = mailing.Status switch
        {
            MailingStatus.Approved => $"<form method='post' action='/mailings/{mailing.Id}/send/start'><button class='button'>Запустить отправку</button></form>",
            MailingStatus.Paused => $"<form method='post' action='/mailings/{mailing.Id}/send/resume'><button class='button'>Продолжить отправку</button></form><p class='muted'>Достигнут дневной лимит отправки. Продолжение возможно после смены дня или изменения лимита администратором.</p>",
            MailingStatus.Sending => $"<p><span class='badge warn'>Отправка выполняется</span></p><p><a class='button' href='/mailings/{mailing.Id}/send'>Обновить статус</a></p>",
            MailingStatus.Sent => "<p><span class='badge ok'>Отправка завершена</span></p>",
            MailingStatus.Failed => "<p><span class='badge danger'>Есть ошибки отправки</span></p><p class='muted'>Подробности ошибок доступны администратору; пользователю показываем только безопасную сводку.</p>",
            _ => "<p class='muted'>Отправка будет доступна после оплаты и одобрения рассылки.</p>"
        };

        var deliveryNote = summary.ProviderAccepted + summary.Delivered + summary.SoftBounced + summary.HardBounced + summary.Complaints + summary.Rejected == 0
            ? "<p class='muted'>Ожидаем статус доставки от провайдера.</p>"
            : string.Empty;
        var replyStatus = replySummary.TotalReplies == 0
            ? "Ответов пока нет."
            : $"Получено ответов: {replySummary.TotalReplies}. Последний: {replySummary.LastReplyAt:yyyy-MM-dd HH:mm} UTC, статус: {H(replySummary.LastStatus?.ToRu() ?? "неизвестно")}";

        var devRows = state.Events.Count == 0
            ? "<tr><td colspan='5'>Событий отправки пока нет.</td></tr>"
            : string.Join(string.Empty, state.Events.OrderBy(x => x.RecipientEmail).Select(x => $"<tr><td>{H(MaskEmail(x.RecipientEmail))}</td><td>{H(x.Status.ToRu())}</td><td>{H(x.DeliveryStatus.ToRu())}</td><td>{H(x.Reason == SendSkipReason.None ? "" : x.Reason.ToString())}</td><td>{H(x.ErrorCode ?? "")}</td></tr>"));

        return $"""
            <section class='wizard-shell send-wizard'>
              <div class='wizard-steps' aria-label='Шаги создания рассылки'>
                <span class='wizard-step done'>Черновик</span>
                <span class='wizard-step done'>1. Адреса</span>
                <span class='wizard-step done'>2. Письмо</span>
                <span class='wizard-step done'>3. Проверка и оплата</span>
                <span class='wizard-step current'>Запуск</span>
              </div>
              <section class='panel'>
                <div class='topline'>
                  <div>
                    <p class='eyebrow'>Финальный запуск</p>
                    <h1>{H(title)}</h1>
                    <p class='muted'>{H(mailing.Subject)}</p>
                  </div>
                  <span class='badge warn'>{H(mailing.StatusRu)}</span>
                </div>
                {alert}
                <div class='notice warn'>Отправка идёт постепенно. Сервис ставит письма в очередь, соблюдает дневные лимиты и исключает отписавшихся получателей перед отправкой.</div>
                <div class='stats launch-stats'>
                  <div class='stat'><b>{summary.Pending}</b><span>Писем в очереди</span></div>
                  <div class='stat'><b>{summary.TotalAcceptedRecipients}</b><span>Оплачено писем</span></div>
                  <div class='stat'><b>{summary.Complaints}</b><span>Жалоб сейчас</span></div>
                  <div class='stat'><b>{replySummary.TotalReplies}</b><span>Ответов сейчас</span></div>
                </div>
                <div class='payment-grid launch-grid'>
                  <section class='box'>
                    <h2>Отправка</h2>
                    <table><thead><tr><th>Показатель</th><th>Значение</th></tr></thead><tbody><tr><td>Принято к отправке</td><td>{summary.AcceptedForSending}</td></tr><tr><td>Отправлено провайдеру</td><td>{summary.Sent}</td></tr><tr><td>Ошибки отправки</td><td>{summary.Failed}</td></tr><tr><td>Глобально отписано / исключено</td><td>{summary.Suppressed}</td></tr><tr><td>Исключено из-за ошибки доставки у клиента</td><td>{summary.ClientSuppressed}</td></tr><tr><td>Приостановлено по лимиту</td><td>{summary.PausedByLimit}</td></tr><tr><td>Ожидает отправки</td><td>{summary.Pending}</td></tr><tr><td>Всего принятых адресов</td><td>{summary.TotalAcceptedRecipients}</td></tr></tbody></table>
                    {action}
                  </section>
                  <section class='box'>
                    <h2>Доставка и ответы</h2>
                    {deliveryNote}
                    <table><thead><tr><th>Статус доставки</th><th>Значение</th></tr></thead><tbody><tr><td>Принято провайдером</td><td>{summary.ProviderAccepted}</td></tr><tr><td>Доставлено</td><td>{summary.Delivered}</td></tr><tr><td>Временная ошибка</td><td>{summary.SoftBounced}</td></tr><tr><td>Постоянная ошибка</td><td>{summary.HardBounced}</td></tr><tr><td>Жалоба</td><td>{summary.Complaints}</td></tr><tr><td>Отклонено</td><td>{summary.Rejected}</td></tr></tbody></table>
                    <h3>Ответы получателей</h3>
                    <p>{replyStatus}</p>
                    <p class='muted'>Ответы пересылаются клиенту на email отправителя. Личный кабинет показывает только счётчик и статус пересылки, без inbox и без raw provider payload.</p>
                  </section>
                </div>
                <details><summary>Dev-сводка событий</summary><table><thead><tr><th>Email</th><th>Статус</th><th>Доставка</th><th>Причина</th><th>Ошибка</th></tr></thead><tbody>{devRows}</tbody></table></details>
                <div class='actions'><a class='btn secondary' href='/dashboard'>Вернуться в историю</a><a class='btn ghost' href='/mailings/{mailing.Id}'>Открыть карточку рассылки</a></div>
              </section>
            </section>
            """;
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
