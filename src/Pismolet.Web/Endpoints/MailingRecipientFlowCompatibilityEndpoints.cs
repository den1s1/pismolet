using System.Net;
using System.Security.Claims;
using System.Text;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class MailingRecipientFlowCompatibilityEndpoints
{
    private const int MaxUploadBytes = 1024 * 1024;

    public static IEndpointRouteBuilder MapMailingRecipientFlowCompatibilityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mailings/{id:guid}/recipients", ShowRecipients).RequireAuthorization().WithOrder(-3000);
        app.MapPost("/mailings/{id:guid}/recipients", ImportRecipients).RequireAuthorization().WithOrder(-3000);
        app.MapGet("/mailings/{id:guid}/confirmation", ShowConfirmation).RequireAuthorization().WithOrder(-3000);
        return app;
    }

    private static IResult ShowRecipients(Guid id, HttpContext http, IMailingService mailings)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var mailing = mailings.GetForOwner(id, email);
        if (mailing is null) return Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."));
        var replace = string.Equals(http.Request.Query["mode"].ToString(), "replace", StringComparison.OrdinalIgnoreCase);
        var html = (mailing.LastImportStats.TotalRows > 0 || mailing.Recipients.Count > 0) && !replace
            ? ReviewPage(mailing, http.Request.Query["q"].ToString())
            : UploadPage(mailing, mailings.ListForOwner(email));
        return Page("Адресаты", html);
    }

    private static async Task<IResult> ImportRecipients(Guid id, HttpContext http, IMailingService mailings, IRecipientImportService imports, IMailingDeclarationService declarations, CancellationToken ct)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var mailing = mailings.GetForOwner(id, email);
        if (mailing is null) return Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."));
        var form = await http.Request.ReadFormAsync(ct);
        var source = await BuildSource(form, mailings.ListForOwner(email), id, ct);
        if (!source.Ok) return Page("Адресаты", UploadPage(mailing, mailings.ListForOwner(email), source.Error));
        await using var stream = source.Content!;
        var imported = await imports.ImportAsync(new ImportRecipientsCommand(email, id, source.FileName, stream, Request(http)), ct);
        if (!imported.Ok) return Page("Адресаты", UploadPage(mailing, mailings.ListForOwner(email), imported.Error));
        var current = imported.Mailing ?? mailings.GetForOwner(id, email) ?? mailing;
        if (HasDeclarationFields(form))
        {
            var result = declarations.Confirm(new ConfirmMailingDeclarationCommand(email, id, ParseSource(form["baseSource"].ToString()), form.ContainsKey("baseLegality"), form.ContainsKey("advertisingConsent"), ParseType(form["messageType"].ToString()), Request(http)));
            return result.Ok ? Results.Redirect($"/mailings/{id}/message") : Page("Адресаты", ReviewPage(result.Mailing ?? current, string.Empty, result.Error));
        }
        return Page("Адресаты", ReviewPage(current, string.Empty));
    }

    private static IResult ShowConfirmation(Guid id, HttpContext http, IMailingService mailings)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        var mailing = mailings.GetForOwner(id, email);
        if (mailing is null) return Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."));
        if (mailing.LastImportStats.Accepted <= 0) return Results.Redirect($"/mailings/{id}/recipients");
        return Page("Финальное подтверждение", ConfirmationPage(mailing));
    }

    private static string UploadPage(Mailing mailing, IEnumerable<Mailing> allMailings, string? error = null)
    {
        var options = string.Join("", allMailings.Where(x => x.Id != mailing.Id && x.Recipients.Any(r => r.Status == RecipientStatus.Accepted)).Select(x => $"<option value='{x.Id}'>{H((string.IsNullOrWhiteSpace(x.MessageDraft?.Subject) ? x.Subject : x.MessageDraft.Subject) + " — " + x.LastImportStats.Accepted + " адресов")}</option>"));
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";
        return $@"<section class='wizard-shell address-step'>{Steps(2)}<section class='panel'><p class='eyebrow'>Шаг 2 из 5</p><h1>2. Добавьте адресатов</h1><p class='muted'>На этом шаге только формируем список. Подтверждение базы и тип письма будут на финальном экране.</p><p class='muted'>Не используйте купленные или чужие базы. <a href='/legal/anti-spam?returnUrl=/mailings/{mailing.Id}/recipients'>Антиспам-политика</a>. <a href='/legal/data-processing?returnUrl=/mailings/{mailing.Id}/recipients'>Техническая обработка email-адресов</a>. <a href='/legal/base-lawfulness?returnUrl=/mailings/{mailing.Id}/recipients'>Декларация законности базы</a>.</p>{alert}<form method='post' action='/mailings/{mailing.Id}/recipients' enctype='multipart/form-data' class='simple-recipient-form'><section class='address-block address-upload-block'><div class='address-block-head'><div><h2>Источник адресатов</h2><p class='muted'>Загрузите файл, вставьте адреса вручную или выберите уже существующий список.</p></div></div><div class='wizard-grid address-upload-grid'><label class='dropzone'><span>Загрузить CSV/XLSX</span><small>Файл с колонкой email.</small><input type='file' name='file' accept='.csv,.xlsx'></label><label class='manual-addresses'><span>Ввести вручную</span><small>Каждый адрес — с новой строки.</small><textarea name='manualAddresses' rows='12'></textarea></label></div><div class='box'><label>Выбрать уже существующий список <select name='sourceMailingId'><option value=''>Не использовать</option>{options}</select></label><p class='muted'>Сохранённых списков пока может не быть. Загрузите файл или вставьте адреса вручную.</p></div></section><div class='actions wizard-actions'><button class='button'>Загрузить и посмотреть список</button><a class='btn secondary' href='/mailings/{mailing.Id}/message'>Назад к письму</a></div></form></section></section>";
    }

    private static string ReviewPage(Mailing mailing, string query, string? error = null)
    {
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";
        return $@"<section class='wizard-shell address-step'>{Steps(3)}<section class='panel'><p class='eyebrow'>Шаг 3 из 5</p><h1>3. Проверьте список адресатов</h1><p class='muted'>К оплате попадут только адреса со статусом «Принят к отправке».</p>{alert}<section class='address-block address-summary-block'><h2>Сводка импорта</h2>{Stats(mailing)}{Warnings(mailing)}</section><section class='address-block address-list-block'><h2>Адресаты</h2><form method='get' action='/mailings/{mailing.Id}/recipients' class='address-inline-form address-search-form'><label class='address-inline-field'>Поиск по списку<input name='q' value='{H(query)}'></label><button class='btn secondary compact'>Найти</button></form><form method='post' action='/mailings/{mailing.Id}/recipients/add' class='address-inline-form address-add-form'><label class='address-inline-field'>Добавить адрес вручную<input name='email' type='email' required></label><button class='button compact'>Добавить</button></form>{Rows(mailing, query)}</section><div class='actions wizard-actions'><a class='button' href='/mailings/{mailing.Id}/confirmation'>Перейти к финальному подтверждению</a><a class='btn secondary' href='/mailings/{mailing.Id}/recipients?mode=replace'>Заменить список адресов</a><a class='btn ghost' href='/mailings/{mailing.Id}/message'>Назад к письму</a></div></section></section>";
    }

    private static string ConfirmationPage(Mailing mailing)
    {
        var options = string.Join("", BaseSourceLabels.All.Select(x => $"<option value='{x.Key}'>{H(x.Value)}</option>"));
        return $@"<section class='wizard-shell confirmation-step'>{Steps(4)}<section class='panel'><p class='eyebrow'>Шаг 4 из 5</p><h1>4. Финальное подтверждение</h1><p class='muted'>Здесь фиксируются источник базы, тип письма и юридически значимые подтверждения.</p><section class='box'><h2>Письмо</h2><p><b>{H(mailing.MessageDraft?.Subject ?? "Письмо ещё не заполнено")}</b></p></section><section class='box'><h2>Адресаты</h2>{Stats(mailing)}</section><form method='post' action='/mailings/{mailing.Id}/confirmation' class='compact-base-form address-declaration-form'><div class='compact-base-fields'><label class='compact-base-field'><span>Источник базы</span><select name='baseSource' required><option value=''>Выберите источник</option>{options}</select></label><label class='compact-base-field'><span>Тип письма</span><select name='messageType' id='messageTypeSelect'><option value='Transactional'>Информационное</option><option value='Advertising'>Рекламное</option></select></label></div><label class='compact-base-check'><input type='checkbox' name='baseLegality'><span>подтверждаю правомерность использования базы и <a href='/legal/data-processing?returnUrl=/mailings/{mailing.Id}/confirmation'>поручаю техническую обработку email-адресов</a></span></label><label class='compact-base-check compact-ad-consent' id='advertisingConsentBlock'><input type='checkbox' name='advertisingConsent'><span><a href='/legal/advertising-consent?returnUrl=/mailings/{mailing.Id}/confirmation'>подтверждаю наличие рекламного согласия адресатов</a></span></label><label class='check'><input type='checkbox' name='campaignLaunchConfirmation' required><span>Я проверил письмо и список адресатов, понимаю, что после оплаты рассылка уйдёт на проверку и будет запущена автоматически после успешной модерации.</span></label><div class='actions'><button class='button'>Подтвердить и перейти к оплате</button><a class='btn secondary' href='/mailings/{mailing.Id}/recipients'>Назад к адресатам</a></div></form></section></section>";
    }

    private static string Stats(Mailing m)
    {
        var s = m.LastImportStats;
        var blocked = s.Invalid + s.Duplicates + s.GloballySuppressed + s.ClientSuppressed;
        return $"<div class='stats import-summary'><div class='stat'><b>{s.TotalRows}</b><span>Строк в файле</span></div><div class='stat'><b>{s.Accepted}</b><span>Принято к отправке</span></div><div class='stat'><b>{s.Duplicates + s.Invalid}</b><span>Дублей и ошибок</span></div><div class='stat'><b>{blocked}</b><span>Не сможем отправить</span></div><div class='stat'><b>{s.GloballySuppressed}</b><span>Ранее отписались</span></div></div>";
    }

    private static string Warnings(Mailing m)
    {
        var warnings = (m.LastImportBatch?.Issues ?? Array.Empty<RecipientImportIssue>()).Where(x => x.Message.Contains("Адрес не исключён", StringComparison.OrdinalIgnoreCase)).Take(10).ToArray();
        return warnings.Length == 0 ? string.Empty : $"<section class='address-warning-block'><h2>Предупреждения</h2><ul class='issue-list'>{string.Join("", warnings.Select(x => $"<li><b>Строка {x.RowNumber}</b><span>{H(x.Email)}</span><em>{H(x.Message)}</em></li>"))}</ul></section>";
    }

    private static string Rows(Mailing m, string q)
    {
        var rows = m.Recipients.Select((r, i) => new { R = r, N = r.RowNumber > 0 ? r.RowNumber : i + 2 }).Where(x => string.IsNullOrWhiteSpace(q) || x.R.Email.Contains(q, StringComparison.OrdinalIgnoreCase) || (x.R.SourceEmail ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase)).Take(100).ToArray();
        if (rows.Length == 0) return "<p class='muted'>По этому запросу адресов не найдено.</p>";
        return "<div class='table-wrap'><table><thead><tr><th>Email</th><th>Статус</th><th>Источник</th><th></th></tr></thead><tbody>" + string.Join("", rows.Select(x => $"<tr><td>{H(x.R.Status == RecipientStatus.Accepted ? x.R.Email : x.R.SourceEmail ?? x.R.Email)}</td><td>{H(x.R.Status == RecipientStatus.Accepted ? "Принят к отправке" : x.R.ExclusionReason ?? x.R.Status.ToString())}</td><td>{(x.R.Status == RecipientStatus.Accepted ? "Текущий список" : "Не сможем отправить")}</td><td><form method='post' action='/mailings/{m.Id}/recipients/remove'><input type='hidden' name='email' value='{H(x.R.Email)}'><input type='hidden' name='rowNumber' value='{x.N}'><button class='btn ghost compact-action'>Удалить</button></form></td></tr>")) + "</tbody></table></div><p class='muted'>Найдено адресов: " + rows.Length + ".</p>";
    }

    private static async Task<ImportSource> BuildSource(IFormCollection form, IEnumerable<Mailing> mailings, Guid id, CancellationToken ct)
    {
        var file = form.Files.GetFile("file");
        if (file is { Length: > 0 })
        {
            if (file.Length > MaxUploadBytes) return ImportSource.Fail("Файл слишком большой для dev-среза.");
            var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct);
            stream.Position = 0;
            return ImportSource.Ok(string.IsNullOrWhiteSpace(file.FileName) ? "recipients.csv" : file.FileName, stream);
        }
        var manual = form["manualAddresses"].ToString();
        if (!string.IsNullOrWhiteSpace(manual))
        {
            if (Encoding.UTF8.GetByteCount(manual) > MaxUploadBytes) return ImportSource.Fail("Ручная вставка слишком большая. Загрузите CSV или XLSX-файл.");
            var rows = manual.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n', StringSplitOptions.TrimEntries).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            if (rows.Length > RecipientImportService.MaxRows) return ImportSource.Fail($"Ручная вставка содержит больше {RecipientImportService.MaxRows} строк.");
            return ImportSource.Ok("manual-addresses.csv", new MemoryStream(Encoding.UTF8.GetBytes("email\n" + string.Join('\n', rows))));
        }
        if (Guid.TryParse(form["sourceMailingId"].ToString(), out var sourceId) && sourceId != id)
        {
            var rows = mailings.FirstOrDefault(x => x.Id == sourceId)?.Recipients.Where(x => x.Status == RecipientStatus.Accepted).Select(x => string.IsNullOrWhiteSpace(x.SourceEmail) ? x.Email : x.SourceEmail).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();
            if (rows.Length == 0) return ImportSource.Fail("В выбранном списке нет адресов, принятых к отправке.");
            return ImportSource.Ok($"existing-list-{sourceId:N}.csv", new MemoryStream(Encoding.UTF8.GetBytes("email\n" + string.Join('\n', rows))));
        }
        return ImportSource.Fail("Загрузите файл, вставьте адреса вручную или выберите существующий список.");
    }

    private static IResult Page(string title, string body) => HtmlRenderer.Html(HtmlRenderer.Page(title, body, authenticated: true));
    private static bool HasDeclarationFields(IFormCollection f) => f.ContainsKey("baseSource") || f.ContainsKey("baseLegality") || f.ContainsKey("messageType") || f.ContainsKey("advertisingConsent");
    private static BaseSource? ParseSource(string? v) => Enum.TryParse<BaseSource>(v, out var source) ? source : null;
    private static MessageType ParseType(string? v) => Enum.TryParse<MessageType>(v, out var type) ? type : MessageType.Transactional;
    private static RequestMetadata Request(HttpContext http) => new(http.Connection.RemoteIpAddress?.ToString() ?? "unknown", string.IsNullOrWhiteSpace(http.Request.Headers.UserAgent.ToString()) ? "unknown" : http.Request.Headers.UserAgent.ToString());
    private static string Steps(int current) => $"<div class='wizard-steps'><span class='wizard-step {(current == 1 ? "current" : current > 1 ? "done" : string.Empty)}'>1. Письмо</span><span class='wizard-step {(current == 2 ? "current" : current > 2 ? "done" : string.Empty)}'>2. Адресаты</span><span class='wizard-step {(current == 3 ? "current" : current > 3 ? "done" : string.Empty)}'>3. Просмотр списка</span><span class='wizard-step {(current == 4 ? "current" : current > 4 ? "done" : string.Empty)}'>4. Подтверждение</span><span class='wizard-step {(current == 5 ? "current" : string.Empty)}'>5. Оплата</span></div>";
    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private sealed record ImportSource(bool Ok, string Error, string FileName, MemoryStream? Content)
    {
        public static ImportSource Ok(string fileName, MemoryStream content) => new(true, string.Empty, fileName, content);
        public static ImportSource Fail(string error) => new(false, error, string.Empty, null);
    }
}
