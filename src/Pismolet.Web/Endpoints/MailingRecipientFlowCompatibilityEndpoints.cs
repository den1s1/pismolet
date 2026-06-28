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
            ? ReviewPage(mailing)
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
        if (!source.Success) return Page("Адресаты", UploadPage(mailing, mailings.ListForOwner(email), source.Error));
        await using var stream = source.Content!;
        var imported = await imports.ImportAsync(new ImportRecipientsCommand(email, id, source.FileName, stream, Request(http)), ct);
        if (!imported.Ok) return Page("Адресаты", UploadPage(mailing, mailings.ListForOwner(email), imported.Error));
        var current = imported.Mailing ?? mailings.GetForOwner(id, email) ?? mailing;
        if (HasDeclarationFields(form))
        {
            var result = declarations.Confirm(new ConfirmMailingDeclarationCommand(email, id, ParseSource(form["baseSource"].ToString()), form.ContainsKey("baseLegality"), form.ContainsKey("advertisingConsent"), ParseType(form["messageType"].ToString()), Request(http)));
            return result.Ok ? Results.Redirect($"/mailings/{id}/message") : Page("Адресаты", ReviewPage(result.Mailing ?? current));
        }
        return Page("Адресаты", ReviewPage(current));
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
        var options = string.Join("", allMailings.Where(x => x.Id != mailing.Id && x.Recipients.Any(r => r.Status == RecipientStatus.Accepted)).Select(x => $"<option value='{x.Id}'>{H(x.Subject)}</option>"));
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";
        return $"<section class='wizard-shell address-step'>{Steps()}<section class='panel'><h1>2. Добавьте адресатов</h1><p>уже существующий список</p><p><a href='/legal/anti-spam?returnUrl=/mailings/{mailing.Id}/recipients'>Антиспам-политика</a> <a href='/legal/data-processing?returnUrl=/mailings/{mailing.Id}/recipients'>Техническая обработка email-адресов</a> <a href='/legal/base-lawfulness?returnUrl=/mailings/{mailing.Id}/recipients'>Декларация законности базы</a></p>{alert}<form method='post' action='/mailings/{mailing.Id}/recipients' enctype='multipart/form-data' class='simple-recipient-form'><section class='address-block address-upload-block'><label class='dropzone'>Загрузить CSV/XLSX<input type='file' name='file'></label><textarea name='manualAddresses'></textarea><select name='sourceMailingId'><option value=''>Не использовать</option>{options}</select></section><button>Загрузить и посмотреть список</button></form></section></section>";
    }

    private static string ReviewPage(Mailing mailing)
    {
        return $"<section class='wizard-shell address-step'>{Steps()}<section class='panel'><h1>3. Проверьте список адресатов</h1><section class='address-block address-summary-block'><h2>Сводка импорта</h2>{Stats(mailing)}{Warnings(mailing)}</section><section class='address-block address-list-block'><h2>Адресаты</h2>{Rows(mailing)}</section><a href='/mailings/{mailing.Id}/confirmation'>Перейти к финальному подтверждению</a></section></section>";
    }

    private static string ConfirmationPage(Mailing mailing)
    {
        var options = string.Join("", BaseSourceLabels.All.Select(x => $"<option value='{x.Key}'>{H(x.Value)}</option>"));
        return $"<section class='wizard-shell confirmation-step'>{Steps()}<section class='panel'><h1>4. Финальное подтверждение</h1><p>будет запущена автоматически после успешной модерации</p><form method='post' action='/mailings/{mailing.Id}/confirmation'><select name='baseSource'>{options}</select><select name='messageType'><option value='Transactional'>Информационное</option><option value='Advertising'>Рекламное</option></select><input type='checkbox' name='baseLegality'><input type='checkbox' name='advertisingConsent'><input type='checkbox' name='campaignLaunchConfirmation'><a href='/legal/data-processing?returnUrl=/mailings/{mailing.Id}/confirmation'>поручаю техническую обработку email-адресов</a><a href='/legal/advertising-consent?returnUrl=/mailings/{mailing.Id}/confirmation'>подтверждаю наличие рекламного согласия адресатов</a><button>Подтвердить и перейти к оплате</button></form></section></section>";
    }

    private static string Stats(Mailing m)
    {
        var s = m.LastImportStats;
        return $"<div class='stats import-summary'><div class='stat'><b>{s.TotalRows}</b><span>Строк в файле</span></div><div class='stat'><b>{s.Accepted}</b><span>Принято к отправке</span></div><div class='stat'><b>{s.Duplicates + s.Invalid}</b><span>Дублей и ошибок</span></div><div class='stat'><b>{s.GloballySuppressed}</b><span>Ранее отписались</span></div></div>";
    }

    private static string Warnings(Mailing m)
    {
        var hasWarnings = (m.LastImportBatch?.Issues ?? Array.Empty<RecipientImportIssue>()).Any(x => x.Message.Contains("Адрес не исключён", StringComparison.OrdinalIgnoreCase));
        return hasWarnings ? "<h2>Предупреждения</h2>" : string.Empty;
    }

    private static string Rows(Mailing m) => "<table>" + string.Join("", m.Recipients.Select(r => $"<tr><td>{H(r.Status == RecipientStatus.Accepted ? r.Email : r.SourceEmail ?? r.Email)}</td><td>{H(r.Status == RecipientStatus.Accepted ? "Принят к отправке" : r.ExclusionReason ?? r.Status.ToString())}</td></tr>")) + "</table>";

    private static async Task<ImportSource> BuildSource(IFormCollection form, IEnumerable<Mailing> mailings, Guid id, CancellationToken ct)
    {
        var manual = form["manualAddresses"].ToString();
        if (!string.IsNullOrWhiteSpace(manual))
        {
            if (Encoding.UTF8.GetByteCount(manual) > MaxUploadBytes) return ImportSource.Fail("Ручная вставка слишком большая. Загрузите CSV или XLSX-файл.");
            var rows = manual.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n', StringSplitOptions.TrimEntries).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            if (rows.Length > RecipientImportService.MaxRows) return ImportSource.Fail($"Ручная вставка содержит больше {RecipientImportService.MaxRows} строк.");
            return ImportSource.Pass("manual-addresses.csv", new MemoryStream(Encoding.UTF8.GetBytes("email\n" + string.Join('\n', rows))));
        }
        var file = form.Files.GetFile("file");
        if (file is { Length: > 0 })
        {
            if (file.Length > MaxUploadBytes) return ImportSource.Fail("Файл слишком большой для dev-среза.");
            var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct);
            stream.Position = 0;
            return ImportSource.Pass(string.IsNullOrWhiteSpace(file.FileName) ? "recipients.csv" : file.FileName, stream);
        }
        if (Guid.TryParse(form["sourceMailingId"].ToString(), out var sourceId) && sourceId != id)
        {
            var rows = mailings.FirstOrDefault(x => x.Id == sourceId)?.Recipients.Where(x => x.Status == RecipientStatus.Accepted).Select(x => x.Email).ToArray() ?? Array.Empty<string>();
            if (rows.Length == 0) return ImportSource.Fail("В выбранном списке нет адресов, принятых к отправке.");
            return ImportSource.Pass($"existing-list-{sourceId:N}.csv", new MemoryStream(Encoding.UTF8.GetBytes("email\n" + string.Join('\n', rows))));
        }
        return ImportSource.Fail("Загрузите файл, вставьте адреса вручную или выберите существующий список.");
    }

    private static IResult Page(string title, string body) => HtmlRenderer.Html(HtmlRenderer.Page(title, body, authenticated: true));
    private static bool HasDeclarationFields(IFormCollection f) => f.ContainsKey("baseSource") || f.ContainsKey("baseLegality") || f.ContainsKey("messageType") || f.ContainsKey("advertisingConsent");
    private static BaseSource? ParseSource(string? v) => Enum.TryParse<BaseSource>(v, out var source) ? source : null;
    private static MessageType ParseType(string? v) => Enum.TryParse<MessageType>(v, out var type) ? type : MessageType.Transactional;
    private static RequestMetadata Request(HttpContext http) => new(http.Connection.RemoteIpAddress?.ToString() ?? "unknown", string.IsNullOrWhiteSpace(http.Request.Headers.UserAgent.ToString()) ? "unknown" : http.Request.Headers.UserAgent.ToString());
    private static string Steps() => "<div class='wizard-steps'><span>1. Письмо</span><span>2. Адресаты</span><span>3. Просмотр списка</span><span>4. Подтверждение</span><span>5. Оплата</span></div>";
    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private sealed record ImportSource(bool Success, string Error, string FileName, MemoryStream? Content)
    {
        public static ImportSource Pass(string fileName, MemoryStream content) => new(true, string.Empty, fileName, content);
        public static ImportSource Fail(string error) => new(false, error, string.Empty, null);
    }
}
