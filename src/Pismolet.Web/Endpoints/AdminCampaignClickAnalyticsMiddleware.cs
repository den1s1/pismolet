using System.Net;
using System.Text;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Endpoints;

public static class AdminCampaignClickAnalyticsMiddleware
{
    public static IApplicationBuilder UseAdminCampaignClickAnalytics(this IApplicationBuilder app) => app.Use(async (context, next) =>
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

            var trackedLinks = context.RequestServices
                .GetRequiredService<IClickTrackingRepository>()
                .ListLinksByMailingId(campaignId)
                .ToArray();
            var enhanced = InjectClickAnalytics(html, trackedLinks);
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

    private static string InjectClickAnalytics(string html, IReadOnlyCollection<TrackedLink> trackedLinks)
    {
        const string marker = "<div class='section-head'><div><p class='eyebrow'>Отправка</p><h2>Лог отправки</h2></div>";
        if (!html.Contains(marker, StringComparison.Ordinal))
        {
            return html;
        }

        return html.Replace(marker, BuildClickAnalytics(trackedLinks) + marker, StringComparison.Ordinal);
    }

    private static string BuildClickAnalytics(IReadOnlyCollection<TrackedLink> trackedLinks)
    {
        var clickedRecipients = trackedLinks
            .Where(link => link.FirstClickedAt is not null)
            .Select(link => link.RecipientEmail)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var totalClicks = trackedLinks.Sum(link => link.ClickCount);
        var lastClickedAt = trackedLinks
            .Select(link => link.LastClickedAt)
            .Where(value => value is not null)
            .OrderByDescending(value => value)
            .FirstOrDefault();
        var rows = trackedLinks
            .Where(link => link.ClickCount > 0 || link.FirstClickedAt is not null)
            .OrderByDescending(link => link.LastClickedAt ?? link.CreatedAt)
            .Take(12)
            .Select(link => $"<tr><td>{H(link.RecipientEmail)}</td><td>{H(ShortUrl(link.OriginalUrl))}</td><td>{link.ClickCount}</td><td>{FormatDate(link.FirstClickedAt)}</td><td>{FormatDate(link.LastClickedAt)}</td></tr>");
        var tableRows = rows.Any()
            ? string.Join(string.Empty, rows)
            : "<tr><td colspan='5'>Переходы по ссылкам пока не зафиксированы.</td></tr>";

        return $"""
            <div class='section-head'><div><p class='eyebrow'>Аналитика</p><h2>Переходы по ссылкам</h2></div></div>
            <div class='admin-stats'>
                <div class='admin-stat'><b>{clickedRecipients}</b><span>Кликнувшие получатели</span></div>
                <div class='admin-stat'><b>{totalClicks}</b><span>Кликов всего</span></div>
                <div class='admin-stat'><b>{FormatDate(lastClickedAt)}</b><span>Последнее нажатие</span></div>
            </div>
            <div class='admin-table-wrap'><table class='admin-table'><thead><tr><th>Email</th><th>Ссылка</th><th>Кликов</th><th>Первый клик</th><th>Последний клик</th></tr></thead><tbody>{tableRows}</tbody></table></div>
            """;
    }

    private static string ShortUrl(string url) => url.Length <= 90 ? url : url[..87] + "...";

    private static string FormatDate(DateTimeOffset? value) => value is null ? "-" : value.Value.ToString("yyyy-MM-dd HH:mm");

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
