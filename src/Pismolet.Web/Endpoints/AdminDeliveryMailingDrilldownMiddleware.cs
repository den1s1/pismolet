using System.Net;
using Microsoft.AspNetCore.Authorization;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class AdminDeliveryMailingDrilldownMiddleware
{
    private const int DefaultDays = 14;
    private const int MaxDays = 90;
    private const int RecentLimit = 200;

    public static IApplicationBuilder UseAdminDeliveryMailingDrilldown(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        var path = http.Request.Path.Value ?? string.Empty;
        const string prefix = "/admin/delivery/mailing/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        var authorization = http.RequestServices.GetRequiredService<IAuthorizationService>();
        var authorizationResult = await authorization.AuthorizeAsync(http.User, http, AdminEndpoints.AdminPolicyName);
        if (!authorizationResult.Succeeded)
        {
            http.Response.Redirect("/account/login");
            return;
        }

        var rawId = Uri.UnescapeDataString(path[prefix.Length..]).Trim('/');
        if (!Guid.TryParse(rawId, out var mailingId))
        {
            await next();
            return;
        }

        var sendEvents = http.RequestServices
            .GetRequiredService<ISendEventRepository>()
            .ListByMailingId(mailingId)
            .ToArray();
        if (sendEvents.Length == 0)
        {
            await next();
            return;
        }

        var mailings = http.RequestServices.GetRequiredService<IMailingRepository>();
        var days = ReadDays(http);
        var since = DateTimeOffset.UtcNow.AddDays(-days);
        var ownerEmail = sendEvents
            .Select(x => x.OwnerEmail)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
        var mailing = string.IsNullOrWhiteSpace(ownerEmail)
            ? null
            : mailings.ListForOwner(ownerEmail).FirstOrDefault(x => x.Id == mailingId);
        var title = mailing is null
            ? $"Рассылка {mailingId}"
            : string.IsNullOrWhiteSpace(mailing.Subject) ? "Рассылка без темы" : mailing.Subject;
        var createdAt = mailing?.CreatedAt.ToString("yyyy-MM-dd HH:mm") ?? "-";
        var ownerLink = string.IsNullOrWhiteSpace(ownerEmail)
            ? "/admin/delivery"
            : $"/admin/delivery/client/{Uri.EscapeDataString(ownerEmail)}?days={days}";
        var hard = nameof(DeliveryStatus.HardBounce);
        var soft = nameof(DeliveryStatus.SoftBounce);
        var rejected = nameof(DeliveryStatus.Rejected);
        var complaint = nameof(DeliveryStatus.Complaint);
        var delivered = nameof(DeliveryStatus.Delivered);
        var notReported = nameof(DeliveryStatus.NotReported);
        var periodEvents = sendEvents
            .Where(x => (x.LastDeliveryEventAt ?? x.UpdatedAt) >= since)
            .ToArray();
        var problemRows = periodEvents
            .Where(x => IsProblem(x.DeliveryStatus.ToString(), hard, soft, rejected, complaint))
            .OrderByDescending(x => x.LastDeliveryEventAt ?? x.UpdatedAt)
            .ThenBy(x => x.RecipientEmail)
            .Take(RecentLimit)
            .ToArray();
        var body = $"""
            <section class='admin-panel'>
                <div class='admin-title-row'>
                    <div>
                        <p class='eyebrow'>Доставка рассылки</p>
                        <h1>{H(title)}</h1>
                        <p class='admin-muted'>{H(ownerEmail)} · создана {H(createdAt)} · {mailingId}</p>
                    </div>
                    <a class='admin-export' href='{ownerLink}'>К доставке клиента</a>
                </div>
                <form class='admin-filters' method='get' action='/admin/delivery/mailing/{mailingId}'>
                    <label>Период, дней<input type='number' min='1' max='{MaxDays}' name='days' value='{days}'></label>
                    <button class='admin-button' type='submit'>Обновить</button>
                    <a class='admin-link' href='/admin/delivery/mailing/{mailingId}'>Сбросить</a>
                </form>
                <div class='admin-stats'>
                    <div class='admin-stat'><b>{periodEvents.Length}</b><span>Событий за {days} дн.</span></div>
                    <div class='admin-stat'><b>{Count(periodEvents, delivered)}</b><span>Delivered</span></div>
                    <div class='admin-stat'><b>{Count(periodEvents, hard)}</b><span>HardBounce</span></div>
                    <div class='admin-stat'><b>{Count(periodEvents, soft)}</b><span>SoftBounce</span></div>
                    <div class='admin-stat'><b>{Count(periodEvents, rejected)}</b><span>Rejected</span></div>
                    <div class='admin-stat'><b>{Count(periodEvents, complaint)}</b><span>Complaint</span></div>
                </div>
                <p class='admin-muted'>NotReported за период: {Count(periodEvents, notReported)}. <a class='admin-link' href='/admin/campaigns/{mailingId}'>Открыть обычную страницу рассылки</a></p>
                {RecommendationBlock(periodEvents, hard, soft, rejected, complaint)}
                <div class='section-head'><div><p class='eyebrow'>События</p><h2>Проблемные события доставки этой рассылки</h2></div></div>
                {Table(problemRows, ProblemEventRowHtml)}
                <p><a class='admin-link' href='{ownerLink}'>Вернуться к доставке клиента</a></p>
            </section>
            """;

        http.Response.ContentType = "text/html; charset=utf-8";
        await http.Response.WriteAsync(HtmlRenderer.Page("Админка - доставка рассылки", body, authenticated: true));
    });

    private static int ReadDays(HttpContext http)
    {
        var raw = http.Request.Query["days"].ToString();
        return int.TryParse(raw, out var days) ? Math.Clamp(days, 1, MaxDays) : DefaultDays;
    }

    private static bool IsProblem(string status, string hard, string soft, string rejected, string complaint) =>
        string.Equals(status, hard, StringComparison.Ordinal) ||
        string.Equals(status, soft, StringComparison.Ordinal) ||
        string.Equals(status, rejected, StringComparison.Ordinal) ||
        string.Equals(status, complaint, StringComparison.Ordinal);

    private static int Count(IEnumerable<SendEvent> rows, string status) =>
        rows.Count(x => string.Equals(x.DeliveryStatus.ToString(), status, StringComparison.Ordinal));

    private static string RecommendationBlock(IReadOnlyCollection<SendEvent> rows, string hard, string soft, string rejected, string complaint)
    {
        var hardCount = Count(rows, hard);
        var softCount = Count(rows, soft);
        var rejectedCount = Count(rows, rejected);
        var complaintCount = Count(rows, complaint);
        if (hardCount + softCount + rejectedCount + complaintCount == 0)
        {
            return "<div class='section-head'><div><p class='eyebrow'>Что делать</p><h2>Проблем доставки за выбранный период нет</h2><p class='admin-muted'>Дополнительных действий не требуется.</p></div></div>";
        }

        var items = new List<string>();
        if (hardCount > 0)
        {
            items.Add($"<li><b>HardBounce:</b> {hardCount}. Постоянная ошибка доставки. Адреса с таким статусом считаются плохими и должны исключаться из будущих отправок через suppression.</li>");
        }

        if (softCount > 0)
        {
            var overQuota = rows.Any(x => string.Equals(x.DeliveryStatus.ToString(), soft, StringComparison.Ordinal) && ContainsAny(x.LastDeliverySummary, "out of storage", "over quota", "overquota", "mailbox full", "quota"));
            var detail = overQuota
                ? "Есть признаки переполненного ящика. Адрес не блокируем сразу: можно повторить позже, но при повторных SoftBounce держим как рискованный."
                : "Временная ошибка доставки. Адрес не блокируем сразу, но повторные SoftBounce по этому адресу ухудшают качество базы.";
            items.Add($"<li><b>SoftBounce:</b> {softCount}. {detail}</li>");
        }

        if (rejectedCount > 0)
        {
            items.Add($"<li><b>Rejected:</b> {rejectedCount}. Получающая сторона отклонила письмо. Нужно смотреть текст ошибки: возможны блокировка, политика домена или плохое содержимое письма.</li>");
        }

        if (complaintCount > 0)
        {
            items.Add($"<li><b>Complaint:</b> {complaintCount}. Жалоба получателя. Это высокий риск для репутации отправки: клиента и базу нужно проверять вручную.</li>");
        }

        return $"<div class='section-head'><div><p class='eyebrow'>Что делать</p><h2>Рекомендации по доставке</h2><ul class='admin-muted'>{string.Join(string.Empty, items)}</ul></div></div>";
    }

    private static bool ContainsAny(string? value, params string[] patterns)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return patterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static string Table(IReadOnlyCollection<SendEvent> rows, Func<SendEvent, string> rowHtml) =>
        $"<div class='admin-table-wrap'><table class='admin-table'><thead><tr><th>Email</th><th>Доставка</th><th>Дата</th><th>Provider ID</th><th>Сводка</th></tr></thead><tbody>{(rows.Count == 0 ? "<tr><td colspan='5'>Проблемных событий за выбранный период нет.</td></tr>" : string.Join(string.Empty, rows.Select(rowHtml)))}</tbody></table></div>";

    private static string ProblemEventRowHtml(SendEvent row) =>
        $"<tr><td><a class='admin-link' href='/admin/recipients/{Uri.EscapeDataString(row.RecipientEmail)}'>{H(row.RecipientEmail)}</a></td><td><span class='admin-badge'>{H(row.DeliveryStatus.ToString())}</span></td><td>{FormatDate(row.LastDeliveryEventAt ?? row.UpdatedAt)}</td><td>{H(row.ProviderMessageId)}</td><td>{H(Shorten(row.LastDeliverySummary, 240))}</td></tr>";

    private static string Shorten(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "...";
    }

    private static string FormatDate(DateTimeOffset? value) => value is null ? "-" : value.Value.ToString("yyyy-MM-dd HH:mm");

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
