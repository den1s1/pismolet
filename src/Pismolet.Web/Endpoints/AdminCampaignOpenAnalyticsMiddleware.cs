using System.Net;
using System.Text;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Endpoints;

public static class AdminCampaignOpenAnalyticsMiddleware
{
    public static IApplicationBuilder UseAdminCampaignOpenAnalytics(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (!TryGetCampaignId(context, out var campaignId))
        {
            await next();
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next();

            buffer.Position = 0;
            using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var html = await reader.ReadToEndAsync();

            context.Response.Body = originalBody;

            if (context.Response.StatusCode != StatusCodes.Status200OK ||
                !IsHtml(context.Response.ContentType) ||
                string.IsNullOrWhiteSpace(html))
            {
                await context.Response.WriteAsync(html);
                return;
            }

            var sendEvents = context.RequestServices
                .GetRequiredService<ISendEventRepository>()
                .ListByMailingId(campaignId)
                .ToArray();
            var enhanced = InjectOpenAnalytics(html, sendEvents);
            context.Response.ContentLength = Encoding.UTF8.GetByteCount(enhanced);
            await context.Response.WriteAsync(enhanced);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    });

    private static bool TryGetCampaignId(HttpContext context, out Guid campaignId)
    {
        campaignId = Guid.Empty;
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            return false;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 3 &&
               string.Equals(parts[0], "admin", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(parts[1], "campaigns", StringComparison.OrdinalIgnoreCase) &&
               Guid.TryParse(parts[2], out campaignId);
    }

    private static bool IsHtml(string? contentType) =>
        contentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true;

    private static string InjectOpenAnalytics(string html, IReadOnlyCollection<SendEvent> sendEvents)
    {
        const string marker = "<div class='section-head'><div><p class='eyebrow'>Отправка</p><h2>Лог отправки</h2></div>";
        if (!html.Contains(marker, StringComparison.Ordinal))
        {
            return html;
        }

        return html.Replace(marker, BuildOpenAnalytics(sendEvents) + marker, StringComparison.Ordinal);
    }

    private static string BuildOpenAnalytics(IReadOnlyCollection<SendEvent> sendEvents)
    {
        var openedRecipients = sendEvents.Count(sendEvent => sendEvent.FirstOpenedAt is not null);
        var totalOpens = sendEvents.Sum(sendEvent => sendEvent.OpenCount);
        var lastOpenedAt = sendEvents
            .Select(sendEvent => sendEvent.LastOpenedAt)
            .Where(value => value is not null)
            .OrderByDescending(value => value)
            .FirstOrDefault();
        var rows = sendEvents
            .Where(sendEvent => !string.IsNullOrWhiteSpace(sendEvent.TrackingToken) || sendEvent.FirstOpenedAt is not null || sendEvent.OpenCount > 0)
            .OrderByDescending(sendEvent => sendEvent.LastOpenedAt ?? sendEvent.UpdatedAt)
            .Take(12)
            .Select(sendEvent => $"<tr><td>{H(sendEvent.RecipientEmail)}</td><td>{(sendEvent.FirstOpenedAt is null ? "Нет" : "Да")}</td><td>{sendEvent.OpenCount}</td><td>{FormatDate(sendEvent.FirstOpenedAt)}</td><td>{FormatDate(sendEvent.LastOpenedAt)}</td></tr>");
        var tableRows = rows.Any()
            ? string.Join(string.Empty, rows)
            : "<tr><td colspan='5'>Открытия пока не зафиксированы.</td></tr>";

        return $"""
            <div class='section-head'><div><p class='eyebrow'>Аналитика</p><h2>Открытия</h2></div></div>
            <div class='admin-stats'>
                <div class='admin-stat'><b>{openedRecipients}</b><span>Открыто, получателей</span></div>
                <div class='admin-stat'><b>{totalOpens}</b><span>Открытий всего</span></div>
                <div class='admin-stat'><b>{FormatDate(lastOpenedAt)}</b><span>Последнее открытие</span></div>
            </div>
            <div class='admin-table-wrap'><table class='admin-table'><thead><tr><th>Email</th><th>Открыто</th><th>Открытий</th><th>Первое открытие</th><th>Последнее открытие</th></tr></thead><tbody>{tableRows}</tbody></table></div>
            """;
    }

    private static string FormatDate(DateTimeOffset? value) => value is null ? "-" : value.Value.ToString("yyyy-MM-dd HH:mm");

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
