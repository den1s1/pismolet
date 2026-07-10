using System.Globalization;
using System.Net;
using System.Text;
using Pismolet.Web.Infrastructure.Postmaster;

namespace Pismolet.Web.Endpoints;

public static class AdminCampaignMailruPostmasterMiddleware
{
    private const string BlockMarker = "data-mailru-postmaster-block='true'";
    private const string InsertMarker = "<div class='section-head'><div><p class='eyebrow'>Отправка</p><h2>Лог отправки</h2></div>";

    public static IApplicationBuilder UseAdminCampaignMailruPostmaster(this IApplicationBuilder app) => app.Use(async (context, next) =>
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

            var integrationOptions = context.RequestServices.GetRequiredService<MailruPostmasterOptions>();
            var mailingOptions = context.RequestServices.GetRequiredService<MailruPostmasterMailingMetricsOptions>();
            var metricsReader = context.RequestServices.GetRequiredService<IMailruPostmasterMailingMetricsReader>();

            try
            {
                var data = await metricsReader.ReadAsync(
                    integrationOptions.Domain,
                    campaignId,
                    context.RequestAborted);
                var enhanced = InjectBlock(
                    html,
                    data,
                    integrationOptions.Enabled,
                    mailingOptions.Enabled);
                context.Response.ContentLength = Encoding.UTF8.GetByteCount(enhanced);
                await context.Response.WriteAsync(enhanced);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(nameof(AdminCampaignMailruPostmasterMiddleware));
                logger.LogError(
                    ex,
                    "Не удалось прочитать локальную статистику Mail.ru Postmaster для карточки рассылки. mailingId={MailingId}",
                    campaignId);
                context.Response.ContentLength = Encoding.UTF8.GetByteCount(html);
                await context.Response.WriteAsync(html);
            }
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    });

    public static string InjectBlock(
        string html,
        MailruPostmasterMailingMetricsData data,
        bool integrationEnabled,
        bool mailingMetricsEnabled)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(data);

        if (html.Contains(BlockMarker, StringComparison.Ordinal) ||
            !html.Contains(InsertMarker, StringComparison.Ordinal))
        {
            return html;
        }

        return html.Replace(
            InsertMarker,
            BuildBlock(data, integrationEnabled, mailingMetricsEnabled) + InsertMarker,
            StringComparison.Ordinal);
    }

    public static string BuildBlock(
        MailruPostmasterMailingMetricsData data,
        bool integrationEnabled,
        bool mailingMetricsEnabled)
    {
        ArgumentNullException.ThrowIfNull(data);

        var status = MailruPostmasterMailingMetricsCalculator.ResolveStatus(
            integrationEnabled,
            mailingMetricsEnabled,
            data);
        var summary = MailruPostmasterMailingMetricsCalculator.Summarize(data.Days);
        var statusText = StatusText(status);
        var period = summary.DateFrom is null || summary.DateTo is null
            ? "данных пока нет"
            : $"{summary.DateFrom:yyyy-MM-dd} — {summary.DateTo:yyyy-MM-dd}";
        var state = data.State;
        var statusMessage = BuildStatusMessage(status, state);
        var error = status == MailruPostmasterMailingMetricsViewStatus.Failed &&
                    !string.IsNullOrWhiteSpace(state?.LastErrorSummary)
            ? $"<p class='admin-alert'>Последняя попытка завершилась ошибкой: {H(state.LastErrorSummary)}</p>"
            : string.Empty;
        var metrics = summary.DataDays == 0
            ? "<p class='admin-muted'>В локальном хранилище пока нет статистики Mail.ru для этой рассылки.</p>"
            : BuildMetrics(summary);

        return $"""
            <section {BlockMarker}>
                <div class='section-head'>
                    <div>
                        <p class='eyebrow'>Внешняя статистика</p>
                        <h2>Mail.ru Postmaster</h2>
                    </div>
                    <span class='admin-badge'>{H(statusText)}</span>
                </div>
                <p class='admin-muted'>Данные относятся только к экосистеме Mail.ru и показываются отдельно от внутренних показателей Письмолёта.</p>
                <div class='admin-profile-grid'>
                    <div><span>Статус синхронизации</span><b>{H(statusText)}</b></div>
                    <div><span>Период данных</span><b>{H(period)}</b></div>
                    <div><span>Дней с данными</span><b>{summary.DataDays}</b></div>
                    <div><span>Последнее успешное обновление</span><b>{FormatDate(state?.LastSuccessAt)}</b></div>
                    <div><span>Последнее локальное обновление метрик</span><b>{FormatDate(summary.LastUpdatedAt)}</b></div>
                    <div><span>msgtype</span><b>{H(data.MsgType)}</b></div>
                </div>
                <p class='admin-muted'>{H(statusMessage)}</p>
                {error}
                {metrics}
            </section>
            """;
    }

    private static string BuildMetrics(MailruPostmasterMailingMetricsSummary summary) => $"""
        <div class='admin-stats'>
            <div class='admin-stat'><b>{summary.MessagesSent}</b><span>Учтено Mail.ru</span></div>
            <div class='admin-stat'><b>{summary.Delivered}</b><span>Доставлено · {Percent(summary.DeliveredPercent)}</span></div>
            <div class='admin-stat'><b>{summary.ProbablySpam}</b><span>Возможно спам · {Percent(summary.ProbablySpamPercent)}</span></div>
            <div class='admin-stat'><b>{summary.Spam}</b><span>Спам · {Percent(summary.SpamPercent)}</span></div>
            <div class='admin-stat'><b>{summary.Complaints}</b><span>Жалобы · {Percent(summary.ComplaintsPercent)}</span></div>
            <div class='admin-stat'><b>{summary.Read}</b><span>Прочитано · {Percent(summary.ReadPercent)}</span></div>
            <div class='admin-stat'><b>{summary.DeletedRead}</b><span>Удалено прочитанным · {Percent(summary.DeletedReadPercent)}</span></div>
            <div class='admin-stat'><b>{summary.DeletedUnread}</b><span>Удалено непрочитанным · {Percent(summary.DeletedUnreadPercent)}</span></div>
        </div>
        """;

    private static string BuildStatusMessage(
        MailruPostmasterMailingMetricsViewStatus status,
        MailruPostmasterMailingMetricsState? state) => status switch
    {
        MailruPostmasterMailingMetricsViewStatus.Disabled =>
            "Синхронизация статистики конкретных рассылок выключена feature flag. Уже сохранённые локальные данные остаются доступными.",
        MailruPostmasterMailingMetricsViewStatus.WaitingForFirstCompletedDay =>
            "Ожидается первый завершённый московский день и первая локальная синхронизация.",
        MailruPostmasterMailingMetricsViewStatus.NoData =>
            "Mail.ru успешно ответил, но данных по этому msgtype пока нет. Повторная проверка будет выполнена по расписанию.",
        MailruPostmasterMailingMetricsViewStatus.Updating =>
            "Данные получены и ещё могут уточняться повторными синхронизациями.",
        MailruPostmasterMailingMetricsViewStatus.Stable =>
            "Данные признаны стабильными; следующая контрольная проверка выполняется с увеличенным интервалом.",
        MailruPostmasterMailingMetricsViewStatus.Completed =>
            $"Синхронизация завершена{(state?.CompletedAt is null ? "." : $" {FormatDate(state.CompletedAt)}.")}",
        MailruPostmasterMailingMetricsViewStatus.Failed =>
            "Последняя попытка синхронизации завершилась ошибкой. Ошибка изолирована и не влияет на отправку писем.",
        _ => string.Empty
    };

    private static string StatusText(MailruPostmasterMailingMetricsViewStatus status) => status switch
    {
        MailruPostmasterMailingMetricsViewStatus.Disabled => "PM-4 отключён",
        MailruPostmasterMailingMetricsViewStatus.WaitingForFirstCompletedDay => "Ожидает первого дня",
        MailruPostmasterMailingMetricsViewStatus.NoData => "Данных пока нет",
        MailruPostmasterMailingMetricsViewStatus.Updating => "Данные обновляются",
        MailruPostmasterMailingMetricsViewStatus.Stable => "Данные стабильны",
        MailruPostmasterMailingMetricsViewStatus.Completed => "Синхронизация завершена",
        MailruPostmasterMailingMetricsViewStatus.Failed => "Ошибка последней попытки",
        _ => "Неизвестно"
    };

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

    private static string Percent(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static string FormatDate(DateTimeOffset? value) =>
        value is null ? "-" : value.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
