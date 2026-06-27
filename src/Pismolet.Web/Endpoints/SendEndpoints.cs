using System.Net;
using System.Security.Claims;
using System.Text;
using ClosedXML.Excel;
using Microsoft.Extensions.Hosting;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class SendEndpoints
{
    private const int RecipientPageSize = 50;

    public static IEndpointRouteBuilder MapSendEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mailings/{id:guid}/send", ShowSend).RequireAuthorization();
        app.MapGet("/mailings/{id:guid}/send/export.csv", ExportCsv).RequireAuthorization();
        app.MapGet("/mailings/{id:guid}/send/export.xlsx", ExportXlsx).RequireAuthorization();
        app.MapPost("/mailings/{id:guid}/send/start", StartSend).RequireAuthorization();
        app.MapPost("/mailings/{id:guid}/send/resume", ResumeSend).RequireAuthorization();
        return app;
    }

    private static IResult ShowSend(Guid id, HttpContext http, IMailingSendService sender, IReplyEventRepository replies, IClickTrackingRepository clicks, IClientSuppressionRepository clientSuppressions, IEmailNormalizer emailNormalizer, IHostEnvironment environment)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var result = sender.GetState(email, id);
        var suppressionPreview = BuildClientSuppressionPreview(result, clientSuppressions, emailNormalizer);
        return HtmlRenderer.Html(HtmlRenderer.Page("Запуск рассылки", SendPage(result, replies.GetSummary(id), clicks.ListLinksByMailingId(id), suppressionPreview, null, environment.IsDevelopment(), ReadRecipientPage(http), IsRecipientPagingRequest(http)), authenticated: true));
    }

    private static IResult ExportCsv(Guid id, HttpContext http, IMailingSendService sender, IClickTrackingRepository clicks)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");

        var result = sender.GetState(email, id);
        if (result.State is null)
        {
            return Results.NotFound("Рассылка не найдена.");
        }

        var csv = BuildCsvReport(result.State, clicks.ListLinksByMailingId(id));
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(csv)).ToArray();
        var fileName = $"pismolet-mailing-{id:N}-report.csv";
        return Results.File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private static IResult ExportXlsx(Guid id, HttpContext http, IMailingSendService sender, IClickTrackingRepository clicks)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");

        var result = sender.GetState(email, id);
        if (result.State is null)
        {
            return Results.NotFound("Рассылка не найдена.");
        }

        var bytes = BuildXlsxReport(result.State, clicks.ListLinksByMailingId(id));
        var fileName = $"pismolet-mailing-{id:N}-report.xlsx";
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private static IResult StartSend(Guid id, HttpContext http, IMailingSendService sender, IReplyEventRepository replies, IClickTrackingRepository clicks, IClientSuppressionRepository clientSuppressions, IEmailNormalizer emailNormalizer, IHostEnvironment environment)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var result = sender.StartSending(email, id, ToRequestMetadata(http));
        var suppressionPreview = BuildClientSuppressionPreview(result, clientSuppressions, emailNormalizer);
        return HtmlRenderer.Html(HtmlRenderer.Page("Рассылка запущена", SendPage(result, replies.GetSummary(id), clicks.ListLinksByMailingId(id), suppressionPreview, result.Ok ? "Отправка поставлена в очередь." : result.Error, environment.IsDevelopment(), 1, false), authenticated: true));
    }

    private static IResult ResumeSend(Guid id, HttpContext http, IMailingSendService sender, IReplyEventRepository replies, IClickTrackingRepository clicks, IClientSuppressionRepository clientSuppressions, IEmailNormalizer emailNormalizer, IHostEnvironment environment)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var result = sender.ResumeSending(email, id, ToRequestMetadata(http));
        var suppressionPreview = BuildClientSuppressionPreview(result, clientSuppressions, emailNormalizer);
        return HtmlRenderer.Html(HtmlRenderer.Page("Рассылка запущена", SendPage(result, replies.GetSummary(id), clicks.ListLinksByMailingId(id), suppressionPreview, result.Ok ? "Продолжение отправки поставлено в очередь." : result.Error, environment.IsDevelopment(), 1, false), authenticated: true));
    }

    private static string SendPage(MailingSendResult result, ReplySummary replySummary, IReadOnlyCollection<TrackedLink> trackedLinks, ClientSuppressionPreview suppressionPreview, string? message, bool showDevReport, int recipientPage, bool keepDetailedReportOpen)
    {
        if (result.State is null)
        {
            return HtmlRenderer.Error(result.Error);
        }

        var state = result.State;
        var mailing = state.Mailing;
        var summary = state.Summary;
        var deliveredRecipients = CountDeliveryStatus(state.Events, "Delivered");
        var softBouncedRecipients = CountDeliveryStatus(state.Events, "SoftBounce");
        var hardBouncedRecipients = CountDeliveryStatus(state.Events, "HardBounce");
        var rejectedRecipients = CountDeliveryStatus(state.Events, "Rejected");
        var openedRecipients = state.Events.Count(x => x.FirstOpenedAt is not null);
        var clickedRecipients = trackedLinks
            .Where(x => x.FirstClickedAt is not null)
            .Select(x => x.RecipientEmail)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var launched = result.Ok && !string.IsNullOrWhiteSpace(message) && message.Contains("поставлен", StringComparison.OrdinalIgnoreCase);
        var title = launched ? "Рассылка запущена" : mailing.Status switch
        {
            MailingStatus.Approved => "Готово к запуску",
            MailingStatus.ReviewRequired => "Рассылка на модерации",
            MailingStatus.Sending => "Рассылка отправляется",
            MailingStatus.Sent => "Рассылка отправлена",
            MailingStatus.Paused => "Отправка приостановлена",
            MailingStatus.Failed => "Есть ошибки отправки",
            _ => "Отправка рассылки"
        };
        var statusBadge = mailing.Status == MailingStatus.Sent
            ? "<span class='badge ok'>Отправка завершена</span>"
            : $"<span class='badge warn'>{H(mailing.StatusRu)}</span>";
        var alert = string.IsNullOrWhiteSpace(message)
            ? string.Empty
            : result.Ok
                ? $"<p class='notice'>{H(message)}</p>"
                : $"<p class='error-message'>{H(message)}</p>";
        var pausedNote = PauseNote(state.Events);
        var action = mailing.Status switch
        {
            MailingStatus.Approved => $"<form method='post' action='/mailings/{mailing.Id}/send/start'><button class='button'>Запустить отправку</button></form>",
            MailingStatus.ReviewRequired => "<div class='launch-action-row'><button class='button' disabled>Запустить отправку</button><span class='badge warn'>Рассылка на модерации</span></div>",
            MailingStatus.PendingChecks => "<div class='launch-action-row'><button class='button' disabled>Запустить отправку</button><span class='badge warn'>Идёт проверка</span></div>",
            MailingStatus.Paid => "<div class='launch-action-row'><button class='button' disabled>Запустить отправку</button><span class='badge warn'>Ожидает проверки</span></div>",
            MailingStatus.Rejected => "<div class='launch-action-row'><button class='button' disabled>Запустить отправку</button><span class='badge danger'>Рассылка отклонена</span></div>",
            MailingStatus.Paused => $"<form method='post' action='/mailings/{mailing.Id}/send/resume'><button class='button'>Продолжить отправку</button></form>{pausedNote}",
            MailingStatus.Sending => $"<p><span class='badge warn'>Отправка выполняется</span></p><p><a class='button' href='/mailings/{mailing.Id}/send'>Обновить статус</a></p>",
            MailingStatus.Sent => string.Empty,
            MailingStatus.Failed => "<p><span class='badge danger'>Есть ошибки отправки</span></p><p class='muted'>Подробности ошибок доступны в подробном отчёте.</p>",
            _ => "<p class='muted'>Отправка будет доступна после оплаты и одобрения рассылки.</p>"
        };

        var deliveryNote = deliveredRecipients + softBouncedRecipients + hardBouncedRecipients + rejectedRecipients == 0
            ? "<p class='muted'>Ожидаем статус доставки от почтового сервера.</p>"
            : string.Empty;
        var openNote = openedRecipients == 0
            ? "<p class='muted'>Открытия появятся после загрузки картинок в письме. Метрика показывает открытие HTML-письма, а не гарантированное прочтение.</p>"
            : string.Empty;
        var clickNote = string.Empty;
        var replyStatus = replySummary.TotalReplies == 0
            ? "Ответов пока нет."
            : $"Получено ответов: {replySummary.TotalReplies}. Последний: {replySummary.LastReplyAt:yyyy-MM-dd HH:mm} UTC, статус: {H(replySummary.LastStatus?.ToRu() ?? "неизвестно")}";
        var paymentRulesHref = $"/legal/payment-and-refund?returnUrl=/mailings/{mailing.Id}/send";
        var replyRetentionHref = $"/legal/reply-retention?returnUrl=/mailings/{mailing.Id}/send";
        var sendErrors = summary.Failed + summary.ClientSuppressed + softBouncedRecipients + hardBouncedRecipients + rejectedRecipients;
        var progressText = LaunchProgressText(mailing.Status, summary, deliveredRecipients, sendErrors, replySummary.TotalReplies);

        var recipientRows = BuildRecipientRows(state, trackedLinks, suppressionPreview);
        var recipientPageCount = Math.Max(1, (int)Math.Ceiling(recipientRows.Count / (double)RecipientPageSize));
        var safeRecipientPage = Math.Clamp(recipientPage, 1, recipientPageCount);
        var recipientRowsHtml = recipientRows.Count == 0
            ? "<tr><td colspan='4'>Получателей пока нет.</td></tr>"
            : string.Join(string.Empty, recipientRows
                .Skip((safeRecipientPage - 1) * RecipientPageSize)
                .Take(RecipientPageSize)
                .Select(x => $"<tr><td>{H(x.Email)}</td><td>{H(x.Status)}</td><td>{x.ClickCount}</td><td>{H(x.SpamComplaint)}</td></tr>"));
        var recipientListNote = string.Empty;
        var recipientPager = RecipientPager(mailing.Id, safeRecipientPage, recipientPageCount);

        var devRows = state.Events.Count == 0
            ? "<tr><td colspan='8'>Событий отправки пока нет.</td></tr>"
            : string.Join(string.Empty, state.Events.OrderBy(x => x.RecipientEmail).Select(x => $"<tr><td>{H(x.RecipientEmail)}</td><td>{H(x.Status.ToRu())}</td><td>{H(x.DeliveryStatus.ToRu())}</td><td>{FormatDate(x.LastDeliveryEventAt)}</td><td>{(x.FirstOpenedAt is null ? "Нет" : "Да")}</td><td>{x.OpenCount}</td><td>{FormatDate(x.LastOpenedAt)}</td><td>{H(x.ErrorCode ?? "")}</td></tr>"));
        var clientSuppressionNotice = ClientSuppressionNoticeBlock(suppressionPreview);
        var devReport = showDevReport
            ? $"<details><summary>Dev-сводка событий</summary><div class='table-wrap'><table><thead><tr><th>Email</th><th>Статус</th><th>Доставка</th><th>Последнее событие доставки</th><th>Открыто</th><th>Открытий</th><th>Последнее открытие</th><th>Ошибка</th></tr></thead><tbody>{devRows}</tbody></table></div></details>"
            : string.Empty;
        var detailedReportOpenAttribute = keepDetailedReportOpen ? " open" : string.Empty;

        return $"""
            <section class='wizard-shell send-wizard'>
              <div class='wizard-steps' aria-label='Шаги создания рассылки'>
                <span class='wizard-step done'>Черновик</span>
                <span class='wizard-step done'>1. Адреса</span>
                <span class='wizard-step done'>2. Письмо</span>
                <span class='wizard-step done'>3. Проверка и оплата</span>
                <span class='wizard-step current'>Запуск</span>
              </div>
              <section class='panel'>
                <div class='topline'>
                  <div>
                    <h1>{H(title)}</h1>
                    <p class='muted'>{H(mailing.Subject)}</p>
                  </div>
                  {statusBadge}
                </div>
                {alert}
                <div class='notice warn'>Отправка идёт постепенно. Письмолёт ставит письма в очередь, соблюдает дневные лимиты и исключает отписавшихся получателей перед отправкой. <a href='{paymentRulesHref}'>Правила оплаты, запуска и возвратов</a>.</div>
                {clientSuppressionNotice}
                <div class='stats launch-stats launch-key-stats'>
                  <div class='stat'><b>{summary.TotalAcceptedRecipients}</b><span>Всего получателей</span></div>
                  <div class='stat'><b>{summary.Sent}</b><span>Отправлено</span></div>
                  <div class='stat'><b>{sendErrors}</b><span>Ошибки отправки</span></div>
                  <div class='stat'><b>{replySummary.TotalReplies}</b><span>Ответов</span></div>
                </div>
                <section class='box launch-main-card'>
                  <h2>Статус рассылки</h2>
                  <p>{H(progressText)}</p>
                  {action}
                </section>
                <details class='detailed-report'{detailedReportOpenAttribute}>
                  <summary>Подробный отчёт</summary>
                  <div class='report-grid'>
                    <section class='box muted-box'>
                      <h3>Сводка</h3>
                      {deliveryNote}
                      {openNote}
                      {clickNote}
                      <table><thead><tr><th>Показатель</th><th>Значение</th></tr></thead><tbody><tr><td>Всего получателей</td><td>{summary.TotalAcceptedRecipients}</td></tr><tr><td>Отправлено</td><td>{summary.Sent}</td></tr><tr><td>Ошибки отправки</td><td>{sendErrors}</td></tr><tr><td>Отписались</td><td>{summary.Suppressed}</td></tr><tr><td>Доставлено</td><td>{deliveredRecipients}</td></tr><tr><td>Открыто</td><td>{openedRecipients}</td></tr><tr><td>Открыли ссылку</td><td>{clickedRecipients}</td></tr><tr><td>Жалобы на спам</td><td>{summary.Complaints}</td></tr></tbody></table>
                      <h3>Ответы получателей</h3>
                      <p>{replyStatus}</p>
                      <p class='muted'>Ответы пересылаются на ваш email. Тут показаны только счётчик и статус пересылки. <a href='{replyRetentionHref}'>Правила хранения и удаления ответов</a>.</p>
                    </section>
                  </div>
                  <section id='recipient-list'>{recipientListNote}<div class='table-wrap'><table><thead><tr><th>Email</th><th>Статус</th><th>Переход по ссылкам</th><th>Жалоба на спам</th></tr></thead><tbody>{recipientRowsHtml}</tbody></table></div>{recipientPager}</section>
                  {devReport}
                </details>
                <div class='actions'><a class='btn secondary' href='/dashboard'>Вернуться в историю</a><a class='btn ghost' href='/mailings/{mailing.Id}'>Открыть карточку рассылки</a><a class='btn ghost' href='/mailings/{mailing.Id}/send/export.xlsx'>Скачать Excel-отчёт</a></div>
              </section>
            </section>
            """;
    }

    private static string BuildCsvReport(MailingSendState state, IReadOnlyCollection<TrackedLink> trackedLinks)
    {
        var csv = new StringBuilder();
        AppendCsvRow(csv, ReportHeaders);

        foreach (var row in BuildReportRows(state, trackedLinks))
        {
            AppendCsvRow(csv,
                row.Email,
                row.SendStatus,
                row.DeliveryStatus,
                row.OpenCount,
                row.FirstOpenedAt,
                row.LastOpenedAt,
                row.ClickCount,
                row.FirstClickedAt,
                row.LastClickedAt,
                row.DeliveryErrorReason);
        }

        return csv.ToString();
    }

    private static byte[] BuildXlsxReport(MailingSendState state, IReadOnlyCollection<TrackedLink> trackedLinks)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Отчёт");

        for (var column = 0; column < ReportHeaders.Length; column++)
        {
            worksheet.Cell(1, column + 1).Value = ReportHeaders[column];
        }

        var rowNumber = 2;
        foreach (var row in BuildReportRows(state, trackedLinks))
        {
            worksheet.Cell(rowNumber, 1).Value = row.Email;
            worksheet.Cell(rowNumber, 2).Value = row.SendStatus;
            worksheet.Cell(rowNumber, 3).Value = row.DeliveryStatus;
            worksheet.Cell(rowNumber, 4).Value = row.OpenCount;
            worksheet.Cell(rowNumber, 5).Value = row.FirstOpenedAt;
            worksheet.Cell(rowNumber, 6).Value = row.LastOpenedAt;
            worksheet.Cell(rowNumber, 7).Value = row.ClickCount;
            worksheet.Cell(rowNumber, 8).Value = row.FirstClickedAt;
            worksheet.Cell(rowNumber, 9).Value = row.LastClickedAt;
            worksheet.Cell(rowNumber, 10).Value = row.DeliveryErrorReason;
            rowNumber++;
        }

        var usedRange = worksheet.Range(1, 1, Math.Max(rowNumber - 1, 1), ReportHeaders.Length);
        usedRange.SetAutoFilter();
        worksheet.Row(1).Style.Font.Bold = true;
        worksheet.SheetView.FreezeRows(1);
        worksheet.Columns().AdjustToContents();
        worksheet.Column(10).Width = Math.Max(worksheet.Column(10).Width, 45);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static IReadOnlyCollection<ReportRow> BuildReportRows(MailingSendState state, IReadOnlyCollection<TrackedLink> trackedLinks)
    {
        var clickStats = trackedLinks
            .GroupBy(x => x.RecipientEmail, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => new RecipientClickStats(
                    x.Sum(y => y.ClickCount),
                    x.Where(y => y.FirstClickedAt is not null).Select(y => y.FirstClickedAt).OrderBy(y => y).FirstOrDefault(),
                    x.Where(y => y.LastClickedAt is not null).Select(y => y.LastClickedAt).OrderByDescending(y => y).FirstOrDefault()),
                StringComparer.OrdinalIgnoreCase);

        return state.Events
            .OrderBy(x => x.RecipientEmail, StringComparer.OrdinalIgnoreCase)
            .Select(sendEvent =>
            {
                clickStats.TryGetValue(sendEvent.RecipientEmail, out var clicks);
                return new ReportRow(
                    sendEvent.RecipientEmail,
                    sendEvent.Status.ToRu(),
                    sendEvent.DeliveryStatus.ToRu(),
                    sendEvent.OpenCount.ToString(),
                    FormatReportDate(sendEvent.FirstOpenedAt),
                    FormatReportDate(sendEvent.LastOpenedAt),
                    (clicks?.ClickCount ?? 0).ToString(),
                    FormatReportDate(clicks?.FirstClickedAt),
                    FormatReportDate(clicks?.LastClickedAt),
                    DeliveryErrorReason(sendEvent));
            })
            .ToArray();
    }

    private static IReadOnlyCollection<RecipientReportRow> BuildRecipientRows(MailingSendState state, IReadOnlyCollection<TrackedLink> trackedLinks, ClientSuppressionPreview suppressionPreview)
    {
        var clickCounts = trackedLinks
            .GroupBy(x => x.RecipientEmail, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.ClickCount), StringComparer.OrdinalIgnoreCase);
        var rowsByEmail = new Dictionary<string, RecipientReportRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var sendEvent in state.Events)
        {
            clickCounts.TryGetValue(sendEvent.RecipientEmail, out var clickCount);
            rowsByEmail[sendEvent.RecipientEmail] = new RecipientReportRow(
                sendEvent.RecipientEmail,
                RecipientStatusLabel(sendEvent),
                clickCount,
                IsSpamComplaint(sendEvent) ? "Да" : "Нет");
        }

        foreach (var suppression in suppressionPreview.Items)
        {
            if (rowsByEmail.ContainsKey(suppression.EmailNormalized))
            {
                continue;
            }

            clickCounts.TryGetValue(suppression.EmailNormalized, out var clickCount);
            rowsByEmail[suppression.EmailNormalized] = new RecipientReportRow(
                suppression.EmailNormalized,
                "Исключён",
                clickCount,
                "Нет");
        }

        return rowsByEmail.Values
            .OrderBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string RecipientStatusLabel(SendEvent sendEvent)
    {
        var sendStatus = sendEvent.Status.ToString();
        var deliveryStatus = sendEvent.DeliveryStatus.ToString();
        var reason = sendEvent.Reason?.ToString() ?? string.Empty;

        if (reason.Contains("Client", StringComparison.OrdinalIgnoreCase))
        {
            return "Исключён";
        }

        if (reason.Contains("Unsub", StringComparison.OrdinalIgnoreCase) || reason.Contains("Suppress", StringComparison.OrdinalIgnoreCase))
        {
            return "Отписался";
        }

        if (sendStatus is "Pending" or "Queued")
        {
            return "В очереди на отправку";
        }

        if (sendStatus == "Skipped")
        {
            return "Исключён";
        }

        if (sendStatus is "Failed" or "Paused" || deliveryStatus is "SoftBounce" or "HardBounce" or "Rejected" || IsSpamComplaint(sendEvent))
        {
            return "Ошибка";
        }

        if (sendEvent.FirstOpenedAt is not null)
        {
            return "Открыто";
        }

        if (deliveryStatus == "Delivered")
        {
            return "Доставлено";
        }

        if (sendStatus == "Accepted")
        {
            return "Отправлено";
        }

        return sendEvent.Status.ToRu();
    }

    private static bool IsSpamComplaint(SendEvent sendEvent) =>
        string.Equals(sendEvent.DeliveryStatus.ToString(), "Complaint", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(sendEvent.Reason?.ToString(), "Complaint", StringComparison.OrdinalIgnoreCase);

    private static string DeliveryErrorReason(SendEvent sendEvent)
    {
        var deliveryStatus = sendEvent.DeliveryStatus.ToString();
        var hasDeliveryProblem = deliveryStatus is "SoftBounce" or "HardBounce" or "Rejected";
        var hasSendProblem = sendEvent.Status is SendEventStatus.Failed or SendEventStatus.Skipped or SendEventStatus.Paused;
        if (!hasDeliveryProblem && !hasSendProblem)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(sendEvent.LastDeliverySummary)) return sendEvent.LastDeliverySummary;
        if (!string.IsNullOrWhiteSpace(sendEvent.ErrorMessage)) return sendEvent.ErrorMessage;
        if (!string.IsNullOrWhiteSpace(sendEvent.ErrorCode)) return sendEvent.ErrorCode;
        return sendEvent.Reason is null or SendSkipReason.None ? string.Empty : sendEvent.Reason.Value.ToString();
    }

    private static void AppendCsvRow(StringBuilder csv, params string?[] values)
    {
        csv.AppendLine(string.Join(',', values.Select(CsvCell)));
    }

    private static string CsvCell(string? value)
    {
        var text = value ?? string.Empty;
        return text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0
            ? text
            : $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private static ClientSuppressionPreview BuildClientSuppressionPreview(MailingSendResult result, IClientSuppressionRepository clientSuppressions, IEmailNormalizer emailNormalizer)
    {
        if (result.State is null)
        {
            return ClientSuppressionPreview.Empty;
        }

        var mailing = result.State.Mailing;
        var acceptedRecipients = mailing.Recipients
            .Where(x => x.Status == RecipientStatus.Accepted)
            .Select(x => emailNormalizer.Normalize(x.Email))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (acceptedRecipients.Length == 0)
        {
            return ClientSuppressionPreview.Empty;
        }

        var suppressedSet = clientSuppressions.GetSuppressedSet(mailing.OwnerEmail, acceptedRecipients);
        if (suppressedSet.Count == 0)
        {
            return ClientSuppressionPreview.Empty;
        }

        var recentByEmail = clientSuppressions.ListRecent(10000)
            .Where(x => string.Equals(x.ClientId, mailing.OwnerEmail, StringComparison.OrdinalIgnoreCase))
            .Where(x => suppressedSet.Contains(x.EmailNormalized))
            .GroupBy(x => x.EmailNormalized, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.LastSeenAt).First(), StringComparer.OrdinalIgnoreCase);

        var items = suppressedSet
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(email => recentByEmail.TryGetValue(email, out var suppression)
                ? new ClientSuppressionPreviewItem(email, suppression.Reason.ToString(), suppression.LastSeenAt, suppression.SourceProviderMessageId)
                : new ClientSuppressionPreviewItem(email, "ClientSuppression", null, null))
            .ToArray();

        return new ClientSuppressionPreview(items);
    }

    private static string ClientSuppressionNoticeBlock(ClientSuppressionPreview preview) => preview.Count == 0
        ? string.Empty
        : $"<div class='notice warn'>Перед отправкой Письмолёт исключит {preview.Count} адресов, по которым ранее были ошибки доставки у этого клиента.</div>";

    private static string ClientSuppressionReportBlock(ClientSuppressionPreview preview)
    {
        if (preview.Count == 0)
        {
            return string.Empty;
        }

        var rows = string.Join(string.Empty, preview.Items
            .Take(50)
            .Select(x => $"<tr><td>{H(x.EmailNormalized)}</td><td>{H(SuppressionReasonRu(x.Reason))}</td><td>{FormatDate(x.LastSeenAt)}</td><td>{H(x.SourceProviderMessageId ?? "-")}</td></tr>"));
        var hidden = preview.Count > 50
            ? $"<p class='muted'>Показаны первые 50 адресов из {preview.Count}.</p>"
            : string.Empty;

        return $"""
            <details>
              <summary>Исключения перед отправкой</summary>
              <p>Эти адреса находятся в suppression list клиента и не будут отправлены повторно. Обычно причина - постоянная ошибка доставки, например HardBounce.</p>
              <div class='table-wrap'><table><thead><tr><th>Email</th><th>Причина</th><th>Последний раз</th><th>ProviderMessageId</th></tr></thead><tbody>{rows}</tbody></table></div>
              {hidden}
            </details>
            """;
    }

    private static string PauseNote(IReadOnlyCollection<SendEvent> events)
    {
        var paused = events.Where(x => x.Status == SendEventStatus.Paused).ToArray();
        if (paused.Any(x => x.Reason == SendSkipReason.WarmupLimit) && paused.All(x => x.Reason != SendSkipReason.DailyLimit))
        {
            return "<p class='muted'>Отправка временно приостановлена из-за лимитов прогрева почты. Продолжение возможно позже, когда пройдёт минимальный интервал между письмами, или после изменения лимитов администратором.</p>";
        }

        return "<p class='muted'>Достигнут дневной лимит отправки. Продолжение возможно после смены дня или изменения лимита администратором.</p>";
    }

    private static string LaunchProgressText(MailingStatus status, MailingSendSummary summary, int deliveredRecipients, int notDelivered, int replies) => status switch
    {
        MailingStatus.Approved => $"Рассылка готова к запуску. К отправке подготовлено {summary.TotalAcceptedRecipients} писем.",
        MailingStatus.ReviewRequired => "Рассылка на модерации. Отправку можно будет запустить после одобрения.",
        MailingStatus.PendingChecks => "Письмолёт проверяет рассылку перед отправкой.",
        MailingStatus.Paid => "Оплата получена. Готовим рассылку к запуску.",
        MailingStatus.Rejected => "Рассылка отклонена. Исправьте письмо и отправьте его на проверку заново.",
        MailingStatus.Sending => $"Отправка идёт: отправлено {summary.Sent} из {summary.TotalAcceptedRecipients}. Ответов: {replies}.",
        MailingStatus.Sent => $"Отправка завершена. Отправлено {summary.Sent} писем, доставлено по отчётам {deliveredRecipients}, не удалось {notDelivered}.",
        MailingStatus.Paused => $"Отправка приостановлена: ожидает продолжения {summary.PausedByLimit} писем.",
        MailingStatus.Failed => $"Отправка завершилась с ошибками. Не удалось {notDelivered} писем.",
        _ => "Запуск станет доступен после оплаты и одобрения рассылки."
    };

    private static int CountDeliveryStatus(IEnumerable<SendEvent> events, string status) => events.Count(x => string.Equals(x.DeliveryStatus.ToString(), status, StringComparison.Ordinal));

    private static string SuppressionReasonRu(string reason) => reason switch
    {
        nameof(ClientSuppressionReason.HardBounce) => "Постоянная ошибка доставки (HardBounce)",
        nameof(ClientSuppressionReason.ManualBlock) => "Ручная блокировка",
        _ => "Исключён для этого клиента"
    };

    private static string ShortUrl(string url) => url.Length <= 80 ? url : url[..77] + "...";

    private static string ShortText(string? text) => string.IsNullOrWhiteSpace(text)
        ? ""
        : text.Length <= 160 ? text : text[..157] + "...";

    private static string FormatDate(DateTimeOffset? value) => value is null ? "-" : value.Value.ToString("yyyy-MM-dd HH:mm");

    private static string FormatReportDate(DateTimeOffset? value) => value is null ? string.Empty : value.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");

    private static int ReadRecipientPage(HttpContext http)
    {
        var rawValue = http.Request.Query["recipientPage"].FirstOrDefault();
        return int.TryParse(rawValue, out var page) && page > 0 ? page : 1;
    }

    private static bool IsRecipientPagingRequest(HttpContext http) => http.Request.Query.ContainsKey("recipientPage");

    private static string RecipientPager(Guid mailingId, int page, int totalPages)
    {
        if (totalPages <= 1)
        {
            return string.Empty;
        }

        var previous = page > 1
            ? $"<a class='btn ghost' href='/mailings/{mailingId}/send?recipientPage={page - 1}#recipient-list'>← Назад</a>"
            : string.Empty;
        var next = page < totalPages
            ? $"<a class='btn ghost' href='/mailings/{mailingId}/send?recipientPage={page + 1}#recipient-list'>Вперёд →</a>"
            : string.Empty;

        return $"<div class='actions'>{previous}<span class='muted'>Страница {page} из {totalPages}</span>{next}</div>";
    }

    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);

    private static RequestMetadata ToRequestMetadata(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = http.Request.Headers.UserAgent.ToString();
        return new RequestMetadata(ip, string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static readonly string[] ReportHeaders =
    {
        "Email",
        "Статус отправки",
        "Статус доставки",
        "Открытий",
        "Первое открытие UTC",
        "Последнее открытие UTC",
        "Кликов",
        "Первый клик UTC",
        "Последний клик UTC",
        "Причина ошибки доставки"
    };

    private sealed record ClientSuppressionPreview(IReadOnlyCollection<ClientSuppressionPreviewItem> Items)
    {
        public static ClientSuppressionPreview Empty { get; } = new(Array.Empty<ClientSuppressionPreviewItem>());

        public int Count => Items.Count;
    }

    private sealed record ClientSuppressionPreviewItem(string EmailNormalized, string Reason, DateTimeOffset? LastSeenAt, string? SourceProviderMessageId);

    private sealed record RecipientClickStats(int ClickCount, DateTimeOffset? FirstClickedAt, DateTimeOffset? LastClickedAt);

    private sealed record RecipientReportRow(string Email, string Status, int ClickCount, string SpamComplaint);

    private sealed record ReportRow(
        string Email,
        string SendStatus,
        string DeliveryStatus,
        string OpenCount,
        string FirstOpenedAt,
        string LastOpenedAt,
        string ClickCount,
        string FirstClickedAt,
        string LastClickedAt,
        string DeliveryErrorReason);
}
