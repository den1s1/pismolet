using System.Globalization;
using System.Net;
using Pismolet.Web.Infrastructure.Postmaster;

namespace Pismolet.Web.Endpoints;

public sealed record AdminMailruPostmasterAlertJournalFilter(
    MailruPostmasterAlertEventStatus? Status,
    MailruPostmasterAlertSeverity? Severity);

public static class AdminMailruPostmasterAlertJournalRenderer
{
    private const string DashboardPath = "/admin/deliverability/mailru";
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private const string Styles = """
        <style>
            .mailru-alert-journal {margin-top:22px;padding-top:20px;border-top:1px solid #e3e7ec}
            .mailru-alert-journal-head {display:flex;justify-content:space-between;gap:12px;align-items:flex-start;flex-wrap:wrap}
            .mailru-alert-journal-head h3 {margin:0 0 5px;font-size:20px}
            .mailru-alert-journal-count {font-size:13px;color:#687383}
            .mailru-alert-journal-filter {display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap;margin:14px 0}
            .mailru-alert-journal-filter label {display:grid;gap:5px;font-size:12px;color:#586575}
            .mailru-alert-journal-filter select {min-width:150px;padding:8px 10px;border:1px solid #cfd6df;border-radius:9px;background:#fff}
            .mailru-alert-journal-filter button,.mailru-alert-journal-filter a {display:inline-flex;align-items:center;justify-content:center;min-height:38px;padding:8px 12px;border-radius:9px;text-decoration:none;font-weight:600}
            .mailru-alert-journal-filter button {border:1px solid #27384a;background:#27384a;color:#fff;cursor:pointer}
            .mailru-alert-journal-filter a {border:1px solid #cfd6df;color:#27384a;background:#fff}
            .mailru-alert-journal-list {display:grid;gap:10px}
            .mailru-alert-journal-event {border:1px solid #dfe4ea;border-left-width:5px;border-radius:12px;padding:13px 14px;background:#fff}
            .mailru-alert-journal-event.info {border-left-color:#6d7f91}
            .mailru-alert-journal-event.warning {border-left-color:#d49421;background:#fffdf6}
            .mailru-alert-journal-event.critical {border-left-color:#b84037;background:#fff9f8}
            .mailru-alert-journal-event-head {display:flex;justify-content:space-between;gap:12px;align-items:flex-start;flex-wrap:wrap}
            .mailru-alert-journal-event h4 {margin:0 0 5px;font-size:16px}
            .mailru-alert-journal-code {font-family:ui-monospace,SFMono-Regular,Consolas,monospace;font-size:12px;color:#586575}
            .mailru-alert-journal-badges {display:flex;gap:7px;flex-wrap:wrap}
            .mailru-alert-journal-badge {display:inline-flex;padding:4px 8px;border-radius:999px;font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.03em}
            .mailru-alert-journal-badge.active {background:#fff4d8;color:#7a5200}
            .mailru-alert-journal-badge.resolved {background:#e8f6ee;color:#17653a}
            .mailru-alert-journal-badge.info {background:#eef2f7;color:#465466}
            .mailru-alert-journal-badge.warning {background:#fff4d8;color:#7a5200}
            .mailru-alert-journal-badge.critical {background:#fde9e7;color:#9d2b20}
            .mailru-alert-journal-meta,.mailru-alert-journal-values {display:flex;gap:10px;flex-wrap:wrap;margin-top:9px;color:#687383;font-size:12px}
            .mailru-alert-journal-values {color:#39495a}
            .mailru-alert-journal-empty,.mailru-alert-journal-unavailable {padding:13px 14px;border-radius:11px}
            .mailru-alert-journal-empty {background:#f7f8fa;color:#586575}
            .mailru-alert-journal-unavailable {border:1px solid #efc85a;background:#fff8df;color:#6c5100}
        </style>
        """;

    public static AdminMailruPostmasterAlertJournalFilter ParseFilter(
        string? status,
        string? severity) => new(
        ParseStatus(status),
        ParseSeverity(severity));

    public static string Render(
        IReadOnlyList<MailruPostmasterAlertJournalEvent> events,
        AdminMailruPostmasterAlertJournalFilter filter,
        bool isAvailable)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(filter);

        var content = !isAvailable
            ? "<div class='mailru-alert-journal-unavailable'><b>Журнал временно недоступен.</b> Ошибка чтения не влияет на синхронизацию Mail.ru Postmaster и отправку писем.</div>"
            : events.Count == 0
                ? "<div class='mailru-alert-journal-empty'>Записей по выбранному фильтру нет.</div>"
                : $"<div class='mailru-alert-journal-list'>{string.Join(string.Empty, events.Select(RenderEvent))}</div>";

        return $"""
            <section id='mailru-alert-journal' class='mailru-alert-journal'>
                {Styles}
                <div class='mailru-alert-journal-head'>
                    <div>
                        <h3>Журнал сигналов</h3>
                        <p class='admin-muted'>Последние события PM-5. Журнал предназначен только для наблюдения и не запускает автоматические ограничения.</p>
                    </div>
                    <span class='mailru-alert-journal-count'>Показано: {N(events.Count)} из последних 100</span>
                </div>
                <form class='mailru-alert-journal-filter' method='get' action='{DashboardPath}'>
                    <label>Статус
                        <select name='pm5Status'>
                            <option value='all'{Selected(filter.Status is null)}>Все</option>
                            <option value='active'{Selected(filter.Status == MailruPostmasterAlertEventStatus.Active)}>Активные</option>
                            <option value='resolved'{Selected(filter.Status == MailruPostmasterAlertEventStatus.Resolved)}>Закрытые</option>
                        </select>
                    </label>
                    <label>Критичность
                        <select name='pm5Severity'>
                            <option value='all'{Selected(filter.Severity is null)}>Все</option>
                            <option value='critical'{Selected(filter.Severity == MailruPostmasterAlertSeverity.Critical)}>Критично</option>
                            <option value='warning'{Selected(filter.Severity == MailruPostmasterAlertSeverity.Warning)}>Внимание</option>
                            <option value='info'{Selected(filter.Severity == MailruPostmasterAlertSeverity.Info)}>Информация</option>
                        </select>
                    </label>
                    <button type='submit'>Применить</button>
                    <a href='{DashboardPath}'>Сбросить</a>
                </form>
                {content}
            </section>
            """;
    }

    private static string RenderEvent(MailruPostmasterAlertJournalEvent journalEvent)
    {
        var (severityText, severityCss) = SeverityPresentation(journalEvent.Severity);
        var (statusText, statusCss) = StatusPresentation(journalEvent.Status);
        var period = journalEvent.DateFrom is null || journalEvent.DateTo is null
            ? "Без периода"
            : $"Период: {D(journalEvent.DateFrom.Value)} — {D(journalEvent.DateTo.Value)}";
        var resolved = journalEvent.ResolvedAt is null
            ? string.Empty
            : $"<span>Закрыт: {T(journalEvent.ResolvedAt.Value)}</span>";

        return $"""
            <article class='mailru-alert-journal-event {severityCss}'>
                <div class='mailru-alert-journal-event-head'>
                    <div>
                        <h4>{H(EventTitle(journalEvent.Code))}</h4>
                        <div class='mailru-alert-journal-code'>{H(journalEvent.Code)}</div>
                    </div>
                    <div class='mailru-alert-journal-badges'>
                        <span class='mailru-alert-journal-badge {statusCss}'>{statusText}</span>
                        <span class='mailru-alert-journal-badge {severityCss}'>{severityText}</span>
                    </div>
                </div>
                <div class='mailru-alert-journal-meta'>
                    <span>Категория: {H(CategoryTitle(journalEvent.Category))}</span>
                    <span>{period}</span>
                    <span>Первое наблюдение: {T(journalEvent.FirstObservedAt)}</span>
                    <span>Последнее наблюдение: {T(journalEvent.LastObservedAt)}</span>
                    {resolved}
                </div>
                <div class='mailru-alert-journal-values'>
                    <span>Срабатываний: {N(journalEvent.OccurrenceCount)}</span>
                    <span>Писем: {N(journalEvent.MessagesSent)}</span>
                    <span>Значение: {V(journalEvent.ObservedValue)}</span>
                    <span>Порог: {V(journalEvent.ThresholdValue)}</span>
                </div>
            </article>
            """;
    }

    private static MailruPostmasterAlertEventStatus? ParseStatus(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "active" => MailruPostmasterAlertEventStatus.Active,
            "resolved" => MailruPostmasterAlertEventStatus.Resolved,
            _ => null
        };

    private static MailruPostmasterAlertSeverity? ParseSeverity(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "critical" => MailruPostmasterAlertSeverity.Critical,
            "warning" => MailruPostmasterAlertSeverity.Warning,
            "info" => MailruPostmasterAlertSeverity.Info,
            _ => null
        };

    private static (string Text, string CssClass) SeverityPresentation(
        MailruPostmasterAlertSeverity severity) => severity switch
        {
            MailruPostmasterAlertSeverity.Critical => ("Критично", "critical"),
            MailruPostmasterAlertSeverity.Warning => ("Внимание", "warning"),
            _ => ("Информация", "info")
        };

    private static (string Text, string CssClass) StatusPresentation(
        MailruPostmasterAlertEventStatus status) => status switch
        {
            MailruPostmasterAlertEventStatus.Resolved => ("Закрыт", "resolved"),
            _ => ("Активен", "active")
        };

    private static string EventTitle(string code)
    {
        var normalized = code.Trim().ToLowerInvariant();
        if (normalized.StartsWith("authentication_trouble_", StringComparison.Ordinal))
        {
            return "Проблема аутентификации домена";
        }

        return normalized switch
        {
            "spam_detected" => "Mail.ru отметил письма как спам",
            "complaints_detected" => "Получены жалобы на рассылки",
            "probably_spam_high" => "Высокая доля писем в категории «возможно спам»",
            "probably_spam_growth" => "Выросла доля писем в категории «возможно спам»",
            "sync_stale" => "Данные Mail.ru давно не обновлялись",
            "sync_consecutive_failures" => "Повторяются ошибки синхронизации Mail.ru",
            "sync_waiting_first_success" => "Ожидается первая успешная синхронизация",
            _ => "Сигнал PM-5"
        };
    }

    private static string CategoryTitle(string category) => category.Trim().ToLowerInvariant() switch
    {
        "authentication" => "Аутентификация",
        "spam" => "Спам",
        "complaints" => "Жалобы",
        "probably_spam" => "Возможно спам",
        "synchronization" => "Синхронизация",
        _ => category
    };

    private static string Selected(bool selected) => selected ? " selected" : string.Empty;

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string D(DateOnly value) => value.ToString("dd.MM.yyyy", RussianCulture);

    private static string T(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("dd.MM.yyyy HH:mm 'UTC'", RussianCulture);

    private static string N(long value) => value.ToString("N0", RussianCulture);

    private static string V(double value) => value.ToString("0.###", RussianCulture);
}
