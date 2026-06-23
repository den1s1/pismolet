using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Infrastructure.Database;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class AdminSuppressionsMiddleware
{
    private const int DefaultDays = 30;
    private const int MaxDays = 365;
    private const int RowLimit = 200;

    public static IApplicationBuilder UseAdminSuppressions(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        var path = http.Request.Path.Value ?? string.Empty;
        if (!string.Equals(path.TrimEnd('/'), "/admin/suppressions", StringComparison.OrdinalIgnoreCase))
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

        var db = http.RequestServices.GetRequiredService<PismoletDbContext>();
        var days = ReadDays(http);
        var client = http.Request.Query["client"].ToString().Trim();
        var since = DateTimeOffset.UtcNow.AddDays(-days);

        var query = db.ClientSuppressions
            .AsNoTracking()
            .Where(x => x.CreatedAt >= since);
        if (!string.IsNullOrWhiteSpace(client))
        {
            query = query.Where(x => x.ClientId == client);
        }

        var rows = query
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.ClientId)
            .ThenBy(x => x.EmailNormalized)
            .Take(RowLimit)
            .Select(x => new SuppressionRow(
                x.ClientId,
                x.EmailNormalized,
                x.Reason,
                x.SourceMailingId,
                x.SourceProviderMessageId,
                x.CreatedAt))
            .ToArray();

        var totalForPeriod = query.Count();
        var totalInDatabase = db.ClientSuppressions.AsNoTracking().Count();
        var topClients = rows
            .GroupBy(x => x.ClientId)
            .Select(group => new TopClientRow(group.Key, group.Count(), group.Max(x => x.CreatedAt)))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.ClientId)
            .Take(20)
            .ToArray();

        var clientValue = H(client);
        var rowsHtml = rows.Length == 0
            ? "<tr><td colspan='7'>Suppression за выбранный период не найден.</td></tr>"
            : string.Join(string.Empty, rows.Select(RowHtml));
        var topClientsHtml = topClients.Length == 0
            ? "<tr><td colspan='3'>Клиентов с suppression за выбранный период нет.</td></tr>"
            : string.Join(string.Empty, topClients.Select(row => TopClientRowHtml(row, days)));

        var body = $"""
            <section class='admin-panel'>
                <div class='admin-title-row'>
                    <div>
                        <p class='eyebrow'>Администрирование</p>
                        <h1>Client suppression</h1>
                        <p class='admin-muted'>Адреса, которые сервис исключает из будущих отправок клиента: hard bounce, complaint, manual/unsubscribe и другие причины.</p>
                    </div>
                    <a class='admin-export' href='/admin/delivery'>К доставке</a>
                </div>
                <form class='admin-filters' method='get' action='/admin/suppressions'>
                    <label>Период, дней<input type='number' min='1' max='{MaxDays}' name='days' value='{days}'></label>
                    <label>Клиент<input type='email' name='client' value='{clientValue}' placeholder='client@example.ru'></label>
                    <button class='admin-button' type='submit'>Обновить</button>
                    <a class='admin-link' href='/admin/suppressions'>Сбросить</a>
                </form>
                <div class='admin-stats'>
                    <div class='admin-stat'><b>{totalForPeriod}</b><span>suppression за {days} дн.</span></div>
                    <div class='admin-stat'><b>{totalInDatabase}</b><span>всего в базе</span></div>
                    <div class='admin-stat'><b>{rows.Length}</b><span>показано строк</span></div>
                    <div class='admin-stat'><b>{topClients.Length}</b><span>клиентов в выборке</span></div>
                </div>
                {RecommendationBlock(rows)}
                <div class='section-head'><div><p class='eyebrow'>Клиенты</p><h2>Клиенты с suppression за период</h2></div></div>
                <div class='admin-table-wrap'>
                    <table class='admin-table'>
                        <thead><tr><th>Клиент</th><th>Адресов</th><th>Последнее добавление</th></tr></thead>
                        <tbody>{topClientsHtml}</tbody>
                    </table>
                </div>
                <div class='section-head'><div><p class='eyebrow'>Адреса</p><h2>Последние suppression</h2></div></div>
                <div class='admin-table-wrap'>
                    <table class='admin-table'>
                        <thead><tr><th>Клиент</th><th>Email</th><th>Причина</th><th>Рассылка</th><th>Provider ID</th><th>Дата</th><th>Что значит</th></tr></thead>
                        <tbody>{rowsHtml}</tbody>
                    </table>
                </div>
                <p><a class='admin-link' href='/admin/delivery'>Вернуться к доставке</a></p>
            </section>
            """;

        http.Response.ContentType = "text/html; charset=utf-8";
        await http.Response.WriteAsync(HtmlRenderer.Page("Админка - client suppression", body, authenticated: true));
    });

    private static int ReadDays(HttpContext http)
    {
        var raw = http.Request.Query["days"].ToString();
        return int.TryParse(raw, out var days) ? Math.Clamp(days, 1, MaxDays) : DefaultDays;
    }

    private static string TopClientRowHtml(TopClientRow row, int days) =>
        $"<tr><td><a class='admin-link' href='/admin/suppressions?days={days}&client={Uri.EscapeDataString(row.ClientId)}'>{H(row.ClientId)}</a></td><td>{row.Count}</td><td>{FormatDate(row.LastCreatedAt)}</td></tr>";

    private static string RowHtml(SuppressionRow row)
    {
        var mailing = row.SourceMailingId is null
            ? "-"
            : $"<a class='admin-link' href='/admin/delivery/mailing/{row.SourceMailingId}'>Delivery</a><br><span class='admin-muted'>{row.SourceMailingId}</span>";
        var client = $"<a class='admin-link' href='/admin/delivery/client/{Uri.EscapeDataString(row.ClientId)}'>{H(row.ClientId)}</a>";
        var email = $"<a class='admin-link' href='/admin/recipients/{Uri.EscapeDataString(row.Email)}'>{H(row.Email)}</a>";
        return $"<tr><td>{client}</td><td>{email}</td><td><span class='admin-badge'>{H(row.Reason)}</span></td><td>{mailing}</td><td>{H(row.ProviderMessageId)}</td><td>{FormatDate(row.CreatedAt)}</td><td>{H(ReasonHint(row.Reason))}</td></tr>";
    }

    private static string RecommendationBlock(IReadOnlyCollection<SuppressionRow> rows)
    {
        if (rows.Count == 0)
        {
            return "<div class='section-head'><div><p class='eyebrow'>Что делать</p><h2>Новых исключённых адресов нет</h2><p class='admin-muted'>Дополнительных действий не требуется.</p></div></div>";
        }

        var hardBounce = rows.Count(x => x.Reason.Contains("HardBounce", StringComparison.OrdinalIgnoreCase));
        var complaint = rows.Count(x => x.Reason.Contains("Complaint", StringComparison.OrdinalIgnoreCase));
        var manual = rows.Count(x => x.Reason.Contains("Manual", StringComparison.OrdinalIgnoreCase));
        var unsubscribe = rows.Count(x => x.Reason.Contains("Unsubscribe", StringComparison.OrdinalIgnoreCase));
        var items = new List<string>();
        if (hardBounce > 0)
        {
            items.Add($"<li><b>HardBounce:</b> {hardBounce}. Адреса считаются плохими, клиенту можно объяснять: сервер получателя сообщил постоянную ошибку доставки.</li>");
        }

        if (complaint > 0)
        {
            items.Add($"<li><b>Complaint:</b> {complaint}. Это высокий риск для репутации отправки. Нужна ручная проверка клиента, базы и согласий.</li>");
        }

        if (unsubscribe > 0)
        {
            items.Add($"<li><b>Unsubscribe:</b> {unsubscribe}. Адрес отказался от рассылок; возвращать его нельзя без нового основания/согласия.</li>");
        }

        if (manual > 0)
        {
            items.Add($"<li><b>Manual:</b> {manual}. Адрес исключён вручную оператором или внутренним правилом.</li>");
        }

        if (items.Count == 0)
        {
            items.Add("<li>Проверьте причину в таблице и исходную рассылку. Suppression означает, что сервис не должен отправлять на этот адрес будущие письма клиента.</li>");
        }

        return $"<div class='section-head'><div><p class='eyebrow'>Что делать</p><h2>Рекомендации по suppression</h2><ul class='admin-muted'>{string.Join(string.Empty, items)}</ul></div></div>";
    }

    private static string ReasonHint(string reason)
    {
        if (reason.Contains("HardBounce", StringComparison.OrdinalIgnoreCase))
        {
            return "Постоянная ошибка доставки. Не отправлять повторно.";
        }

        if (reason.Contains("Complaint", StringComparison.OrdinalIgnoreCase))
        {
            return "Жалоба получателя. Высокий reputational risk.";
        }

        if (reason.Contains("Unsubscribe", StringComparison.OrdinalIgnoreCase))
        {
            return "Получатель отказался от рассылок.";
        }

        if (reason.Contains("Manual", StringComparison.OrdinalIgnoreCase))
        {
            return "Ручное или внутреннее исключение.";
        }

        return "Адрес исключён из будущих отправок клиента.";
    }

    private static string FormatDate(DateTimeOffset value) => value.ToString("yyyy-MM-dd HH:mm");

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private sealed record TopClientRow(string ClientId, int Count, DateTimeOffset LastCreatedAt);

    private sealed record SuppressionRow(string ClientId, string Email, string Reason, Guid? SourceMailingId, string? ProviderMessageId, DateTimeOffset CreatedAt);
}
