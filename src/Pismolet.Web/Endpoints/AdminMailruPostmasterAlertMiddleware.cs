using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pismolet.Web.Infrastructure.Postmaster;

namespace Pismolet.Web.Endpoints;

public static class AdminMailruPostmasterAlertMiddleware
{
    private const string DashboardPath = "/admin/deliverability/mailru";
    private const string BlockId = "mailru-operational-alerts";
    private const string InsertMarker = "<div class='mailru-summary'>";
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private const string BlockStyles = """
        <style>
            .mailru-alert-panel {margin:20px 0 24px;padding:18px;border:1px solid #dfe4ea;border-radius:16px;background:#fff}
            .mailru-alert-head {display:flex;justify-content:space-between;gap:16px;align-items:flex-start;flex-wrap:wrap}
            .mailru-alert-head h2 {margin:2px 0 6px}
            .mailru-alert-status {display:inline-flex;align-items:center;padding:7px 12px;border-radius:999px;font-size:14px;font-weight:700}
            .mailru-alert-status.off {background:#eceff3;color:#4f5b67}
            .mailru-alert-status.nodata {background:#eef2f7;color:#465466}
            .mailru-alert-status.calm {background:#e8f6ee;color:#17653a}
            .mailru-alert-status.warning {background:#fff4d8;color:#7a5200}
            .mailru-alert-status.critical {background:#fde9e7;color:#9d2b20}
            .mailru-alert-observation,.mailru-alert-low-sample {margin:14px 0;padding:12px 14px;border-radius:11px}
            .mailru-alert-observation {border:1px solid #a7c8b5;background:#edf7f1}
            .mailru-alert-low-sample {border:1px solid #efc85a;background:#fff8df}
            .mailru-alert-list {display:grid;gap:10px;margin-top:14px}
            .mailru-alert-signal {border:1px solid #dfe4ea;border-left-width:5px;border-radius:12px;padding:13px 14px;background:#fff}
            .mailru-alert-signal.info {border-left-color:#6d7f91}
            .mailru-alert-signal.warning {border-left-color:#d49421;background:#fffdf6}
            .mailru-alert-signal.critical {border-left-color:#b84037;background:#fff9f8}
            .mailru-alert-signal-head {display:flex;justify-content:space-between;gap:12px;align-items:flex-start;flex-wrap:wrap}
            .mailru-alert-signal h3 {font-size:16px;margin:0 0 6px}
            .mailru-alert-signal p {margin:0}
            .mailru-alert-severity {font-size:12px;font-weight:700;text-transform:uppercase;letter-spacing:.04em}
            .mailru-alert-meta {display:flex;gap:10px;flex-wrap:wrap;margin-top:8px;color:#687383;font-size:12px}
            .mailru-alert-empty {margin-top:14px;padding:14px;border-radius:11px;background:#f7f8fa;color:#586575}
            .mailru-alert-window {margin:12px 0 0;color:#687383;font-size:13px}
        </style>
        """;

    public static IApplicationBuilder UseAdminMailruPostmasterAlerts(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.Request.Method) ||
                !string.Equals(context.Request.Path.Value, DashboardPath, StringComparison.OrdinalIgnoreCase))
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

                if (!IsHtmlResponse(context.Response) || context.Response.StatusCode != StatusCodes.Status200OK)
                {
                    await buffer.CopyToAsync(originalBody, context.RequestAborted);
                    return;
                }

                using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                var html = await reader.ReadToEndAsync(context.RequestAborted);
                var block = await BuildAlertBlockSafelyAsync(
                    context.RequestServices,
                    context.Request.Query,
                    context.RequestAborted);
                var updatedHtml = InjectAlertBlock(html, block);
                var bytes = Encoding.UTF8.GetBytes(updatedHtml);
                context.Response.ContentLength = bytes.Length;
                await originalBody.WriteAsync(bytes, context.RequestAborted);
            }
            finally
            {
                context.Response.Body = originalBody;
            }
        });
    }

    public static string InjectAlertBlock(string html, string block)
    {
        if (string.IsNullOrWhiteSpace(html) ||
            string.IsNullOrWhiteSpace(block) ||
            html.Contains($"id='{BlockId}'", StringComparison.OrdinalIgnoreCase) ||
            !html.Contains(InsertMarker, StringComparison.Ordinal))
        {
            return html;
        }

        return html.Replace(InsertMarker, block + InsertMarker, StringComparison.Ordinal);
    }

    public static string RenderAlertBlock(
        MailruPostmasterAlertEvaluation evaluation,
        MailruPostmasterAlertOptions options,
        DateOnly currentDateFrom,
        DateOnly currentDateTo,
        DateOnly previousDateFrom,
        DateOnly previousDateTo,
        string journalBlock = "")
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentNullException.ThrowIfNull(options);

        var normalizedOptions = options.Normalize();
        var (statusText, statusCss) = StatusPresentation(evaluation.Status);
        var signals = evaluation.Signals.Count == 0
            ? EmptySignalsMessage(evaluation.Status)
            : string.Join(string.Empty, evaluation.Signals.Select(RenderSignal));
        var observation = evaluation.IsObservation
            ? "<div class='mailru-alert-observation'><b>Режим наблюдения.</b> Сигналы не влияют на отправку, лимиты и модерацию. Автоматических действий нет.</div>"
            : string.Empty;
        var lowSample = evaluation.IsLowSample && evaluation.Status != MailruPostmasterAlertOverallStatus.Off
            ? $"<div class='mailru-alert-low-sample'><b>Малая выборка.</b> За текущее окно учтено {N(evaluation.MessagesSent)} писем. Процентные правила применяются только после {N(normalizedOptions.MinimumMessagesForRates)} писем.</div>"
            : string.Empty;
        var observationStarted = normalizedOptions.ObservationStartedAt is null
            ? "не зафиксировано"
            : normalizedOptions.ObservationStartedAt.Value.ToUniversalTime().ToString("dd.MM.yyyy HH:mm 'UTC'", RussianCulture);

        return $"""
            <section id='{BlockId}' class='mailru-alert-panel'>
                {BlockStyles}
                <div class='mailru-alert-head'>
                    <div>
                        <p class='eyebrow'>PM-5 → эксплуатационный контроль</p>
                        <h2>Сигналы доставляемости</h2>
                        <p class='admin-muted'>Оценка выполняется только по локально сохранённым данным Mail.ru Postmaster.</p>
                    </div>
                    <span class='mailru-alert-status {statusCss}'>{H(statusText)}</span>
                </div>
                {observation}
                {lowSample}
                <div class='mailru-alert-list'>{signals}</div>
                <p class='mailru-alert-window'>Текущее окно: {D(currentDateFrom)} — {D(currentDateTo)}. Сравнение: {D(previousDateFrom)} — {D(previousDateTo)}. Начало наблюдения: {H(observationStarted)}.</p>
                {journalBlock}
            </section>
            """;
    }

    public static string RenderUnavailableBlock() => $"""
        <section id='{BlockId}' class='mailru-alert-panel' style='margin:20px 0 24px;padding:18px;border:1px solid #efc85a;border-radius:16px;background:#fff8df'>
            <p class='eyebrow'>PM-5 → эксплуатационный контроль</p>
            <h2>Сигналы временно недоступны</h2>
            <p>Не удалось оценить локальные данные. Ошибка PM-5 не влияет на отправку писем и работу Mail.ru Postmaster.</p>
        </section>
        """;

    private static async Task<string> BuildAlertBlockSafelyAsync(
        IServiceProvider services,
        IQueryCollection query,
        CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("MailruPostmasterAlerts");
        try
        {
            var alertOptions = services.GetRequiredService<MailruPostmasterAlertOptions>().Normalize();
            var integrationOptions = services.GetRequiredService<MailruPostmasterOptions>();
            var nowUtc = DateTimeOffset.UtcNow;
            var (currentDateFrom, currentDateTo) = MailruPostmasterDashboardCalculator.CalculateCompletedMoscowWindow(
                nowUtc,
                alertOptions.WindowDays);
            var previousDateTo = currentDateFrom.AddDays(-1);
            var previousDateFrom = previousDateTo.AddDays(-(alertOptions.WindowDays - 1));
            var journalBlock = await BuildJournalBlockSafelyAsync(
                services,
                integrationOptions,
                query,
                logger,
                cancellationToken);

            if (!alertOptions.Enabled)
            {
                var disabled = new MailruPostmasterAlertEvaluation(
                    MailruPostmasterAlertOverallStatus.Off,
                    alertOptions.ObservationMode,
                    true,
                    0,
                    nowUtc,
                    Array.Empty<MailruPostmasterAlertSignal>());
                return RenderAlertBlock(
                    disabled,
                    alertOptions,
                    currentDateFrom,
                    currentDateTo,
                    previousDateFrom,
                    previousDateTo,
                    journalBlock);
            }

            MailruPostmasterDashboardData data;
            if (integrationOptions.IsConfigured)
            {
                var dashboardReader = services.GetRequiredService<IMailruPostmasterDashboardReader>();
                data = await dashboardReader.ReadAsync(
                    integrationOptions.Domain,
                    previousDateFrom,
                    currentDateTo,
                    cancellationToken);
            }
            else
            {
                data = new MailruPostmasterDashboardData(
                    integrationOptions.Domain,
                    previousDateFrom,
                    currentDateTo,
                    null,
                    Array.Empty<MailruPostmasterDashboardTrouble>(),
                    Array.Empty<MailruPostmasterDashboardDay>(),
                    Array.Empty<MailruPostmasterDashboardRun>());
            }

            var input = new MailruPostmasterAlertInput(
                data.Domain,
                integrationOptions.Enabled,
                integrationOptions.IsConfigured,
                currentDateFrom,
                currentDateTo,
                data.Days.Where(x => x.Date >= currentDateFrom && x.Date <= currentDateTo).ToArray(),
                previousDateFrom,
                previousDateTo,
                data.Days.Where(x => x.Date >= previousDateFrom && x.Date <= previousDateTo).ToArray(),
                data.ActiveTroubles,
                data.SyncState);
            var evaluator = services.GetRequiredService<IMailruPostmasterAlertEvaluator>();
            var evaluation = evaluator.Evaluate(input, alertOptions, nowUtc);
            return RenderAlertBlock(
                evaluation,
                alertOptions,
                currentDateFrom,
                currentDateTo,
                previousDateFrom,
                previousDateTo,
                journalBlock);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось построить блок эксплуатационных сигналов Mail.ru Postmaster.");
            return RenderUnavailableBlock();
        }
    }

    private static async Task<string> BuildJournalBlockSafelyAsync(
        IServiceProvider services,
        MailruPostmasterOptions integrationOptions,
        IQueryCollection query,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var filter = AdminMailruPostmasterAlertJournalRenderer.ParseFilter(
            query["pm5Status"].ToString(),
            query["pm5Severity"].ToString());

        try
        {
            var journalStore = services.GetService<IMailruPostmasterAlertJournalStore>();
            if (journalStore is null || string.IsNullOrWhiteSpace(integrationOptions.Domain))
            {
                return AdminMailruPostmasterAlertJournalRenderer.Render(
                    Array.Empty<MailruPostmasterAlertJournalEvent>(),
                    filter,
                    isAvailable: false);
            }

            var events = await journalStore.ReadRecentAsync(
                integrationOptions.Domain,
                filter.Status,
                filter.Severity,
                take: 100,
                cancellationToken);
            return AdminMailruPostmasterAlertJournalRenderer.Render(
                events,
                filter,
                isAvailable: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Не удалось прочитать журнал эксплуатационных сигналов Mail.ru Postmaster.");
            return AdminMailruPostmasterAlertJournalRenderer.Render(
                Array.Empty<MailruPostmasterAlertJournalEvent>(),
                filter,
                isAvailable: false);
        }
    }

    private static string RenderSignal(MailruPostmasterAlertSignal signal)
    {
        var severity = signal.Severity switch
        {
            MailruPostmasterAlertSeverity.Critical => ("Критично", "critical"),
            MailruPostmasterAlertSeverity.Warning => ("Внимание", "warning"),
            _ => ("Информация", "info")
        };
        var period = signal.DateFrom is null || signal.DateTo is null
            ? string.Empty
            : $"<span>Период: {D(signal.DateFrom.Value)} — {D(signal.DateTo.Value)}</span>";
        var sample = signal.MessagesSent > 0
            ? $"<span>Отправлено: {N(signal.MessagesSent)}</span>"
            : string.Empty;
        var lowSample = signal.IsLowSample
            ? "<span>Малая выборка</span>"
            : string.Empty;

        return $"""
            <article class='mailru-alert-signal {severity.Item2}'>
                <div class='mailru-alert-signal-head'>
                    <div>
                        <h3>{H(signal.Title)}</h3>
                        <p>{H(signal.Summary)}</p>
                    </div>
                    <span class='mailru-alert-severity'>{severity.Item1}</span>
                </div>
                <div class='mailru-alert-meta'>{period}{sample}{lowSample}<span>Код: {H(signal.Code)}</span></div>
            </article>
            """;
    }

    private static string EmptySignalsMessage(MailruPostmasterAlertOverallStatus status)
    {
        var text = status switch
        {
            MailruPostmasterAlertOverallStatus.Off => "PM-5 выключен конфигурацией. Сбор доменных метрик и статистики рассылок продолжает работать независимо.",
            MailruPostmasterAlertOverallStatus.Calm => "За текущее окно эксплуатационные сигналы не сработали.",
            _ => "Пока недостаточно данных для содержательной оценки. Это не считается подтверждением хорошей или плохой доставляемости."
        };
        return $"<div class='mailru-alert-empty'>{H(text)}</div>";
    }

    private static (string Text, string CssClass) StatusPresentation(MailruPostmasterAlertOverallStatus status) => status switch
    {
        MailruPostmasterAlertOverallStatus.Off => ("Выключено", "off"),
        MailruPostmasterAlertOverallStatus.NoData => ("Недостаточно данных", "nodata"),
        MailruPostmasterAlertOverallStatus.Calm => ("Спокойно", "calm"),
        MailruPostmasterAlertOverallStatus.Attention => ("Требует внимания", "warning"),
        MailruPostmasterAlertOverallStatus.Critical => ("Критично", "critical"),
        _ => ("Неизвестно", "nodata")
    };

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string D(DateOnly value) => value.ToString("dd.MM.yyyy", RussianCulture);

    private static string N(long value) => value.ToString("N0", RussianCulture);

    private static bool IsHtmlResponse(HttpResponse response) =>
        response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;
}
