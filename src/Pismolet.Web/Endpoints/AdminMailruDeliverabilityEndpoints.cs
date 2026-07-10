using System.Globalization;
using System.Net;
using System.Security.Claims;
using Pismolet.Web.Infrastructure.Postmaster;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class AdminMailruDeliverabilityEndpoints
{
    private const int DefaultDays = 30;
    private static readonly int[] AllowedDays = [7, 30, 90];
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private const string DashboardStyles = """
        <style>
            .mailru-header {display:flex;justify-content:space-between;gap:24px;align-items:flex-start;flex-wrap:wrap}
            .mailru-header-actions {display:flex;gap:10px;align-items:center;flex-wrap:wrap}
            .mailru-header-actions form {margin:0}
            .mailru-status {display:inline-flex;align-items:center;padding:7px 12px;border-radius:999px;font-weight:700;font-size:14px}
            .mailru-status.ok {background:#e8f6ee;color:#17653a}
            .mailru-status.warn {background:#fff4d8;color:#7a5200}
            .mailru-status.error {background:#fde9e7;color:#9d2b20}
            .mailru-status.off {background:#eceff3;color:#4f5b67}
            .mailru-periods {display:flex;gap:8px;flex-wrap:wrap;margin:18px 0}
            .mailru-period {display:inline-flex;padding:8px 12px;border:1px solid #d9dee7;border-radius:9px;text-decoration:none;color:inherit;background:#fff}
            .mailru-period.active {border-color:#214a3a;background:#edf6f1;font-weight:700}
            .mailru-summary {display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:12px;margin:18px 0}
            .mailru-card {border:1px solid #e1e5eb;border-radius:14px;background:#fff;padding:16px;min-width:0}
            .mailru-card small {display:block;color:#687383;margin-bottom:8px}
            .mailru-card b {font-size:24px;line-height:1.1}
            .mailru-card span {display:block;color:#687383;margin-top:6px;font-size:13px}
            .mailru-warning {border:1px solid #efc85a;background:#fff8df;border-radius:12px;padding:14px 16px;margin:16px 0}
            .mailru-notice {border:1px solid #a7c8b5;background:#edf7f1;border-radius:12px;padding:14px 16px;margin:16px 0}
            .mailru-notice.error {border-color:#e4aaa4;background:#fdf0ef}
            .mailru-meta {display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:12px;margin:18px 0}
            .mailru-meta div {border:1px solid #e1e5eb;border-radius:12px;padding:14px;background:#fff}
            .mailru-meta span {display:block;color:#687383;font-size:13px;margin-bottom:6px}
            .mailru-chart-grid {display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:16px;margin:18px 0 24px}
            .mailru-chart {border:1px solid #e1e5eb;border-radius:14px;background:#fff;padding:16px;overflow:hidden}
            .mailru-chart h3 {margin:0 0 4px}
            .mailru-chart p {margin:0 0 12px;color:#687383;font-size:13px}
            .mailru-chart svg {width:100%;height:auto;display:block}
            .mailru-legend {display:flex;gap:14px;flex-wrap:wrap;margin-top:10px;color:#687383;font-size:13px}
            .mailru-legend i {display:inline-block;width:16px;height:3px;border-radius:4px;vertical-align:middle;margin-right:5px}
            .mailru-legend .sent {background:#315b9d} .mailru-legend .delivered {background:#23835a}
            .mailru-legend .probably {background:#d49421} .mailru-legend .spam {background:#b84037} .mailru-legend .complaints {background:#7b4bb3}
            .mailru-empty-chart {min-height:180px;display:grid;place-items:center;color:#687383;background:#f7f8fa;border-radius:10px}
            .mailru-note {font-size:13px;color:#687383}
            @media (max-width:700px) {.mailru-chart-grid {grid-template-columns:1fr}}
        </style>
        """;

    public static IEndpointRouteBuilder MapAdminMailruDeliverabilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/admin/deliverability/mailru", ShowDashboard)
            .RequireAuthorization(AdminEndpoints.AdminPolicyName);
        endpoints.MapPost("/admin/deliverability/mailru/sync", RunManualSync)
            .RequireAuthorization(AdminEndpoints.AdminPolicyName);
        return endpoints;
    }

    private static async Task<IResult> ShowDashboard(
        HttpContext http,
        MailruPostmasterOptions options,
        IMailruPostmasterDashboardReader reader,
        CancellationToken cancellationToken)
    {
        var days = ReadDays(http);
        var (dateFrom, dateTo) = MailruPostmasterDashboardCalculator.CalculateCompletedMoscowWindow(
            DateTimeOffset.UtcNow,
            days);
        var data = await reader.ReadAsync(options.Domain, dateFrom, dateTo, cancellationToken);
        var summary = MailruPostmasterDashboardCalculator.Summarize(data.Days);
        var status = ResolveStatus(options, data.SyncState);
        var periodLinks = string.Join(string.Empty, AllowedDays.Select(value =>
            $"<a class='mailru-period{(value == days ? " active" : string.Empty)}' href='/admin/deliverability/mailru?days={value}'>{value} дней</a>"));
        var troubleRows = data.ActiveTroubles.Count == 0
            ? "<tr><td colspan='5'>Активных проблем SPF, DKIM или DMARC нет.</td></tr>"
            : string.Join(string.Empty, data.ActiveTroubles.Select(TroubleRow));
        var dailyRows = data.Days.Count == 0
            ? "<tr><td colspan='10'>За выбранный период Mail.ru ещё не вернул ежедневные метрики.</td></tr>"
            : string.Join(string.Empty, data.Days.OrderByDescending(x => x.Date).Select(DailyRow));
        var runRows = data.RecentRuns.Count == 0
            ? "<tr><td colspan='9'>Журнал запусков пока пуст. Новые фоновые и ручные синхронизации появятся после применения миграции.</td></tr>"
            : string.Join(string.Empty, data.RecentRuns.Select(RunRow));
        var lowSampleWarning = summary.IsLowSample
            ? $"<div class='mailru-warning'><b>Низкая выборка.</b> За период учтено {N(summary.MessagesSent)} писем. Проценты показываются вместе с абсолютными значениями и пока не должны использоваться для автоматических решений.</div>"
            : string.Empty;
        var syncNotice = BuildSyncNotice(http);
        var lastError = string.IsNullOrWhiteSpace(data.SyncState?.LastErrorCode) &&
                        string.IsNullOrWhiteSpace(data.SyncState?.LastErrorSummary)
            ? "<span class='admin-muted'>Нет</span>"
            : $"<b>{H(data.SyncState?.LastErrorCode ?? "sync_error")}</b><br><span>{H(data.SyncState?.LastErrorSummary)}</span>";

        var body = $"""
            {DashboardStyles}
            <section class='admin-panel'>
                <div class='mailru-header'>
                    <div>
                        <p class='eyebrow'>Доставляемость → Mail.ru</p>
                        <h1>Доменная доставляемость</h1>
                        <p class='admin-muted'>Локальная история Mail.ru Postmaster по домену <b>{H(data.Domain)}</b>. Открытие страницы не обращается к внешнему API.</p>
                    </div>
                    <div class='mailru-header-actions'>
                        <span class='mailru-status {status.CssClass}'>{H(status.Text)}</span>
                        <form method='post' action='/admin/deliverability/mailru/sync'>
                            <input type='hidden' name='days' value='{days}'>
                            <button class='admin-button' type='submit'>Синхронизировать сейчас</button>
                        </form>
                    </div>
                </div>

                {syncNotice}

                <div class='mailru-meta'>
                    <div><span>Домен</span><b>{H(data.Domain)}</b></div>
                    <div><span>Последняя успешная синхронизация</span><b>{FormatDate(data.SyncState?.LastSuccessAt)}</b></div>
                    <div><span>Последняя попытка</span><b>{FormatDate(data.SyncState?.LastAttemptAt)}</b></div>
                    <div><span>Последний обработанный день</span><b>{FormatDate(data.SyncState?.LastDomainDate)}</b></div>
                    <div><span>Последняя ошибка</span>{lastError}</div>
                    <div><span>Ошибок подряд</span><b>{data.SyncState?.ConsecutiveFailures ?? 0}</b></div>
                </div>

                <div class='mailru-periods'>{periodLinks}</div>
                <p class='mailru-note'>Период: {FormatDate(data.DateFrom)} — {FormatDate(data.DateTo)} по завершённым московским дням. Дней с данными: {summary.DataDays}.</p>

                {lowSampleWarning}

                <div class='mailru-summary'>
                    {MetricCard("Отправлено", summary.MessagesSent, null)}
                    {MetricCard("Доставлено", summary.Delivered, summary.DeliveredPercent)}
                    {MetricCard("Возможно спам", summary.ProbablySpam, summary.ProbablySpamPercent)}
                    {MetricCard("Спам", summary.Spam, summary.SpamPercent)}
                    {MetricCard("Жалобы", summary.Complaints, summary.ComplaintsPercent)}
                    {MetricCard("Прочитано", summary.Read, summary.ReadPercent)}
                    {MetricCard("Удалено прочитанным", summary.DeletedRead, summary.DeletedReadPercent)}
                    {MetricCard("Удалено непрочитанным", summary.DeletedUnread, summary.DeletedUnreadPercent)}
                    {ValueCard("Репутация", summary.LatestReputation)}
                    {ValueCard("Тренд", summary.LatestTrend)}
                </div>

                <div class='mailru-chart-grid'>
                    <div class='mailru-chart'>
                        <h3>Объём и доставка</h3>
                        <p>Количество отправленных и доставленных писем по дням.</p>
                        {BuildVolumeChart(data.Days)}
                    </div>
                    <div class='mailru-chart'>
                        <h3>Нежелательная почта и жалобы</h3>
                        <p>Доля от количества отправленных писем за каждый день.</p>
                        {BuildQualityChart(data.Days)}
                    </div>
                </div>

                <div class='section-head'><div><p class='eyebrow'>Проверки домена</p><h2>Активные проблемы SPF, DKIM и DMARC</h2></div><span class='admin-badge'>{data.ActiveTroubles.Count}</span></div>
                <div class='admin-table-wrap'>
                    <table class='admin-table'>
                        <thead><tr><th>Тип</th><th>Код</th><th>Сообщение Mail.ru</th><th>Впервые</th><th>Последний раз</th></tr></thead>
                        <tbody>{troubleRows}</tbody>
                    </table>
                </div>

                <div class='section-head'><div><p class='eyebrow'>Синхронизация</p><h2>Последние запуски</h2></div><span class='admin-badge'>{data.RecentRuns.Count}</span></div>
                <div class='admin-table-wrap'>
                    <table class='admin-table'>
                        <thead><tr><th>Начало</th><th>Источник</th><th>Статус</th><th>Период</th><th>Дней</th><th>Проблем</th><th>Длительность</th><th>Инициатор</th><th>Ошибка</th></tr></thead>
                        <tbody>{runRows}</tbody>
                    </table>
                </div>

                <div class='section-head'><div><p class='eyebrow'>Ежедневные данные</p><h2>Метрики по дням</h2></div></div>
                <div class='admin-table-wrap'>
                    <table class='admin-table'>
                        <thead><tr><th>Дата</th><th>Отправлено</th><th>Доставлено</th><th>Возможно спам</th><th>Спам</th><th>Жалобы</th><th>Прочитано</th><th>Удалено проч.</th><th>Удалено непроч.</th><th>Репутация / тренд</th></tr></thead>
                        <tbody>{dailyRows}</tbody>
                    </table>
                </div>
                <p class='mailru-note'>Данные относятся только к экосистеме Mail.ru и не заменяют внутреннюю статистику отправки Письмолёта.</p>
                <p><a class='admin-link' href='/admin'>Вернуться в админку</a></p>
            </section>
            """;

        return HtmlRenderer.Html(HtmlRenderer.Page("Админка - доставляемость Mail.ru", body, authenticated: true));
    }

    private static async Task<IResult> RunManualSync(
        HttpContext http,
        IMailruPostmasterManualSyncService manualSync,
        CancellationToken cancellationToken)
    {
        var days = ReadDays(http);
        var requestedBy = http.User.FindFirstValue(ClaimTypes.Email);
        var result = await manualSync.RunAsync(requestedBy, cancellationToken);
        var status = result.Status switch
        {
            MailruPostmasterManualSyncStatus.Succeeded => "succeeded",
            MailruPostmasterManualSyncStatus.Failed => "failed",
            MailruPostmasterManualSyncStatus.RateLimited => "rate_limited",
            MailruPostmasterManualSyncStatus.AlreadyRunning => "already_running",
            MailruPostmasterManualSyncStatus.Disabled => "disabled",
            MailruPostmasterManualSyncStatus.NotConfigured => "not_configured",
            _ => "failed"
        };
        var retry = result.RetryAfterSeconds is > 0
            ? $"&retryAfter={result.RetryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture)}"
            : string.Empty;
        return Results.Redirect($"/admin/deliverability/mailru?days={days}&sync={status}{retry}");
    }

    private static string BuildSyncNotice(HttpContext http)
    {
        var status = http.Request.Query["sync"].ToString();
        var retryAfter = int.TryParse(
            http.Request.Query["retryAfter"].ToString(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? Math.Max(1, parsed)
            : 60;
        return status switch
        {
            "succeeded" => "<div class='mailru-notice'><b>Синхронизация завершена.</b> Данные и журнал обновлены.</div>",
            "failed" => "<div class='mailru-notice error'><b>Синхронизация завершилась ошибкой.</b> Проверьте последнюю ошибку и журнал запусков.</div>",
            "rate_limited" => $"<div class='mailru-notice'><b>Повторный ручной запуск временно ограничен.</b> Попробуйте примерно через {retryAfter} сек.</div>",
            "already_running" => "<div class='mailru-notice'><b>Синхронизация уже выполняется.</b> Параллельный запуск не создан.</div>",
            "disabled" => "<div class='mailru-notice error'><b>Интеграция отключена.</b> Ручная синхронизация недоступна.</div>",
            "not_configured" => "<div class='mailru-notice error'><b>Интеграция не настроена.</b> Проверьте серверную конфигурацию.</div>",
            _ => string.Empty
        };
    }

    private static int ReadDays(HttpContext http)
    {
        var raw = http.Request.HasFormContentType
            ? http.Request.Form["days"].ToString()
            : http.Request.Query["days"].ToString();
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
               AllowedDays.Contains(parsed)
            ? parsed
            : DefaultDays;
    }

    private static (string Text, string CssClass) ResolveStatus(
        MailruPostmasterOptions options,
        MailruPostmasterDashboardSyncState? state)
    {
        if (!options.Enabled)
        {
            return ("Интеграция отключена", "off");
        }

        if (!options.IsConfigured)
        {
            return ("Интеграция не настроена", "warn");
        }

        if (state?.LastSuccessAt is null)
        {
            return ("Ожидает первой синхронизации", "warn");
        }

        if (state.ConsecutiveFailures > 0 || !string.IsNullOrWhiteSpace(state.LastErrorCode))
        {
            return ("Последняя синхронизация завершилась ошибкой", "error");
        }

        return ("Подключено", "ok");
    }

    private static string MetricCard(string label, long value, double? percent) => $"""
        <div class='mailru-card'>
            <small>{H(label)}</small>
            <b>{N(value)}</b>
            {(percent is null ? "<span>абсолютное значение</span>" : $"<span>{P(percent.Value)} от отправленных</span>")}
        </div>
        """;

    private static string ValueCard(string label, double? value) => $"""
        <div class='mailru-card'>
            <small>{H(label)}</small>
            <b>{(value is null ? "—" : value.Value.ToString("0.###", CultureInfo.InvariantCulture))}</b>
            <span>последний день с данными</span>
        </div>
        """;

    private static string TroubleRow(MailruPostmasterDashboardTrouble row) => $"""
        <tr>
            <td><span class='admin-badge'>{H(DetectTroubleType(row.Message))}</span></td>
            <td>{row.Code}</td>
            <td>{H(row.Message)}</td>
            <td>{FormatDate(row.FirstSeenAt)}</td>
            <td>{FormatDate(row.LastSeenAt)}</td>
        </tr>
        """;

    private static string RunRow(MailruPostmasterDashboardRun row) => $"""
        <tr>
            <td>{FormatDate(row.StartedAt)}</td>
            <td>{H(FormatTrigger(row.Trigger))}</td>
            <td><span class='admin-badge'>{H(FormatRunStatus(row.Status))}</span></td>
            <td>{FormatRange(row.DateFrom, row.DateTo)}</td>
            <td>{row.MetricDays}</td>
            <td>{row.TroubleCount}</td>
            <td>{FormatDuration(row.DurationMs)}</td>
            <td>{H(string.IsNullOrWhiteSpace(row.RequestedBy) ? "—" : row.RequestedBy)}</td>
            <td>{H(string.IsNullOrWhiteSpace(row.ErrorCode) ? "—" : row.ErrorCode)}</td>
        </tr>
        """;

    private static string DailyRow(MailruPostmasterDashboardDay row) => $"""
        <tr>
            <td>{FormatDate(row.Date)}</td>
            <td>{N(row.MessagesSent)}</td>
            <td>{N(row.Delivered)}<br><span class='admin-muted'>{P(MailruPostmasterDashboardCalculator.Percent(row.Delivered, row.MessagesSent))}</span></td>
            <td>{N(row.ProbablySpam)}<br><span class='admin-muted'>{P(MailruPostmasterDashboardCalculator.Percent(row.ProbablySpam, row.MessagesSent))}</span></td>
            <td>{N(row.Spam)}<br><span class='admin-muted'>{P(MailruPostmasterDashboardCalculator.Percent(row.Spam, row.MessagesSent))}</span></td>
            <td>{N(row.Complaints)}<br><span class='admin-muted'>{P(MailruPostmasterDashboardCalculator.Percent(row.Complaints, row.MessagesSent))}</span></td>
            <td>{N(row.Read)}<br><span class='admin-muted'>{P(MailruPostmasterDashboardCalculator.Percent(row.Read, row.MessagesSent))}</span></td>
            <td>{N(row.DeletedRead)}</td>
            <td>{N(row.DeletedUnread)}</td>
            <td>{row.Reputation.ToString("0.###", CultureInfo.InvariantCulture)} / {row.Trend.ToString("0.###", CultureInfo.InvariantCulture)}</td>
        </tr>
        """;

    private static string BuildVolumeChart(IReadOnlyList<MailruPostmasterDashboardDay> days)
    {
        if (days.Count == 0)
        {
            return "<div class='mailru-empty-chart'>Нет данных за выбранный период</div>";
        }

        var max = Math.Max(1d, days.Max(x => (double)x.MessagesSent));
        var sentPoints = BuildPoints(days, x => x.MessagesSent, max);
        var deliveredPoints = BuildPoints(days, x => x.Delivered, max);
        return $"""
            <svg viewBox='0 0 900 250' role='img' aria-label='График отправленных и доставленных писем'>
                {GridLines(max, string.Empty)}
                <polyline fill='none' stroke='#315b9d' stroke-width='4' stroke-linejoin='round' stroke-linecap='round' points='{sentPoints}' />
                <polyline fill='none' stroke='#23835a' stroke-width='4' stroke-linejoin='round' stroke-linecap='round' points='{deliveredPoints}' />
                {AxisLabels(days)}
            </svg>
            <div class='mailru-legend'><span><i class='sent'></i>Отправлено</span><span><i class='delivered'></i>Доставлено</span></div>
            """;
    }

    private static string BuildQualityChart(IReadOnlyList<MailruPostmasterDashboardDay> days)
    {
        if (days.Count == 0)
        {
            return "<div class='mailru-empty-chart'>Нет данных за выбранный период</div>";
        }

        var probablyValues = days.Select(x => MailruPostmasterDashboardCalculator.Percent(x.ProbablySpam, x.MessagesSent)).ToArray();
        var spamValues = days.Select(x => MailruPostmasterDashboardCalculator.Percent(x.Spam, x.MessagesSent)).ToArray();
        var complaintValues = days.Select(x => MailruPostmasterDashboardCalculator.Percent(x.Complaints, x.MessagesSent)).ToArray();
        var max = Math.Max(1d, Math.Max(probablyValues.Max(), Math.Max(spamValues.Max(), complaintValues.Max())) * 1.1d);
        var probablyPoints = BuildPoints(probablyValues, max);
        var spamPoints = BuildPoints(spamValues, max);
        var complaintPoints = BuildPoints(complaintValues, max);
        return $"""
            <svg viewBox='0 0 900 250' role='img' aria-label='График доли нежелательной почты и жалоб'>
                {GridLines(max, "%")}
                <polyline fill='none' stroke='#d49421' stroke-width='4' stroke-linejoin='round' stroke-linecap='round' points='{probablyPoints}' />
                <polyline fill='none' stroke='#b84037' stroke-width='4' stroke-linejoin='round' stroke-linecap='round' points='{spamPoints}' />
                <polyline fill='none' stroke='#7b4bb3' stroke-width='4' stroke-linejoin='round' stroke-linecap='round' points='{complaintPoints}' />
                {AxisLabels(days)}
            </svg>
            <div class='mailru-legend'><span><i class='probably'></i>Возможно спам</span><span><i class='spam'></i>Спам</span><span><i class='complaints'></i>Жалобы</span></div>
            """;
    }

    private static string BuildPoints(
        IReadOnlyList<MailruPostmasterDashboardDay> days,
        Func<MailruPostmasterDashboardDay, double> selector,
        double max) => BuildPoints(days.Select(selector).ToArray(), max);

    private static string BuildPoints(IReadOnlyList<double> values, double max)
    {
        const double left = 58d;
        const double top = 18d;
        const double width = 812d;
        const double height = 178d;
        var denominator = Math.Max(1, values.Count - 1);
        return string.Join(" ", values.Select((value, index) =>
        {
            var x = left + width * index / denominator;
            var y = top + height - Math.Clamp(value / max, 0d, 1d) * height;
            return $"{F(x)},{F(y)}";
        }));
    }

    private static string GridLines(double max, string suffix)
    {
        const double left = 58d;
        const double right = 870d;
        const double top = 18d;
        const double height = 178d;
        return string.Join(string.Empty, Enumerable.Range(0, 5).Select(index =>
        {
            var ratio = index / 4d;
            var y = top + height * ratio;
            var value = max * (1d - ratio);
            return $"<line x1='{F(left)}' y1='{F(y)}' x2='{F(right)}' y2='{F(y)}' stroke='#e4e8ee' stroke-width='1'/><text x='4' y='{F(y + 4)}' fill='#687383' font-size='12'>{H(value.ToString("0.#", CultureInfo.InvariantCulture) + suffix)}</text>";
        }));
    }

    private static string AxisLabels(IReadOnlyList<MailruPostmasterDashboardDay> days)
    {
        var first = days[0].Date.ToString("dd.MM", CultureInfo.InvariantCulture);
        var last = days[^1].Date.ToString("dd.MM", CultureInfo.InvariantCulture);
        return $"<text x='58' y='230' fill='#687383' font-size='12'>{first}</text><text x='840' y='230' fill='#687383' font-size='12'>{last}</text>";
    }

    private static string DetectTroubleType(string message)
    {
        if (message.Contains("SPF", StringComparison.OrdinalIgnoreCase))
        {
            return "SPF";
        }

        if (message.Contains("DKIM", StringComparison.OrdinalIgnoreCase))
        {
            return "DKIM";
        }

        if (message.Contains("DMARC", StringComparison.OrdinalIgnoreCase))
        {
            return "DMARC";
        }

        return "Домен";
    }

    private static string FormatTrigger(string value) => value switch
    {
        MailruPostmasterSyncTriggers.Manual => "Ручной",
        MailruPostmasterSyncTriggers.Scheduled => "Фоновый",
        _ => value
    };

    private static string FormatRunStatus(string value) => value switch
    {
        "running" => "Выполняется",
        "succeeded" => "Успешно",
        "failed" => "Ошибка",
        "cancelled" => "Отменён",
        "disabled" => "Отключено",
        "not_configured" => "Не настроено",
        "skipped_already_running" => "Пропущен: уже выполняется",
        _ => value
    };

    private static string FormatRange(DateOnly? dateFrom, DateOnly? dateTo) =>
        dateFrom is null || dateTo is null
            ? "—"
            : $"{FormatDate(dateFrom.Value)} — {FormatDate(dateTo.Value)}";

    private static string FormatDuration(long? durationMs) =>
        durationMs is null ? "—" : $"{durationMs.Value.ToString("N0", RussianCulture)} мс";

    private static string FormatDate(DateTimeOffset? value) =>
        value is null ? "—" : value.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", RussianCulture);

    private static string FormatDate(DateOnly? value) =>
        value is null ? "—" : value.Value.ToString("dd.MM.yyyy", RussianCulture);

    private static string FormatDate(DateOnly value) => value.ToString("dd.MM.yyyy", RussianCulture);

    private static string N(long value) => value.ToString("N0", RussianCulture);

    private static string P(double value) => value.ToString("0.##", RussianCulture) + "%";

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
