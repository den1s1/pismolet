using System.Net;
using System.Security.Claims;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin", () => HtmlRenderer.Html(HtmlRenderer.Page("Админ-зона", "<section class='card'><h1>Админ-зона</h1><p>Внутренние инструменты MVP.</p><p><a class='button' href='/admin/moderation'>Очередь модерации</a></p><p><a class='button' href='/admin/limits'>Дневные лимиты клиентов</a></p><p class='muted'>TODO: подключить роли администратора после появления RBAC-модели.</p><p><a href='/dashboard'>Вернуться в ЛК</a></p></section>"))).RequireAuthorization();
        app.MapGet("/admin/moderation", ShowQueue).RequireAuthorization();
        app.MapGet("/admin/moderation/{reviewId:guid}", ShowReview).RequireAuthorization();
        app.MapPost("/admin/moderation/{reviewId:guid}/approve", Approve).RequireAuthorization();
        app.MapPost("/admin/moderation/{reviewId:guid}/reject", Reject).RequireAuthorization();
        app.MapGet("/admin/limits", ShowLimits).RequireAuthorization();
        app.MapPost("/admin/limits", UpdateLimit).RequireAuthorization();
        return app;
    }

    private static IResult ShowQueue(IModerationAdminService moderation)
    {
        var items = moderation.ListOpen();
        var rows = items.Count == 0
            ? "<tr><td colspan='5'>Очередь пуста.</td></tr>"
            : string.Join(string.Empty, items.Select(item =>
            {
                var mailing = item.Mailing;
                var reasons = SafeReasons(item.RiskResult);
                return $"<tr><td>{item.Review.CreatedAt:yyyy-MM-dd HH:mm}</td><td>{H(mailing?.MessageDraft?.Subject ?? mailing?.Subject ?? "Без темы")}</td><td>{H(mailing?.OwnerEmail ?? "неизвестно")}</td><td>{H(reasons)}</td><td><a href='/admin/moderation/{item.Review.Id}'>Открыть</a></td></tr>";
            }));

        var body = $"<section class='card'><h1>Очередь модерации</h1><p class='muted'>Доступ пока ограничен общей авторизацией. TODO: добавить роль администратора.</p><table><thead><tr><th>Дата</th><th>Тема</th><th>Клиент</th><th>Причины</th><th></th></tr></thead><tbody>{rows}</tbody></table><p><a href='/admin'>Вернуться в админ-зону</a></p></section>";
        return HtmlRenderer.Html(HtmlRenderer.Page("Очередь модерации", body));
    }

    private static IResult ShowReview(Guid reviewId, IModerationAdminService moderation, IMessageRenderingService renderer)
    {
        var result = moderation.Get(reviewId);
        return HtmlRenderer.Html(HtmlRenderer.Page("Карточка модерации", ReviewPage(result, renderer)));
    }

    private static async Task<IResult> Approve(Guid reviewId, HttpContext http, IModerationAdminService moderation, IMessageRenderingService renderer)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var form = await http.Request.ReadFormAsync();
        var result = moderation.Approve(reviewId, email, form["comment"].ToString(), ToRequestMetadata(http));
        return HtmlRenderer.Html(HtmlRenderer.Page("Карточка модерации", ReviewPage(result, renderer)));
    }

    private static async Task<IResult> Reject(Guid reviewId, HttpContext http, IModerationAdminService moderation, IMessageRenderingService renderer)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var form = await http.Request.ReadFormAsync();
        var result = moderation.Reject(reviewId, email, form["comment"].ToString(), ToRequestMetadata(http));
        return HtmlRenderer.Html(HtmlRenderer.Page("Карточка модерации", ReviewPage(result, renderer)));
    }

    private static IResult ShowLimits() => HtmlRenderer.Html(HtmlRenderer.Page("Дневные лимиты", LimitPage(null, null, null)));

    private static async Task<IResult> UpdateLimit(HttpContext http, IClientSendLimitAdminService limits)
    {
        var adminEmail = CurrentEmail(http);
        if (adminEmail is null) return Results.Redirect("/account/login");
        var form = await http.Request.ReadFormAsync();
        var clientEmail = form["clientEmail"].ToString();
        var rawLimit = form["dailyLimit"].ToString();
        if (!int.TryParse(rawLimit, out var dailyLimit))
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Дневные лимиты", LimitPage("Укажите лимит числом.", clientEmail, rawLimit)));
        }

        var result = limits.UpdateDailyLimit(clientEmail, dailyLimit, adminEmail, ToRequestMetadata(http));
        var message = result.Ok && result.User is not null
            ? $"Лимит клиента {result.User.Email} изменён на {result.User.Profile.DailySendLimit}."
            : result.Error;
        return HtmlRenderer.Html(HtmlRenderer.Page("Дневные лимиты", LimitPage(message, clientEmail, dailyLimit.ToString())));
    }

    private static string ReviewPage(AdminModerationResult result, IMessageRenderingService renderer)
    {
        if (!result.Ok || result.Review is null)
        {
            return HtmlRenderer.Error(result.Error);
        }

        var review = result.Review;
        var mailing = result.Mailing;
        var risk = result.RiskResult;
        var preview = mailing is null ? null : renderer.RenderPreview(mailing);
        var resolvedAt = review.ResolvedAt?.ToString("yyyy-MM-dd HH:mm") ?? "не указано";
        var rules = risk is null || risk.TriggeredRules.Count == 0
            ? "<li>Формальная проверка не указала дополнительные причины.</li>"
            : string.Join(string.Empty, risk.TriggeredRules.Select(rule => $"<li>{H(rule.PublicReason)} — {rule.Score} баллов</li>"));
        var actions = review.Status == ModerationReviewStatus.Open
            ? $"<form method='post' action='/admin/moderation/{review.Id}/approve'><label>Комментарий модератора<input name='comment'></label><button class='button'>Одобрить</button></form><form method='post' action='/admin/moderation/{review.Id}/reject'><label>Комментарий модератора<input name='comment'></label><button class='button danger'>Отклонить</button></form>"
            : $"<p><span class='badge'>{review.Status.ToRu()}</span></p><p class='muted'>Решение: {H(review.ResolvedBy)} · {H(resolvedAt)}</p>";
        var logs = result.Logs.Count == 0
            ? "<li>Действий пока нет.</li>"
            : string.Join(string.Empty, result.Logs.Select(log => $"<li>{log.CreatedAt:yyyy-MM-dd HH:mm}: {H(log.ActorEmail)} — {H(log.Action)} ({H(log.PreviousState)} → {H(log.NewState)})</li>"));

        return $"<section class='card'><h1>Карточка модерации</h1><p><span class='badge'>{review.Status.ToRu()}</span></p><p><strong>Рассылка:</strong> {H(mailing?.Subject ?? "не найдена")}</p><p><strong>Клиент:</strong> {H(mailing?.OwnerEmail ?? "неизвестно")}</p><p><strong>Причина ручной проверки:</strong> {H(review.Reason)}</p><h2>Письмо</h2><p><strong>Отправитель:</strong> {H(mailing?.MessageDraft?.SenderName)}</p><p><strong>Тема:</strong> {H(mailing?.MessageDraft?.Subject)}</p><pre>{H(mailing?.MessageDraft?.Body)}</pre><h2>Служебные блоки</h2><p>{H(preview?.ReasonBlock)}</p><p>{H(preview?.UnsubscribeUrl)}</p><p>{H(preview?.ServiceIdentifier)}</p><h2>Формальные причины</h2><ul>{rules}</ul><h2>Решение</h2>{actions}<h2>Лог действий</h2><ul>{logs}</ul><p><a href='/admin/moderation'>Вернуться к очереди</a></p></section>";
    }

    private static string LimitPage(string? message, string? clientEmail, string? dailyLimit)
    {
        var alert = string.IsNullOrWhiteSpace(message) ? string.Empty : $"<p class='muted'>{H(message)}</p>";
        return $"<section class='card form-card'><h1>Дневные лимиты клиентов</h1><p class='muted'>Dev/admin форма Sprint 6. Изменение влияет на новые запуски и resume, но не переписывает уже созданные события отправки.</p>{alert}<form method='post' action='/admin/limits'><label>Email клиента<input type='email' name='clientEmail' required value='{H(clientEmail)}'></label><label>Новый дневной лимит<input type='number' min='0' max='100000' name='dailyLimit' required value='{H(dailyLimit)}'></label><button class='button'>Сохранить лимит</button></form><p><a href='/admin'>Вернуться в админ-зону</a></p></section>";
    }

    private static string SafeReasons(RiskCheckResult? risk) => risk is null || risk.TriggeredRules.Count == 0
        ? "Нет дополнительных причин"
        : string.Join("; ", risk.TriggeredRules.Select(rule => rule.PublicReason).Distinct());

    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);

    private static RequestMetadata ToRequestMetadata(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = http.Request.Headers.UserAgent.ToString();
        return new RequestMetadata(ip, string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
