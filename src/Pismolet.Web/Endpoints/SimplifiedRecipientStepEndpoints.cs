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
    private const int MaxManualAddressesChars = 1024 * 1024;

    public static IEndpointRouteBuilder MapSimplifiedRecipientStepEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mailings/{id:guid}/recipients", Show).RequireAuthorization().WithOrder(-200);
        app.MapPost("/mailings/{id:guid}/recipients", ImportRecipients).RequireAuthorization().WithOrder(-200);
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
        var html = (mailing.LastImportStats.TotalRows > 0 || mailing.Recipients.Count > 0) && !replaceMode
            ? ManagementPage(mailing, query)
            : UploadPage(mailing);
        return HtmlRenderer.Html(HtmlRenderer.Page("Адреса получателей", html, authenticated: true));
    }

    private static async Task<IResult> ImportRecipients(Guid id, HttpContext http, IMailingService mailings, IRecipientImportService imports, IMailingDeclarationService declarations, CancellationToken cancellationToken)
    {
        var ownerEmail = http.User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(ownerEmail))
        {
            return Results.Redirect("/account/login");
        }

        var mailing = mailings.GetForOwner(id, ownerEmail);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        var form = await http.Request.ReadFormAsync(cancellationToken);
        var source = await BuildImportSource(form, cancellationToken);
        if (!source.Ok)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Адреса получателей", UploadPage(mailing, source.Error), authenticated: true));
        }

        await using var stream = source.Content!;
        var importResult = await imports.ImportAsync(new ImportRecipientsCommand(ownerEmail, id, source.FileName, stream, Request(http)), cancellationToken);
        if (!importResult.Ok)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Адреса получателей", UploadPage(mailing, importResult.Error), authenticated: true));
        }

        var imported = importResult.Mailing ?? mailings.GetForOwner(id, ownerEmail) ?? mailing;
        if (!ContainsDeclarationInput(form))
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Адреса получателей", ManagementPage(imported, string.Empty), authenticated: true));
        }

        var declarationResult = ConfirmDeclaration(ownerEmail, id, imported, form, declarations, http);
        if (declarationResult.Ok)
        {
            return Results.Redirect($"/mailings/{id}/message");
        }

        var withDeclarationError = declarationResult.Mailing ?? mailings.GetForOwner(id, ownerEmail) ?? imported;
        return HtmlRenderer.Html(HtmlRenderer.Page("Адреса получателей", ManagementPage(withDeclarationError, string.Empty, declarationResult.Error), authenticated: true));
    }

    private static async Task<IResult> AddRecipient(Guid id, HttpContext http, IMailingService mailings, IRecipientImportService imports, IMailingDeclarationService declarations)
    {
        var form = await http.Request.ReadFormAsync();
        var newEmail = form["email"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(newEmail))
        {
            return Results.Redirect($"/mailings/{id}/recipients");
        }

        return await Reimport(id, http, mailings, imports, declarations, rows => rows.Add(new RecipientSourceRow(NextRowNumber(rows), newEmail)));
    }

    private static async Task<IResult> RemoveRecipient(Guid id, HttpContext http, IMailingService mailings, IRecipientImportService imports, IMailingDeclarationService declarations)
    {
        var form = await http.Request.ReadFormAsync();
        var removedEmail = form["email"].ToString().Trim();
        var rowNumber = int.TryParse(form["rowNumber"].ToString(), out var parsedRow) ? parsedRow : 0;
        return await Reimport(id, http, mailings, imports, declarations, rows =>
        {
            var index = rowNumber > 0
                ? rows.FindIndex(row => row.RowNumber == rowNumber)
                : rows.FindIndex(row => string.Equals(row.Email, removedEmail, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                rows.RemoveAt(index);
            }
        });
    }

    private static async Task<IResult> Reimport(Guid id, HttpContext http, IMailingService mailings, IRecipientImportService imports, IMailingDeclarationService declarations, Action<List<RecipientSourceRow>> change)
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

        var rows = CurrentSourceRows(mailing).ToList();
        change(rows);
        var csv = "email\n" + string.Join('\n', rows.Select(row => row.Email).Where(email => !string.IsNullOrWhiteSpace(email)));
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        await imports.ImportAsync(new ImportRecipientsCommand(ownerEmail, id, "manual-addresses.csv", stream, Request(http)));
        var refreshed = mailings.GetForOwner(id, ownerEmail) ?? mailing;
        PreserveDeclaration(ownerEmail, id, refreshed, declarations, http);
        return Results.Redirect($"/mailings/{id}/recipients");
    }

    private static string ManagementPage(Mailing mailing, string query, string? error = null)
    {
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";
        var declaration = DeclarationPanel(mailing);
        var rows = RecipientRows(mailing, query);
        return """
<section class='wizard-shell address-step'>
  <div class='wizard-steps'><span class='wizard-step current'>1. Адреса</span><span class='wizard-step'>2. Письмо</span><span class='wizard-step'>3. Расчёт и оплата</span><span class='wizard-step'>4. Готово</span></div>
  <section class='panel'>
    <p class='eyebrow'>Шаг 1 из 4</p>
    <h1>1. Адреса загружены</h1>
    <p class='muted'>Вы остаетесь внутри этой рассылки. Загруженный список и подтверждения сохранены.</p>
    __ALERT__
    <section class='address-block address-summary-block'>
      <div class='address-block-head'><div><h2>Сводка импорта</h2><p class='muted'>К оплате попадут только адреса со статусом «Принят к отправке».</p></div></div>
      __STATS__
    </section>
    <section class='address-block address-base-block'>
      <div class='address-block-head'><div><h2>Подтверждение базы</h2><p class='muted'>Источник базы и юридические подтверждения фиксируются перед переходом к письму.</p></div></div>
      __DECLARATION__
    </section>
    <section class='address-block address-list-block'>
      <div class='address-block-head'><div><h2>Управление адресами</h2><p class='muted'>Найдите адрес, добавьте новый вручную или удалите строку из текущего списка.</p></div></div>
      <form method='get' action='/mailings/__ID__/recipients' class='address-inline-form address-search-form'>
        <label class='address-inline-field'>Поиск по списку<input name='q' value='__QUERY__' placeholder='email или статус'></label>
        <button class='btn secondary compact'>Найти</button>
        <a class='control-link' href='/mailings/__ID__/recipients'>Сбросить</a>
      </form>
      <form method='post' action='/mailings/__ID__/recipients/add' class='address-inline-form address-add-form'>
        <label class='address-inline-field'>Добавить адрес вручную<input name='email' type='email' placeholder='new@example.ru' required></label>
        <button class='button compact'>Добавить</button>
      </form>
      __ROWS__
    </section>
    <div class='actions wizard-actions'>
      __NEXT_ACTION__
      <a class='btn secondary' href='/mailings/__ID__/recipients?mode=replace'>Заменить список адресов</a>
      <a class='btn ghost' href='/dashboard'>Вернуться в ЛК</a>
    </div>
  </section>
</section>
"""
            .Replace("__ID__", mailing.Id.ToString(), StringComparison.Ordinal)
            .Replace("__QUERY__", H(query), StringComparison.Ordinal)
            .Replace("__ALERT__", alert, StringComparison.Ordinal)
            .Replace("__STATS__", Stats(mailing), StringComparison.Ordinal)
            .Replace("__DECLARATION__", declaration, StringComparison.Ordinal)
            .Replace("__ROWS__", rows, StringComparison.Ordinal)
            .Replace("__NEXT_ACTION__", mailing.Declaration is null ? string.Empty : $"<a class='button' href='/mailings/{mailing.Id}/message'>Перейти к письму</a>", StringComparison.Ordinal);
    }

    private static string UploadPage(Mailing mailing, string? error = null)
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
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";
        return """
<section class='wizard-shell address-step'>
  <div class='wizard-steps'><span class='wizard-step current'>1. Адреса</span><span class='wizard-step'>2. Письмо</span><span class='wizard-step'>3. Расчёт и оплата</span><span class='wizard-step'>4. Готово</span></div>
  <section class='panel'>
    <p class='eyebrow'>Шаг 1 из 4</p>
    <h1>1. Добавьте список адресов</h1>
    <p class='muted'>Не используйте купленные или чужие базы. <a href='/legal/anti-spam?returnUrl=/mailings/__ID__/recipients'>Антиспам-политика</a></p>
    __ALERT__
    <form method='post' action='/mailings/__ID__/recipients' enctype='multipart/form-data' class='simple-recipient-form'>
      <section class='address-block address-upload-block'>
        <div class='address-block-head'><div><h2>Список получателей</h2><p class='muted'>Загрузите CSV/XLSX с колонкой email или вставьте адреса вручную.</p></div></div>
        <div class='wizard-grid address-upload-grid'>
          <label class='dropzone'><span>Перетащите таблицу Excel сюда</span><small>или нажмите, чтобы выбрать файл.</small><input type='file' name='file' accept='.csv,.xlsx'></label>
          <label class='manual-addresses'><span>Или вставьте адреса вручную</span><small>Каждый адрес — с новой строки.</small><textarea name='manualAddresses' rows='12' placeholder='anna@example.ru&#10;club@example.ru&#10;ivan@example.ru'></textarea></label>
        </div>
      </section>
      __STATS__
      <section class='address-block address-base-block'>
        <div class='address-block-head'><div><h2>Подтвердите базу</h2><p class='muted'>Без подтверждения источника и правомерности базы перейти к письму нельзя.</p></div></div>
        <div class='compact-base-fields'>
          <label class='compact-base-field'><span>Источник базы</span><select name='baseSource' required><option value=''>Выберите источник</option>__OPTIONS__</select></label>
          <label class='compact-base-field'><span>Тип письма</span><select name='messageType' id='messageTypeSelect'><option value='Transactional'__TX__>Информационное</option><option value='Advertising'__AD__>Рекламное</option></select></label>
        </div>
        <label class='compact-base-check'><input type='checkbox' name='baseLegality'__BASE_CHECKED__><span>подтверждаю правомерность использования базы и <a href='/legal/data-processing?returnUrl=/mailings/__ID__/recipients'>поручаю техническую обработку email-адресов</a></span></label>
        <label class='compact-base-check compact-ad-consent' id='advertisingConsentBlock'><input type='checkbox' name='advertisingConsent'__AD_CHECKED__><span><a href='/legal/advertising-consent?returnUrl=/mailings/__ID__/recipients'>подтверждаю наличие рекламного согласия адресатов</a></span></label>
        <p class='compact-legal-link'><a href='/legal/base-lawfulness?returnUrl=/mailings/__ID__/recipients'>Декларация законности базы</a></p>
      </section>
      <div class='actions wizard-actions'><button class='button'>Адреса добавлены, дальше</button><a class='btn secondary' href='/dashboard'>Вернуться в ЛК</a></div>
    </form>
  </section>
</section>
__SCRIPT__
"""
            .Replace("__ID__", mailing.Id.ToString(), StringComparison.Ordinal)
            .Replace("__ALERT__", alert, StringComparison.Ordinal)
            .Replace("__STATS__", stats, StringComparison.Ordinal)
            .Replace("__OPTIONS__", options, StringComparison.Ordinal)
            .Replace("__TX__", txSelected, StringComparison.Ordinal)
            .Replace("__AD__", adSelected, StringComparison.Ordinal)
            .Replace("__BASE_CHECKED__", baseChecked, StringComparison.Ordinal)
            .Replace("__AD_CHECKED__", adChecked, StringComparison.Ordinal)
            .Replace("__SCRIPT__", AdvertisingConsentScript(), StringComparison.Ordinal);
    }

    private static string Stats(Mailing mailing)
    {
        var stats = mailing.LastImportStats;
        var blocked = stats.Invalid + stats.Duplicates + stats.GloballySuppressed + stats.ClientSuppressed;
        return $"<div class='stats import-summary'><div class='stat'><b>{stats.TotalRows}</b><span>Строк в файле</span></div><div class='stat'><b>{stats.Accepted}</b><span>Принято к отправке</span></div><div class='stat'><b>{stats.Duplicates + stats.Invalid}</b><span>Дублей и ошибок</span></div><div class='stat'><b>{blocked}</b><span>Не сможем отправить</span></div><div class='stat'><b>{stats.GloballySuppressed}</b><span>Ранее отписались</span></div></div>";
    }

    private static string DeclarationPanel(Mailing mailing) => mailing.Declaration is null
        ? DeclarationForm(mailing)
        : DeclarationSummary(mailing);

    private static string DeclarationForm(Mailing mailing)
    {
        var options = string.Join("", BaseSourceLabels.All.Select(x => Option(x.Key.ToString(), x.Value, null)));
        return $"""
<form method='post' action='/mailings/{mailing.Id}/declaration' class='compact-base-form address-declaration-form'>
  <div class='compact-base-fields'>
    <label class='compact-base-field'><span>Источник базы</span><select name='baseSource' required><option value=''>Выберите источник</option>{options}</select></label>
    <label class='compact-base-field'><span>Тип письма</span><select name='messageType' id='messageTypeSelect'><option value='Transactional'>Информационное</option><option value='Advertising'>Рекламное</option></select></label>
  </div>
  <label class='compact-base-check'><input type='checkbox' name='baseLegality'><span>подтверждаю правомерность использования базы и <a href='/legal/data-processing?returnUrl=/mailings/{mailing.Id}/recipients'>поручаю техническую обработку email-адресов</a></span></label>
  <label class='compact-base-check compact-ad-consent' id='advertisingConsentBlock'><input type='checkbox' name='advertisingConsent'><span><a href='/legal/advertising-consent?returnUrl=/mailings/{mailing.Id}/recipients'>подтверждаю наличие рекламного согласия адресатов</a></span></label>
  <p class='compact-legal-link'><a href='/legal/base-lawfulness?returnUrl=/mailings/{mailing.Id}/recipients'>Декларация законности базы</a></p>
  <button class='button compact-base-submit'>Перейти к письму</button>
</form>
{AdvertisingConsentScript()}
""";
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
        return $"<div class='compact-base-fields address-base-summary'><div class='box muted-box'><b>Источник базы</b><p>{H(sourceLabel)}</p></div><div class='box muted-box'><b>Тип письма</b><p>{H(typeLabel)}</p></div><div class='box muted-box'><b>Правомерность базы</b><p>{H(baseStatus)}</p></div><div class='box muted-box'><b>Рекламное согласие</b><p>{H(adStatus)}</p></div></div><p class='compact-legal-link'><a href='/legal/base-lawfulness?returnUrl=/mailings/{mailing.Id}/recipients'>Декларация законности базы</a></p>";
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

    private static string ActionCell(Guid mailingId, RecipientDisplayRow row) =>
        $"<form method='post' action='/mailings/{mailingId}/recipients/remove'><input type='hidden' name='email' value='{H(row.Email)}'><input type='hidden' name='rowNumber' value='{row.Order}'><button class='btn ghost compact-action'>Удалить</button></form>";

    private static IEnumerable<RecipientDisplayRow> RecipientDisplayRows(Mailing mailing)
    {
        var warnings = (mailing.LastImportBatch?.Issues ?? Array.Empty<RecipientImportIssue>())
            .Where(IsWarningIssue)
            .GroupBy(issue => (issue.RowNumber, Email: issue.Email))
            .ToDictionary(group => group.Key, group => group.First().Message);
        var fallbackOrder = 0;

        foreach (var recipient in mailing.Recipients)
        {
            fallbackOrder++;
            var rowNumber = recipient.RowNumber > 0 ? recipient.RowNumber : fallbackOrder + 1;
            var email = recipient.Status == RecipientStatus.Accepted || string.IsNullOrWhiteSpace(recipient.SourceEmail)
                ? recipient.Email
                : recipient.SourceEmail;
            if (string.IsNullOrWhiteSpace(email))
            {
                email = recipient.SourceEmail;
            }
            var status = recipient.Status == RecipientStatus.Accepted
                ? "Принят к отправке"
                : recipient.ExclusionReason ?? StatusLabel(recipient.Status);
            var source = recipient.Status == RecipientStatus.Accepted ? "Текущий список" : "Не сможем отправить";

            if (recipient.Status == RecipientStatus.Accepted && warnings.TryGetValue((rowNumber, recipient.Email), out var warning))
            {
                status = $"{status}; предупреждение: {warning}";
                source = "Текущий список, есть предупреждение";
            }

            yield return new RecipientDisplayRow(email, status, source, rowNumber, fallbackOrder);
        }
    }

    private static IEnumerable<RecipientSourceRow> CurrentSourceRows(Mailing mailing)
    {
        var fallbackOrder = 0;
        foreach (var recipient in mailing.Recipients.OrderBy(x => x.RowNumber > 0 ? x.RowNumber : int.MaxValue))
        {
            fallbackOrder++;
            var email = string.IsNullOrWhiteSpace(recipient.SourceEmail) ? recipient.Email : recipient.SourceEmail;
            if (!string.IsNullOrWhiteSpace(email))
            {
                yield return new RecipientSourceRow(recipient.RowNumber > 0 ? recipient.RowNumber : fallbackOrder + 1, email);
            }
        }
    }

    private static async Task<ImportSourceInput> BuildImportSource(IFormCollection form, CancellationToken cancellationToken)
    {
        var file = form.Files.GetFile("file");
        if (file is { Length: > 0 })
        {
            var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);
            stream.Position = 0;
            return ImportSourceInput.Success(string.IsNullOrWhiteSpace(file.FileName) ? "recipients.csv" : file.FileName, stream);
        }

        var manual = form["manualAddresses"].ToString();
        if (string.IsNullOrWhiteSpace(manual))
        {
            return ImportSourceInput.Failure("Добавьте файл или вставьте адреса вручную.");
        }

        if (manual.Length > MaxManualAddressesChars)
        {
            return ImportSourceInput.Failure("Ручная вставка слишком большая. Загрузите CSV или XLSX-файл.");
        }

        var rows = manual.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(row => !string.IsNullOrWhiteSpace(row))
            .ToArray();
        if (rows.Length > RecipientImportService.MaxRows)
        {
            return ImportSourceInput.Failure($"Ручная вставка содержит больше {RecipientImportService.MaxRows} строк.");
        }

        var csv = "email\n" + string.Join('\n', rows);
        return ImportSourceInput.Success("manual-addresses.csv", new MemoryStream(Encoding.UTF8.GetBytes(csv)));
    }

    private static bool ContainsDeclarationInput(IFormCollection form) =>
        form.ContainsKey("baseSource") || form.ContainsKey("baseLegality") || form.ContainsKey("messageType") || form.ContainsKey("advertisingConsent");

    private static MailingDeclarationResult ConfirmDeclaration(string ownerEmail, Guid mailingId, Mailing mailing, IFormCollection form, IMailingDeclarationService declarations, HttpContext http)
    {
        if (mailing.LastImportStats.Accepted <= 0)
        {
            return MailingDeclarationResult.Failure("Нет адресов, принятых к отправке.", mailing);
        }

        var type = TryParseMessageType(form["messageType"].ToString());
        return declarations.Confirm(new ConfirmMailingDeclarationCommand(
            ownerEmail,
            mailingId,
            TryParseBaseSource(form["baseSource"].ToString()),
            form.ContainsKey("baseLegality"),
            form.ContainsKey("advertisingConsent"),
            type,
            Request(http)));
    }

    private static int NextRowNumber(IReadOnlyCollection<RecipientSourceRow> rows) => rows.Count == 0 ? 2 : rows.Max(row => row.RowNumber) + 1;

    private static bool IsWarningIssue(RecipientImportIssue issue) =>
        issue.Message.Contains("Адрес не исключён", StringComparison.OrdinalIgnoreCase);

    private static string StatusLabel(RecipientStatus status) => status switch
    {
        RecipientStatus.Accepted => "Принят к отправке",
        RecipientStatus.Invalid => "Некорректный адрес",
        RecipientStatus.Duplicate => "Дубль",
        RecipientStatus.GloballySuppressed => "Ранее отписался",
        RecipientStatus.ClientSuppressed => "Исключён клиентом",
        _ => status.ToString()
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

    private static BaseSource? TryParseBaseSource(string? value) => Enum.TryParse<BaseSource>(value, out var source) ? source : null;
    private static MessageType TryParseMessageType(string? value) => Enum.TryParse<MessageType>(value, out var type) ? type : MessageType.Transactional;
    private static RequestMetadata Request(HttpContext http) => new(http.Connection.RemoteIpAddress?.ToString() ?? "unknown", string.IsNullOrWhiteSpace(http.Request.Headers.UserAgent.ToString()) ? "unknown" : http.Request.Headers.UserAgent.ToString());
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    private static string AdvertisingConsentScript() => "<script class='compact-ad-consent-script'>(function(){var s=document.getElementById('messageTypeSelect');var b=document.getElementById('advertisingConsentBlock');if(!s||!b)return;function x(){b.style.display=s.value==='Advertising'?'flex':'none';}s.addEventListener('change',x);x();})();</script>";

    private sealed record RecipientSourceRow(int RowNumber, string Email);
    private sealed record RecipientDisplayRow(string Email, string Status, string Source, int Order, int FallbackOrder);
    private sealed record ImportSourceInput(bool Ok, string Error, string FileName, MemoryStream? Content)
    {
        public static ImportSourceInput Success(string fileName, MemoryStream content) => new(true, string.Empty, fileName, content);
        public static ImportSourceInput Failure(string error) => new(false, error, string.Empty, null);
    }
}
