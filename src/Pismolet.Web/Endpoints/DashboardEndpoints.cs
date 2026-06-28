using System.Net;
using System.Security.Claims;
using Pismolet.Web.Application.Auth;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dashboard", (HttpContext http, IUserAccountService accounts, IMailingService mailings) =>
        {
            var email = CurrentEmail(http);
            if (email is null)
            {
                return Results.Redirect("/account/login");
            }

            var user = accounts.GetByEmail(email);
            if (user is null)
            {
                return Results.Redirect("/account/login");
            }

            var shownUser = user with { Mailings = mailings.ListForOwner(email).ToList() };
            return HtmlRenderer.Html(HtmlRenderer.Page("Личный кабинет", HtmlRenderer.Dashboard(shownUser), authenticated: true));
        }).RequireAuthorization();

        app.MapGet("/mailings/{id:guid}", ShowMailing).RequireAuthorization();

        return app;
    }

    private static IResult ShowMailing(Guid id, HttpContext http, IMailingService mailings, IReplyEventRepository replies)
    {
        var mailing = GetMailing(id, http, mailings);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        var stats = mailing.LastImportStats;
        var next = NextStep(mailing);
        var importInfo = mailing.LastImportBatch is null
            ? string.Empty
            : $"<p class='muted'>Последний импорт: {H(mailing.LastImportBatch.FileName)} ({mailing.LastImportBatch.SourceFormat})</p>";
        var replySummary = replies.GetSummary(mailing.Id);
        var replyInfo = replySummary.TotalReplies == 0
            ? "Ответов пока нет."
            : $"Ответы: {replySummary.TotalReplies}; последний: {replySummary.LastReplyAt:yyyy-MM-dd HH:mm} UTC; статус: {H(replySummary.LastStatus?.ToRu() ?? "неизвестно")}.";
        var replyRetentionHref = $"/legal/reply-retention?returnUrl=/mailings/{mailing.Id}";
        var body = $"<section class='card'><h1>{H(DisplayTitle(mailing))}</h1><p><span class='badge'>{mailing.StatusRu}</span></p>{importInfo}<p>Адресаты: принято {stats.Accepted}; дублей {stats.Duplicates}; невалидных: {stats.Invalid}; исключены по глобальной отписке {stats.GloballySuppressed}; исключены из-за ошибок доставки {stats.ClientSuppressed}.</p><h2>Ответы получателей</h2><p>{replyInfo}</p><p class='muted'>Ответы пересылаются клиенту на email отправителя; здесь показывается только счётчик и безопасный статус. <a href='{replyRetentionHref}'>Правила хранения и удаления ответов</a>.</p><p>{next}</p><p><a href='/dashboard'>Вернуться в ЛК</a></p></section>";
        return HtmlRenderer.Html(HtmlRenderer.Page("Рассылка", body, authenticated: true));
    }

    private static string NextStep(Mailing mailing)
    {
        if (mailing.LastImportStats.Accepted <= 0)
        {
            return $"<a class='button' href='/mailings/{mailing.Id}/recipients'>Загрузить адреса</a>";
        }

        if (mailing.Declaration is null)
        {
            return $"<a class='button' href='/mailings/{mailing.Id}/confirmation'>Подтвердить базу</a>";
        }

        if (mailing.MessageDraft is null)
        {
            return $"<a class='button' href='/mailings/{mailing.Id}/message'>Написать письмо</a>";
        }

        if (mailing.StatusRu is "Оплачено" or "Проверяем перед отправкой" or "На ручной проверке" or "Одобрено")
        {
            return $"<a class='button' href='/mailings/{mailing.Id}/send'>Открыть запуск рассылки</a>";
        }

        if (mailing.StatusRu is "Отклонено")
        {
            return $"<a class='button' href='/mailings/{mailing.Id}/message'>Исправить письмо</a>";
        }

        return $"<a class='button' href='/mailings/{mailing.Id}/payment'>Перейти к проверке и оплате</a>";
    }

    private static string DisplayTitle(Mailing mailing) => string.IsNullOrWhiteSpace(mailing.MessageDraft?.Subject)
        ? "Новая рассылка"
        : mailing.MessageDraft!.Subject;

    private static Mailing? GetMailing(Guid id, HttpContext http, IMailingService mailings)
    {
        var email = CurrentEmail(http);
        return email is null ? null : mailings.GetForOwner(id, email);
    }

    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
