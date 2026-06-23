using System.Net;
using Microsoft.AspNetCore.Authorization;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Database;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class AdminDeliveryClientDrilldownMiddleware
{
    private const int DefaultDays = 14;
    private const int MaxDays = 90;
    private const int RecentLimit = 50;

    public static IApplicationBuilder UseAdminDeliveryClientDrilldown(this IApplicationBuilder app) => app.Use(async (http, next) =>
    {
        var path = http.Request.Path.Value ?? string.Empty;
        const string prefix = "/admin/delivery/client/";
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

        var ownerEmail = Uri.UnescapeDataString(path[prefix.Length..]).Trim('/');
        if (string.IsNullOrWhiteSpace(ownerEmail))
        {
            await next();
            return;
        }

        var db = http.RequestServices.GetRequiredService<PismoletDbContext>();
        var mailings = http.RequestServices.GetRequiredService<IMailingRepository>();
        var days = ReadDays(http);
        var since = DateTimeOffset.UtcNow.AddDays(-days);
        var hard = nameof(DeliveryStatus.HardBounce);
        var soft = nameof(DeliveryStatus.SoftBounce);
        var rejected = nameof(DeliveryStatus.Rejected);
        var complaint = nameof(DeliveryStatus.Complaint);
        var problemRows = db.SendEvents
            .Where(x => x.OwnerEmail == ownerEmail && x.UpdatedAt >= since)
            .Where(x => x.DeliveryStatus == hard || x.DeliveryStatus == soft || x.DeliveryStatus == rejected || x.DeliveryStatus == complaint)
            .Select(x => new ClientDeliveryEventRow(
                x.MailingId,
                x.RecipientEmail,
                x.DeliveryStatus,
                x.LastDeliveryEventAt ?? x.UpdatedAt,
                x.ProviderMessageId,
                x.LastDeliverySummary))
            .ToArray();
        var mailingIds = problemRows.Select(x => x.MailingId).Distinct().ToHashSet();
        var mailingLookup = mailings.ListForOwner(ownerEmail)
            .Where(x => mailingIds.Contains(x.Id))
            .ToDictionary(x => x.Id, x => new MailingDisplayRow(
                string.IsNullOrWhiteSpace(x.Subject) ? "Рассылка без темы" : x.Subject,
                x.CreatedAt.ToString("yyyy-MM-dd HH:mm")));
        var mailingRows = problemRows
            .GroupBy(x => x.MailingId)
            .Select(g => new ClientMailingProblemRow(g.Key, g.Count(), g.Count(x => x.DeliveryStatus == hard), g.Count(x => x.DeliveryStatus == soft), g.Count(x => x.DeliveryStatus == rejected), g.Count(x => x.DeliveryStatus == complaint), g.Max(x => x.EventAt)))
            .OrderByDescending(x => x.TotalProblems)
            .ThenByDescending(x => x.LastEventAt)
            .Take(50)
            .ToArray();
        var recentRows = problemRows
            .OrderByDescending(x => x.EventAt)
            .ThenBy(x => x.Email)
            .Take(RecentLimit)
            .ToArray();
        var backUrl = $"/admin/delivery?days={days}";

        var body = $"""
            <section class='admin-panel'>
                <div class='admin-title-row'>
                    <div>
                        <p class='eyebrow'>Доставка клиента</p>
                        <h1>{H(ownerEmail)}</h1>
                        <p class='admin-muted'>Drill-down по проблемным событиям доставки клиента за выбранный период.</p>
                    </div>
                    <a class='admin-export' href='{backUrl}'>К обзору доставки</a>
                </div>
                <form class='admin-filters' method='get' action='/admin/delivery/client/{Uri.EscapeDataString(ownerEmail)}'>
                    <label>Период, дней<input type='number' min='1' max='{MaxDays}' name='days' value='{days}'></label>
                    <button class='admin-button' type='submit'>Обновить</button>
                    <a class='admin-link' href='/admin/delivery/client/{Uri.EscapeDataString(ownerEmail)}'>Сбросить</a>
                </form>
                <div class='admin-stats'>
                    <div class='admin-stat'><b>{problemRows.Length}</b><span>Проблем за {days} дн.</span></div>
                    <div class='admin-stat'><b>{problemRows.Count(x => x.DeliveryStatus == hard)}</b><span>HardBounce</span></div>
                    <div class='admin-stat'><b>{problemRows.Count(x => x.DeliveryStatus == soft)}</b><span>SoftBounce</span></div>
                    <div class='admin-stat'><b>{problemRows.Count(x => x.DeliveryStatus == rejected)}</b><span>Rejected</span></div>
                    <div class='admin-stat'><b>{problemRows.Count(x => x.DeliveryStatus == complaint)}</b><span>Complaint</span></div>
                </div>
                <div class='section-head'><div><p class='eyebrow'>Рассылки</p><h2>Проблемные рассылки клиента</h2></div></div>
                {Table("Рассылка|Всего проблем|Hard|Soft|Rejected|Complaint|Последнее событие", mailingRows, x => ClientMailingProblemRowHtml(x, mailingLookup), 7, "Проблемных рассылок за выбранный период нет.")}
                <div class='section-head'><div><p class='eyebrow'>События</p><h2>Последние проблемные события</h2></div></div>
                {Table("Рассылка|Email|Доставка|Дата|Provider ID|Сводка", recentRows, x => ClientDeliveryEventRowHtml(x, mailingLookup), 6, "Проблемных событий за выбранный период нет.")}
                <p><a class='admin-link' href='{backUrl}'>Вернуться к обзору доставки</a></p>
            </section>
            """;

        http.Response.ContentType = "text/html; charset=utf-8";
        await http.Response.WriteAsync(HtmlRenderer.Page("Админка - доставка клиента", body, authenticated: true));
    });

    private static int ReadDays(HttpContext http)
    {
        var raw = http.Request.Query["days"].ToString();
        return int.TryParse(raw, out var days) ? Math.Clamp(days, 1, MaxDays) : DefaultDays;
    }

    private static string Table<T>(string headers, IReadOnlyCollection<T> rows, Func<T, string> rowHtml, int columns, string emptyText) => $"<div class='admin-table-wrap'><table class='admin-table'><thead><tr>{string.Join(string.Empty, headers.Split('|').Select(x => $"<th>{H(x)}</th>"))}</tr></thead><tbody>{(rows.Count == 0 ? $"<tr><td colspan='{columns}'>{H(emptyText)}</td></tr>" : string.Join(string.Empty, rows.Select(rowHtml)))}</tbody></table></div>";

    private static string ClientMailingProblemRowHtml(ClientMailingProblemRow row, IReadOnlyDictionary<Guid, MailingDisplayRow> mailings) => $"<tr><td>{MailingCell(row.MailingId, mailings)}</td><td>{row.TotalProblems}</td><td>{row.HardBounce}</td><td>{row.SoftBounce}</td><td>{row.Rejected}</td><td>{row.Complaint}</td><td>{FormatDate(row.LastEventAt)}</td></tr>";

    private static string ClientDeliveryEventRowHtml(ClientDeliveryEventRow row, IReadOnlyDictionary<Guid, MailingDisplayRow> mailings) => $"<tr><td>{MailingCell(row.MailingId, mailings)}</td><td><a class='admin-link' href='/admin/recipients/{Uri.EscapeDataString(row.Email)}'>{H(row.Email)}</a></td><td><span class='admin-badge'>{H(row.DeliveryStatus)}</span></td><td>{FormatDate(row.EventAt)}</td><td>{H(row.ProviderMessageId)}</td><td>{H(Shorten(row.Summary, 180))}</td></tr>";

    private static string MailingCell(Guid mailingId, IReadOnlyDictionary<Guid, MailingDisplayRow> mailings)
    {
        if (!mailings.TryGetValue(mailingId, out var mailing))
        {
            return $"<a class='admin-link' href='/admin/campaigns/{mailingId}'>Открыть</a><br><span class='admin-muted'>{mailingId}</span>";
        }

        return $"<a class='admin-link' href='/admin/campaigns/{mailingId}'>{H(mailing.Subject)}</a><br><span class='admin-muted'>{H(mailing.CreatedAtText)} · {mailingId}</span>";
    }

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

    private sealed record ClientMailingProblemRow(Guid MailingId, int TotalProblems, int HardBounce, int SoftBounce, int Rejected, int Complaint, DateTimeOffset LastEventAt);

    private sealed record ClientDeliveryEventRow(Guid MailingId, string Email, string DeliveryStatus, DateTimeOffset EventAt, string? ProviderMessageId, string? Summary);

    private sealed record MailingDisplayRow(string Subject, string CreatedAtText);
}
