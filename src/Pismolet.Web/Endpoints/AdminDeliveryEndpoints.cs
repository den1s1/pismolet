using System.Net;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Database;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class AdminDeliveryEndpoints
{
    private const int DefaultDays = 14;
    private const int MaxDays = 90;
    private const int RecentLimit = 50;

    public static IEndpointRouteBuilder MapAdminDeliveryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/delivery", ShowDeliveryOverview).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        return app;
    }

    private static IResult ShowDeliveryOverview(HttpContext http, PismoletDbContext db)
    {
        var adminEmail = CurrentEmail(http) ?? "admin@example.test";
        var days = ReadDays(http);
        var since = DateTimeOffset.UtcNow.AddDays(-days);
        var hardBounce = DeliveryStatus.HardBounce.ToString();
        var softBounce = DeliveryStatus.SoftBounce.ToString();
        var delivered = DeliveryStatus.Delivered.ToString();
        var accepted = DeliveryStatus.Accepted.ToString();
        var rejected = DeliveryStatus.Rejected.ToString();
        var complaint = DeliveryStatus.Complaint.ToString();

        var sendEvents = db.SendEvents.AsNoTracking().Where(x => x.UpdatedAt >= since);
        var totalEvents = sendEvents.Count();
        var acceptedCount = sendEvents.Count(x => x.DeliveryStatus == accepted);
        var deliveredCount = sendEvents.Count(x => x.DeliveryStatus == delivered);
        var softBounceCount = sendEvents.Count(x => x.DeliveryStatus == softBounce);
        var hardBounceCount = sendEvents.Count(x => x.DeliveryStatus == hardBounce);
        var rejectedCount = sendEvents.Count(x => x.DeliveryStatus == rejected);
        var complaintCount = sendEvents.Count(x => x.DeliveryStatus == complaint);

        var clientSuppressions = db.ClientSuppressions.AsNoTracking().Where(x => x.LastSeenAt >= since);
        var clientSuppressionCount = clientSuppressions.Count();
        var totalClientSuppressionCount = db.ClientSuppressions.AsNoTracking().Count();

        var topClientRows = sendEvents
            .Where(x => x.DeliveryStatus == hardBounce || x.DeliveryStatus == softBounce || x.DeliveryStatus == rejected || x.DeliveryStatus == complaint)
            .GroupBy(x => x.OwnerEmail)
            .Select(group => new DeliveryClientRow(
                group.Key,
                group.Count(),
                group.Count(x => x.DeliveryStatus == hardBounce),
                group.Count(x => x.DeliveryStatus == softBounce),
                group.Count(x => x.DeliveryStatus == rejected),
                group.Count(x => x.DeliveryStatus == complaint),
                group.Max(x => x.LastDeliveryEventAt ?? x.UpdatedAt)))
            .OrderByDescending(row => row.TotalProblems)
            .ThenBy(row => row.OwnerEmail)
            .Take(20)
            .ToArray();

        var topSuppressionRows = clientSuppressions
            .GroupBy(x => x.ClientId)
            .Select(group => new SuppressionClientRow(
                group.Key,
                group.Count(),
                group.Max(x => x.LastSeenAt)))
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.ClientId)
            .Take(20)
            .ToArray();

        var recentDeliveryRows = sendEvents
            .Where(x => x.DeliveryStatus == hardBounce || x.DeliveryStatus == softBounce || x.DeliveryStatus == rejected || x.DeliveryStatus == complaint)
            .OrderByDescending(x => x.LastDeliveryEventAt ?? x.UpdatedAt)
            .ThenBy(x => x.RecipientEmail)
            .Take(RecentLimit)
            .Select(x => new RecentDeliveryRow(
                x.OwnerEmail,
                x.RecipientEmail,
                x.DeliveryStatus,
                x.LastDeliveryEventAt ?? x.UpdatedAt,
                x.ProviderMessageId,
                x.LastDeliverySummary))
            .ToArray();

        var recentSuppressionRows = db.ClientSuppressions
            .AsNoTracking()
            .OrderByDescending(x => x.LastSeenAt)
            .ThenBy(x => x.EmailNormalized)
            .Take(RecentLimit)
            .Select(x => new RecentSuppressionRow(
                x.ClientId,
                x.EmailNormalized,
                x.Reason,
                x.SourceMailingId,
                x.SourceProviderMessageId,
                x.LastSeenAt))
            .ToArray();

        var stats = $"""
            <div class='admin-stats'>
                <div class='admin-stat'><b>{totalEvents}</b><span>Событий за {days} дн.</span></div>
                <div class='admin-stat'><b>{deliveredCount}</b><span>Delivered</span></div>
                <div class='admin-stat'><b>{hardBounceCount}</b><span>HardBounce</span></div>
                <div class='admin-stat'><b>{softBounceCount}</b><span>SoftBounce</span></div>
                <div class='admin-stat'><b>{rejectedCount}</b><span>Rejected</span></div>
                <div class='admin-stat'><b>{clientSuppressionCount}</b><span>Client suppression за период</span></div>
            </div>
            """;

        var clientRowsHtml = topClientRows.Length == 0
            ? "<tr><td colspan='7'>Проблем доставки за выбранный период нет.</td></tr>"
            : string.Join(string.Empty, topClientRows.Select(ClientRow));
        var suppressionClientRowsHtml = topSuppressionRows.Length == 0
            ? "<tr><td colspan='3'>Новых client suppression за выбранный период нет.</td></tr>"
            : string.Join(string.Empty, topSuppressionRows.Select(SuppressionClientRowHtml));
        var recentDeliveryRowsHtml = recentDeliveryRows.Length == 0
            ? "<tr><td colspan='6'>Проблем доставки за выбранный период нет.</td></tr>"
            : string.Join(string.Empty, recentDeliveryRows.Select(RecentDeliveryRowHtml));
        var recentSuppressionRowsHtml = recentSuppressionRows.Length == 0
            ? "<tr><td colspan='6'>Client suppression пока нет.</td></tr>"
            : string.Join(string.Empty, recentSuppressionRows.Select(RecentSuppressionRowHtml));

        var body = $"""
            <section class='admin-panel'>
                <div class='admin-title-row'>
                    <div>
                        <p class='eyebrow'>Администрирование</p>
                        <h1>Доставка и suppression</h1>
                        <p class='admin-muted'>Обзор bounce-статусов, проблемных клиентов и client suppression. Данные нужны для поддержки, deliverability и будущего SoftBounce v2.</p>
                    </div>
                    <a class='admin-export' href='/admin/campaigns'>К кампаниям</a>
                </div>
                <form class='admin-filters' method='get' action='/admin/delivery'>
                    <label>Период, дней<input type='number' min='1' max='{MaxDays}' name='days' value='{days}'></label>
                    <button class='admin-button' type='submit'>Обновить</button>
                    <a class='admin-link' href='/admin/delivery'>Сбросить</a>
                </form>
                {stats}
                <p class='admin-muted'>Accepted за период: {acceptedCount}. Complaint за период: {complaintCount}. Всего client suppression в базе: {totalClientSuppressionCount}.</p>

                <div class='section-head'><div><p class='eyebrow'>Клиенты</p><h2>Проблемы доставки за период</h2></div></div>
                <div class='admin-table-wrap'>
                    <table class='admin-table'>
                        <thead><tr><th>Клиент</th><th>Всего проблем</th><th>Hard</th><th>Soft</th><th>Rejected</th><th>Complaint</th><th>Последнее событие</th></tr></thead>
                        <tbody>{clientRowsHtml}</tbody>
                    </table>
                </div>

                <div class='section-head'><div><p class='eyebrow'>Клиенты</p><h2>Client suppression за период</h2></div></div>
                <div class='admin-table-wrap'>
                    <table class='admin-table'>
                        <thead><tr><th>Клиент</th><th>Адресов в suppression</th><th>Последнее обновление</th></tr></thead>
                        <tbody>{suppressionClientRowsHtml}</tbody>
                    </table>
                </div>

                <div class='section-head'><div><p class='eyebrow'>Последние события</p><h2>Bounce / rejected / complaint</h2></div></div>
                <div class='admin-table-wrap'>
                    <table class='admin-table'>
                        <thead><tr><th>Клиент</th><th>Email</th><th>Доставка</th><th>Дата</th><th>Provider ID</th><th>Сводка</th></tr></thead>
                        <tbody>{recentDeliveryRowsHtml}</tbody>
                    </table>
                </div>

                <div class='section-head'><div><p class='eyebrow'>Последние события</p><h2>Client suppression</h2></div></div>
                <div class='admin-table-wrap'>
                    <table class='admin-table'>
                        <thead><tr><th>Клиент</th><th>Email</th><th>Причина</th><th>Рассылка</th><th>Provider ID</th><th>Дата</th></tr></thead>
                        <tbody>{recentSuppressionRowsHtml}</tbody>
                    </table>
                </div>
                <p><a class='admin-link' href='/admin'>Вернуться в админку</a></p>
            </section>
            """;

        return HtmlRenderer.Html(HtmlRenderer.Page("Админка - доставка", body, authenticated: true));
    }

    private static int ReadDays(HttpContext http)
    {
        var raw = http.Request.Query["days"].ToString();
        return int.TryParse(raw, out var days) ? Math.Clamp(days, 1, MaxDays) : DefaultDays;
    }

    private static string ClientRow(DeliveryClientRow row) => $"<tr><td><a class='admin-link' href='/admin/users/{Uri.EscapeDataString(row.OwnerEmail)}'>{H(row.OwnerEmail)}</a></td><td>{row.TotalProblems}</td><td>{row.HardBounce}</td><td>{row.SoftBounce}</td><td>{row.Rejected}</td><td>{row.Complaint}</td><td>{FormatDate(row.LastEventAt)}</td></tr>";

    private static string SuppressionClientRowHtml(SuppressionClientRow row) => $"<tr><td><a class='admin-link' href='/admin/users/{Uri.EscapeDataString(row.ClientId)}'>{H(row.ClientId)}</a></td><td>{row.Count}</td><td>{FormatDate(row.LastSeenAt)}</td></tr>";

    private static string RecentDeliveryRowHtml(RecentDeliveryRow row) => $"<tr><td>{H(row.OwnerEmail)}</td><td><a class='admin-link' href='/admin/recipients/{Uri.EscapeDataString(row.Email)}'>{H(row.Email)}</a></td><td><span class='admin-badge'>{H(DeliveryStatusText(row.DeliveryStatus))}</span></td><td>{FormatDate(row.EventAt)}</td><td>{H(row.ProviderMessageId)}</td><td>{H(Shorten(row.Summary, 180))}</td></tr>";

    private static string RecentSuppressionRowHtml(RecentSuppressionRow row) => $"<tr><td>{H(row.ClientId)}</td><td><a class='admin-link' href='/admin/recipients/{Uri.EscapeDataString(row.Email)}'>{H(row.Email)}</a></td><td>{H(row.Reason)}</td><td>{(row.SourceMailingId is null ? "-" : $"<a class='admin-link' href='/admin/campaigns/{row.SourceMailingId}'>Открыть</a>")}</td><td>{H(row.ProviderMessageId)}</td><td>{FormatDate(row.LastSeenAt)}</td></tr>";

    private static string DeliveryStatusText(string value) => value switch
    {
        nameof(DeliveryStatus.HardBounce) => "HardBounce",
        nameof(DeliveryStatus.SoftBounce) => "SoftBounce",
        nameof(DeliveryStatus.Rejected) => "Rejected",
        nameof(DeliveryStatus.Complaint) => "Complaint",
        nameof(DeliveryStatus.Delivered) => "Delivered",
        nameof(DeliveryStatus.Accepted) => "Accepted",
        _ => value
    };

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

    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private sealed record DeliveryClientRow(string OwnerEmail, int TotalProblems, int HardBounce, int SoftBounce, int Rejected, int Complaint, DateTimeOffset LastEventAt);

    private sealed record SuppressionClientRow(string ClientId, int Count, DateTimeOffset LastSeenAt);

    private sealed record RecentDeliveryRow(string OwnerEmail, string Email, string DeliveryStatus, DateTimeOffset EventAt, string? ProviderMessageId, string? Summary);

    private sealed record RecentSuppressionRow(string ClientId, string Email, string Reason, Guid? SourceMailingId, string? ProviderMessageId, DateTimeOffset LastSeenAt);
}
