using System.Net;
using System.Text;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Endpoints;

public static class AdminCampaignDeliveryAnalyticsMiddleware
{
    public static IApplicationBuilder UseAdminCampaignDeliveryAnalytics(this IApplicationBuilder app) => app.Use(async (context, next) =>
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
            var enhanced = InjectDeliveryAnalytics(html, sendEvents);
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

    private static string InjectDeliveryAnalytics(string html, IReadOnlyCollection<SendEvent> sendEvents)
    {
        const string marker = "<div class='section-head'><div><p class='eyebrow'>Отправка</p><h2>Лог отправки</h2></div>";
        if (!html.Contains(marker, StringComparison.Ordinal))
        {
            return html;
        }

        return html.Replace(marker, BuildDeliveryAnalytics(sendEvents) + marker, StringComparison.Ordinal);
    }

    private static string BuildDeliveryAnalytics(IReadOnlyCollection<SendEvent> sendEvents)
    {
        var delivered = CountDeliveryStatus(sendEvents, "Delivered");
        var softBounced = CountDeliveryStatus(sendEvents, "SoftBounce");
        var hardBounced = CountDeliveryStatus(sendEvents, "HardBounce");
        var rejected = CountDeliveryStatus(sendEvents, "Rejected");
        var notReported = CountDeliveryStatus(sendEvents, "NotReported");
        var lastDeliveryEventAt = sendEvents
            .Select(sendEvent => sendEvent.LastDeliveryEventAt)
            .Where(value => value is not null)
            .OrderByDescending(value => value)
            .FirstOrDefault();
        var rows = sendEvents
            .Where(sendEvent => !string.Equals(sendEvent.DeliveryStatus.ToString(), "NotReported", StringComparison.Ordinal) || sendEvent.LastDeliveryEventAt is not null)
            .OrderByDescending(sendEvent => sendEvent.LastDeliveryEventAt ?? sendEvent.UpdatedAt)
            .ThenBy(sendEvent => sendEvent.RecipientEmail)
            .Take(20)
            .Select(sendEvent => $"<tr><td>{H(sendEvent.RecipientEmail)}</td><td>{H(sendEvent.DeliveryStatus.ToRu())}</td><td>{FormatDate(sendEvent.LastDeliveryEventAt)}</td><td>{H(ShortText(sendEvent.LastDeliverySummary))}</td></tr>");
        var tableRows = rows.Any()
            ? string.Join(string.Empty, rows)
            : "<tr><td colspan='4'>Реальные события доставки пока не зафиксированы.</td></tr>";

        return $"""
            <div class='section-head'><div><p class='eyebrow'>Аналитика</p><h2>Доставка</h2></div></div>
            <div class='admin-stats'>
                <div class='admin-stat'><b>{delivered}</b><span>Доставлено</span></div>
                <div class='admin-stat'><b>{softBounced}</b><span>Временная ошибка</span></div>
                <div class='admin-stat'><b>{hardBounced}</b><span>Постоянная ошибка</span></div>
                <div class='admin-stat'><b>{rejected}</b><span>Отклонено</span></div>
                <div class='admin-stat'><b>{notReported}</b><span>Не сообщено</span></div>
                <div class='admin-stat'><b>{FormatDate(lastDeliveryEventAt)}</b><span>Последнее событие</span></div>
            </div>
            <div class='admin-table-wrap'><table class='admin-table'><thead><tr><th>Email</th><th>Доставка</th><th>Последнее событие</th><th>Причина</th></tr></thead><tbody>{tableRows}</tbody></table></div>
            """;
    }

    private static int CountDeliveryStatus(IEnumerable<SendEvent> sendEvents, string status) => sendEvents.Count(sendEvent => string.Equals(sendEvent.DeliveryStatus.ToString(), status, StringComparison.Ordinal));

    private static string ShortText(string? text) => string.IsNullOrWhiteSpace(text)
        ? ""
        : text.Length <= 160 ? text : text[..157] + "...";

    private static string FormatDate(DateTimeOffset? value) => value is null ? "-" : value.Value.ToString("yyyy-MM-dd HH:mm");

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
