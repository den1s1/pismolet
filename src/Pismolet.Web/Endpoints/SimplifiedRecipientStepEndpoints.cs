using System.Collections;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class SimplifiedRecipientStepEndpoints
{
    private const int RecipientListLimit = 100;

    public static IEndpointRouteBuilder MapSimplifiedRecipientStepEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mailings/{id:guid}/recipients", Show).RequireAuthorization().WithOrder(-200);
        app.MapPost("/mailings/{id:guid}/recipients/add", AddRecipient).RequireAuthorization();
        app.MapPost("/mailings/{id:guid}/recipients/remove", RemoveRecipient).RequireAuthorization();
        return app;
    }

    private static IResult Show(Guid id, HttpContext http, IMailingService mailings)
    {
        var email = http.User.FindFirstValue(ClaimTypes.Email);
        var mailing = email is null ? null : mailings.GetForOwner(id, email);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        var replaceMode = string.Equals(http.Request.Query["mode"].ToString(), "replace", StringComparison.OrdinalIgnoreCase);
        var query = http.Request.Query["q"].ToString();
        var html = mailing.LastImportStats.TotalRows > 0 && !replaceMode
            ? ManagementPage(mailing, query)
            : UploadPage(mailing);
        return HtmlRenderer.Html(HtmlRenderer.Page("Адреса получателей", html, authenticated: true));
    }

    private static async Task<IResult> AddRecipient(Guid id, HttpContext http, IMailingService mailings, IRecipientImportService imports, IMailingDeclarationService declarations)
    {
        var form = await http.Request.ReadFormAsync();
        var newEmail = form["email"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(newEmail))
        {
            return Results.Redirect($"/mailings/{id}/recipients");
        }

        return await Reimport(id, http, mailings, imports, declarations, emails =>
        {
            if (!emails.Contains(newEmail, StringComparer.OrdinalIgnoreCase))
            {
                emails.Add(newEmail);
            }
        });
    }

    private static async Task<IResult> RemoveRecipient(Guid id, HttpContext http, IMailingService mailings, IRecipientImportService imports, IMailingDeclarationService declarations)
    {
        var form = await http.Request.ReadFormAsync();
        var removedEmail = form["email"].ToString().Trim();
        return await Reimport(id, http, mailings, imports, declarations, emails =>
            emails.RemoveAll(email => string.Equals(email, removedEmail, StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task<IResult> Reimport(Guid id, HttpContext http, IMailingService mailings, IRecipientImportService imports, IMailingDeclarationService declarations, Action<List<string>> change)
    {
        var ownerEmail = http.User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(ownerEmail))
        {
            return Results.Redirect("/dashboard");
        }

        var mailing = mailings.GetForOwner(id, ownerEmail);
        if (mailing is null)
        {
            return Results.Redirect("/dashboard");
        }

        var currentEmails = CurrentAcceptedEmails(mailing).ToList();
        change(currentEmails);
        var csv = "email\n" + string.Join('\n', currentEmails.Distinct(StringComparer.OrdinalIgnoreCase));
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        await imports.ImportAsync(new ImportRecipientsCommand(ownerEmail, id, "manual-addresses.csv", stream, Request(http)));
        var refreshed = mailings.GetForOwner(id, ownerEmail) ?? mailing;
        RecipientImportIssueStore.Save(refreshed);
        PreserveDeclaration(ownerEmail, id, refreshed, declarations, http);
        return Results.Redirect($"/mailings/{id}/recipients");
    }

    private static string ManagementPage(Mailing mailing, string query)
    {
        var declaration = DeclarationSummary(mailing);
        var rows = RecipientRows(mailing, query);
        return """
<section class='wizard-shell'>
  <div class='wizard-steps'><span class='wizard-step current'>1. Адреса</span><span class='wizard-step'>2. Письмо</span><span class='wizard-step'>3. Расчёт и оплата</span><span class='wizard-step'>4. Готово</span></div>
  <section class='panel'>
    <p class='eyebrow'>Шаг 1 из 4</p>
    <h1>1. Адреса загружены</h1>
    <p class='muted'>Вы остаетесь внутри этой рассылки. Загруженный список и подтверждения сохранены.</p>
    __STATS__
    <section class='inline-base-confirm'>
      <h2>Подтверждение базы</h2>
      __DECLARATION__
    </section>
    <section class='inline-base-confirm'>
      <div class='topline'><h2>Загруженные адреса</h2></div>
      <form method='get' action='/mailings/__ID__/recipients' class='row' style='gap:10px;align-items:end;margin-bottom:12px;'>
        <label style='flex:1;font-weight:400;'>Поиск по списку<input name='q' value='__QUERY__' placeholder='email или статус'></label>
        <button class='btn secondary'>Найти</button>
        <a class='btn ghost' href='/mailings/__ID__/recipients'>Сбросить</a>
      </form>
      <form method='post' action='/mailings/__ID__/recipients/add' class='row' style='gap:10px;align-items:end;margin-bottom:16px;'>
        <label style='flex:1;font-weight:400;'>Добавить адрес вручную<input name='email' type='email' placeholder='new@example.ru' required></label>
        <button class='button'>Добавить</button>
      </form>
      __ROWS__
    </section>
    <div class='actions wizard-actions'>
      <a class='button' href='/mailings/__ID__/message'>Перейти к письму</a>
      <a class='btn secondary' href='/mailings/__ID__/recipients?mode=replace'>Заменить список адресов</a>
      <a class='btn ghost' href='/dashboard'>Вернуться в ЛК</a>
    </div>
  </section>
</section>
"""
            .Replace("__ID__", mailing.Id.ToString(), StringComparison.Ordinal)
            .Replace("__QUERY__", H(query), StringComparison.Ordinal)
            .Replace("__STATS__", Stats(mailing), StringComparison.Ordinal)
            .Replace("__DECLARATION__", declaration, StringComparison.Ordinal)
            .Replace("__ROWS__", rows, StringComparison.Ordinal);
    }

    private static string UploadPage(Mailing mailing)
    {
        var sourceValue = DeclarationValue(mailing.Declaration, "BaseSource");
        var typeValue = DeclarationValue(mailing.Declaration, "IntendedMessageType")
            ?? DeclarationValue(mailing.Declaration, "MessageType")
            ?? mailing.MessageDraft?.MessageType.ToString()
            ?? MessageType.Transactional.ToString();
        var options = string.Join("", BaseSourceLabels.All.Select(x => Option(x.Key.ToString(), x.Value, sourceValue)));
        var stats = mailing.LastImportStats.TotalRows > 0 ? Stats(mailing) : string.Empty;
        var baseChecked = mailing.Declaration is null ? string.Empty : " checked";
        var adChecked = IsTrue(mailing.Declaration, "IsAdvertisingConsentConfirmed", "AdvertisingConsentConfirmed", "HasAdvertisingConsent") ? " checked" : string.Empty;
        var txSelected = typeValue == MessageType.Advertising.ToString() ? string.Empty : " selected";
        var adSelected = typeValue == MessageType.Advertising.ToString() ? " selected" : string.Empty;
        return """
<section class='wizard-shell'>
  <div class='wizard-steps'><span class='wizard-step current'>1. Адреса</span><span class='wizard-step'>2. Письмо</span><span class='wizard-step'>3. Расчёт и оплата</span><span class='wizard-step'>4. Готово</span></div>
  <section class='panel'>
    <p class='eyebrow'>Шаг 1 из 4</p>
    <h1>1. Добавьте список адресов</h1>
    <p class='muted'>Не используйте купленные или чужие базы.</p>
    <form method='post' action='/mailings/__ID__/recipients' enctype='multipart/form-data' class='simple-recipient-form'>
      <div class='wizard-grid'>
        <label class='dropzone'><span>Перетащите таблицу Excel сюда</span><small>или нажмите, чтобы выбрать файл.</small><input type='file' name='file' accept='.csv,.xlsx'></label>
        <label class='manual-addresses'><span>Или вставьте адреса вручную</span><small>Каждый адрес — с новой строки.</small><textarea name='manualAddresses' rows='12' placeholder='anna@example.ru&#10;club@example.ru&#10;ivan@example.ru'></textarea></label>
      </div>
      __STATS__
      <section class='inline-base-confirm'>
        <h2>Подтвердите базу</h2>
        <div class='compact-base-fields'>
          <label class='compact-base-field'><span>Источник базы</span><select name='baseSource' required><option value=''>Выберите источник</option>__OPTIONS__</select></label>
          <label class='compact-base-field'><span>Тип письма</span><select name='messageType' id='messageTypeSelect'><option value='Transactional'__TX__>Информационное</option><option value='Advertising'__AD__>Рекламное</option></select></label>
        </div>
        <label class='compact-base-check'><input type='checkbox' name='baseLegality'__BASE_CHECKED__><span>подтверждаю правомерность использования базы</span></label>
        <label class='compact-base-check compact-ad-consent' id='advertisingConsentBlock'><input type='checkbox' name='advertisingConsent'__AD_CHECKED__><span>подтверждаю наличие рекламного согласия адресатов</span></label>
        <p class='compact-legal-link'><a href='/legal/base-lawfulness?returnUrl=/mailings/__ID__/recipients'>Декларация законности базы</a></p>
      </section>
      <div class='actions wizard-actions'><button class='button'>Адреса добавлены, дальше</button><a class='btn secondary' href='/dashboard'>Вернуться в ЛК</a></div>
    </form>
  </section>
</section>
"""
            .Replace("__ID__", mailing.Id.ToString(), StringComparison.Ordinal)
            .Replace("__STATS__", stats, StringComparison.Ordinal)
            .Replace("__OPTIONS__", options, StringComparison.Ordinal)
            .Replace("__TX__", txSelected, StringComparison.Ordinal)
            .Replace("__AD__", adSelected, StringComparison.Ordinal)
            .Replace("__BASE_CHECKED__", baseChecked, StringComparison.Ordinal)
            .Replace("__AD_CHECKED__", adChecked, StringComparison.Ordinal);
    }

    private static string Stats(Mailing mailing)
    {
        var stats = mailing.LastImportStats;
        var blocked = stats.Invalid + stats.Duplicates + stats.GloballySuppressed + stats.ClientSuppressed;
        return $"<div class='stats import-summary'><div class='stat'><b>{stats.TotalRows}</b><span>Строк в файле</span></div><div class='stat'><b>{stats.Accepted}</b><span>Принято к отправке</span></div><div class='stat'><b>{stats.Duplicates + stats.Invalid}</b><span>Дублей и ошибок</span></div><div class='stat'><b>{blocked}</b><span>Не сможем отправить</span></div><div class='stat'><b>{stats.GloballySuppressed}</b><span>Ранее отписались</span></div></div>";
    }

    private static string DeclarationSummary(Mailing mailing)
    {
        var source = DeclarationValue(mailing.Declaration, "BaseSource");
        var sourceLabel = BaseSourceLabels.All.FirstOrDefault(x => string.Equals(x.Key.ToString(), source, StringComparison.OrdinalIgnoreCase)).Value ?? "не выбран";
        var type = DeclarationValue(mailing.Declaration, "IntendedMessageType")
            ?? DeclarationValue(mailing.Declaration, "MessageType")
            ?? mailing.MessageDraft?.MessageType.ToString()
            ?? MessageType.Transactional.ToString();
        var typeLabel = type == MessageType.Advertising.ToString() ? "Рекламное" : "Информационное";
        var baseStatus = mailing.Declaration is null ? "не подтверждена" : "подтверждена";
        var adStatus = IsTrue(mailing.Declaration, "IsAdvertisingConsentConfirmed", "AdvertisingConsentConfirmed", "HasAdvertisingConsent") ? "подтверждено" : "не требуется или не подтверждено";
        return $"<div class='compact-base-fields'><div class='box muted-box'><b>Источник базы</b><p>{H(sourceLabel)}</p></div><div class='box muted-box'><b>Тип письма</b><p>{H(typeLabel)}</p></div><div class='box muted-box'><b>Правомерность базы</b><p>{H(baseStatus)}</p></div><div class='box muted-box'><b>Рекламное согласие</b><p>{H(adStatus)}</p></div></div><p class='compact-legal-link'><a href='/legal/base-lawfulness?returnUrl=/mailings/{mailing.Id}/recipients'>Декларация законности базы</a></p>";
    }

    private static string RecipientRows(Mailing mailing, string query)
    {
        var allRows = RecipientDisplayRows(mailing).OrderBy(row => row.Order).ThenBy(row => row.FallbackOrder).ToList();
        if (!string.IsNullOrWhiteSpace(query))
        {
            allRows = allRows.Where(row => row.Email.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.Status.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.Source.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var visibleRows = allRows.Take(RecipientListLimit).ToList();
        if (visibleRows.Count == 0)
        {
            return "<p class='muted'>По этому запросу адресов не найдено.</p>";
        }

        var rows = string.Join("", visibleRows.Select(row => $"<tr><td>{H(row.Email)}</td><td>{H(row.Status)}</td><td>{H(row.Source)}</td><td>{ActionCell(mailing.Id, row)}</td></tr>"));
        var note = allRows.Count > RecipientListLimit
            ? $"<p class='muted'>Найдено {allRows.Count}, показано {RecipientListLimit}. Уточните поиск, чтобы быстрее найти нужный адрес.</p>"
            : $"<p class='muted'>Найдено адресов: {allRows.Count}.</p>";
        return $"<div class='table-wrap'><table><thead><tr><th>Email</th><th>Статус</th><th>Источник</th><th></th></tr></thead><tbody>{rows}</tbody></table></div>{note}";
    }

    private static string ActionCell(Guid mailingId, RecipientDisplayRow row) => row.CanRemove
        ? $"<form method='post' action='/mailings/{mailingId}/recipients/remove'><input type='hidden' name='email' value='{H(row.Email)}'><button class='btn ghost'>Удалить</button></form>"
        : "<span class='muted'>—</span>";

    private static IEnumerable<RecipientDisplayRow> RecipientDisplayRows(Mailing mailing)
    {
        var fallbackOrder = 0;
        foreach (var recipient in RecipientObjects(mailing))
        {
            fallbackOrder++;
            var email = Value(recipient, "Email", "Address") ?? "адрес";
            var status = StatusLabel(Value(recipient, "Status") ?? "Accepted");
            var rowNumber = IntValue(recipient, "RowNumber", "SourceRowNumber", "ImportRowNumber", "LineNumber") ?? fallbackOrder;
            yield return new RecipientDisplayRow(email, status, "Текущий список", rowNumber, fallbackOrder, CanRemove: true);
        }

        foreach (var issue in ImportIssueObjects(mailing))
        {
            fallbackOrder++;
            var source = issue.Message.Contains("Адрес не исключён", StringComparison.OrdinalIgnoreCase) ? "Предупреждение" : "Не сможем отправить";
            yield return new RecipientDisplayRow(issue.Email, issue.Message, source, issue.RowNumber, fallbackOrder, CanRemove: false);
        }
    }

    private static IEnumerable<string> CurrentAcceptedEmails(Mailing mailing) => RecipientObjects(mailing)
        .Select(recipient => Value(recipient, "Email", "Address"))
        .Where(email => !string.IsNullOrWhiteSpace(email))
        .Cast<string>();

    private static IEnumerable<object> RecipientObjects(Mailing mailing) =>
        mailing.GetType().GetProperty("Recipients", BindingFlags.Instance | BindingFlags.Public)?.GetValue(mailing) is IEnumerable recipients
            ? recipients.Cast<object>()
            : Enumerable.Empty<object>();

    private static IEnumerable<RecipientImportIssueSnapshot> ImportIssueObjects(Mailing mailing)
    {
        var liveIssues = mailing.LastImportBatch?.Issues
            .Select(issue => new RecipientImportIssueSnapshot(issue.RowNumber, issue.Email, issue.Message))
            .ToArray();
        return liveIssues is { Length: > 0 }
            ? liveIssues
            : RecipientImportIssueStore.Load(mailing.Id);
    }

    private static string StatusLabel(string status) => status switch
    {
        "Accepted" => "Принят к отправке",
        "Invalid" => "Некорректный адрес",
        "Duplicate" => "Дубль",
        "GloballySuppressed" => "Ранее отписался",
        "ClientSuppressed" => "Исключён клиентом",
        _ => status
    };

    private static void PreserveDeclaration(string ownerEmail, Guid mailingId, Mailing mailing, IMailingDeclarationService declarations, HttpContext http)
    {
        var source = TryParseBaseSource(DeclarationValue(mailing.Declaration, "BaseSource"));
        if (source is null) return;
        var type = TryParseMessageType(DeclarationValue(mailing.Declaration, "IntendedMessageType") ?? DeclarationValue(mailing.Declaration, "MessageType"));
        var advertisingConsent = IsTrue(mailing.Declaration, "IsAdvertisingConsentConfirmed", "AdvertisingConsentConfirmed", "HasAdvertisingConsent");
        declarations.Confirm(new ConfirmMailingDeclarationCommand(ownerEmail, mailingId, source, true, advertisingConsent, type, Request(http)));
    }

    private static string Option(string value, string label, string? selectedValue)
    {
        var selected = string.Equals(value, selectedValue, StringComparison.OrdinalIgnoreCase) ? " selected" : string.Empty;
        return $"<option value='{H(value)}'{selected}>{H(label)}</option>";
    }

    private static string? DeclarationValue(object? declaration, params string[] names)
    {
        if (declaration is null) return null;
        var type = declaration.GetType();
        foreach (var name in names)
        {
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            var value = property?.GetValue(declaration)?.ToString();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }

    private static bool IsTrue(object? declaration, params string[] names)
    {
        if (declaration is null) return false;
        var type = declaration.GetType();
        foreach (var name in names)
        {
            if (type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(declaration) is true) return true;
        }

        return false;
    }

    private static string? Value(object target, params string[] names)
    {
        var type = target.GetType();
        foreach (var name in names)
        {
            var value = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target)?.ToString();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }

    private static int? IntValue(object target, params string[] names)
    {
        var value = Value(target, names);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static BaseSource? TryParseBaseSource(string? value) => Enum.TryParse<BaseSource>(value, out var source) ? source : null;
    private static MessageType TryParseMessageType(string? value) => Enum.TryParse<MessageType>(value, out var type) ? type : MessageType.Transactional;
    private static RequestMetadata Request(HttpContext http) => new(http.Connection.RemoteIpAddress?.ToString() ?? "unknown", string.IsNullOrWhiteSpace(http.Request.Headers.UserAgent.ToString()) ? "unknown" : http.Request.Headers.UserAgent.ToString());
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private sealed record RecipientDisplayRow(string Email, string Status, string Source, int Order, int FallbackOrder, bool CanRemove);
}
