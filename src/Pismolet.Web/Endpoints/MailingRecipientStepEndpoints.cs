using System.Net;
using System.Security.Claims;
using System.Text;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class MailingRecipientStepEndpoints
{
    private const int RecipientListLimit = 100;
    private const int MaxUploadBytes = 1024 * 1024;

    public static IEndpointRouteBuilder MapMailingRecipientStepEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mailings/{id:guid}/recipients", ShowRecipients)
            .RequireAuthorization()
            .WithOrder(-3000);
        app.MapPost("/mailings/{id:guid}/recipients", ImportRecipients)
            .RequireAuthorization()
            .WithOrder(-3000);
        app.MapGet("/mailings/{id:guid}/confirmation", ShowConfirmation)
            .RequireAuthorization()
            .WithOrder(-3000);
        return app;
    }

    private static IResult ShowRecipients(Guid id, HttpContext http, IMailingService mailings)
    {
        var email = CurrentEmail(http);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var mailing = mailings.GetForOwner(id, email);
        if (mailing is null)
        {
            return Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."));
        }

        var replaceMode = string.Equals(http.Request.Query["mode"].ToString(), "replace", StringComparison.OrdinalIgnoreCase);
        var query = http.Request.Query["q"].ToString();
        var allMailings = mailings.ListForOwner(email);
        var body = (mailing.LastImportStats.TotalRows > 0 || mailing.Recipients.Count > 0) && !replaceMode
            ? RecipientReviewPage(mailing, query)
            : RecipientUploadPage(mailing, ExistingRecipientSources(allMailings, id));
        return Page("Адресаты", body);
    }

    private static async Task<IResult> ImportRecipients(Guid id, HttpContext http, IMailingService mailings, IRecipientImportService imports, CancellationToken cancellationToken)
    {
        var email = CurrentEmail(http);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var mailing = mailings.GetForOwner(id, email);
        if (mailing is null)
        {
            return Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."));
        }

        var allMailings = mailings.ListForOwner(email);
        var form = await http.Request.ReadFormAsync(cancellationToken);
        var source = await BuildImportSource(form, allMailings, id, cancellationToken);
        if (!source.Success)
        {
            return Page("Адресаты", RecipientUploadPage(mailing, ExistingRecipientSources(allMailings, id), source.Error));
        }

        await using var stream = source.Content!;
        var importResult = await imports.ImportAsync(new ImportRecipientsCommand(email, id, source.FileName, stream, ToRequestMetadata(http)), cancellationToken);
        if (!importResult.Ok)
        {
            return Page("Адресаты", RecipientUploadPage(mailing, ExistingRecipientSources(allMailings, id), importResult.Error));
        }

        var current = importResult.Mailing ?? mailings.GetForOwner(id, email) ?? mailing;
        return Page("Адресаты", RecipientReviewPage(current, string.Empty));
    }

    private static IResult ShowConfirmation(Guid id, HttpContext http, IMailingService mailings)
    {
        var email = CurrentEmail(http);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var mailing = mailings.GetForOwner(id, email);
        if (mailing is null)
        {
            return Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."));
        }

        if (mailing.LastImportStats.Accepted <= 0)
        {
            return Results.Redirect($"/mailings/{id}/recipients");
        }

        return Page("Финальное подтверждение", ConfirmationPage(mailing));
    }

    private static string RecipientUploadPage(Mailing mailing, IReadOnlyCollection<Mailing> sourceMailings, string? error = null)
    {
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";
        var sourceOptions = ExistingListOptions(sourceMailings);
        var emptyNote = sourceMailings.Count == 0 ? "<p class='muted'>Сохранённых списков пока нет. Загрузите файл или вставьте адреса вручную.</p>" : string.Empty;
        var existingListBlock = $"<label>Выбрать уже существующий список <select name='sourceMailingId'><option value=''>Не использовать</option>{sourceOptions}</select><span class='field-hint'>Адреса будут скопированы в эту рассылку как snapshot.</span></label>{emptyNote}";

        return $@"
<section class='wizard-shell address-step'>
  {WizardSteps(2)}
  <section class='panel'>
    <p class='eyebrow'>Шаг 2 из 5</p>
    <h1>2. Добавьте адресатов</h1>
    <p class='muted'>На этом шаге только формируем список. Подтверждение базы и тип письма будут на финальном экране.</p>
    <p class='muted'>Не используйте купленные или чужие базы. <a href='/legal/anti-spam?returnUrl=/mailings/{mailing.Id}/recipients'>Антиспам-политика</a>. <a href='/legal/data-processing?returnUrl=/mailings/{mailing.Id}/recipients'>Техническая обработка email-адресов</a>. <a href='/legal/base-lawfulness?returnUrl=/mailings/{mailing.Id}/recipients'>Декларация законности базы</a>.</p>
    {alert}
    <form method='post' action='/mailings/{mailing.Id}/recipients' enctype='multipart/form-data' class='simple-recipient-form'>
      <section class='address-block address-upload-block'>
        <div class='address-block-head'><div><h2>Источник адресатов</h2><p class='muted'>Загрузите файл, вставьте адреса вручную или выберите уже существующий список.</p></div></div>
        <div class='wizard-grid address-upload-grid'>
          <label class='dropzone'><span>Загрузить CSV/XLSX</span><small>Файл с колонкой email.</small><input type='file' name='file' accept='.csv,.xlsx'></label>
          <label class='manual-addresses'><span>Ввести вручную</span><small>Каждый адрес — с новой строки.</small><textarea name='manualAddresses' rows='12' placeholder='anna@example.ru&#10;club@example.ru&#10;ivan@example.ru'></textarea></label>
        </div>
        <div class='box'>{existingListBlock}</div>
      </section>
      <div class='actions wizard-actions'><button class='button'>Загрузить и посмотреть список</button><a class='btn secondary' href='/mailings/{mailing.Id}/message'>Назад к письму</a></div>
    </form>
  </section>
</section>";
    }

    private static string RecipientReviewPage(Mailing mailing, string query, string? error = null)
    {
        var rows = RecipientRows(mailing, query);
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";
        return $@"
<section class='wizard-shell address-step'>
  {WizardSteps(3)}
  <section class='panel'>
    <p class='eyebrow'>Шаг 3 из 5</p>
    <h1>3. Проверьте список адресатов</h1>
    <p class='muted'>К оплате попадут только адреса со статусом «Принят к отправке».</p>
    {alert}
    <section class='address-block address-summary-block'>
      <div class='address-block-head'><div><h2>Сводка импорта</h2><p class='muted'>Ошибки, дубли и отписавшиеся адреса исключаются из оплаты и отправки.</p></div></div>
      {Stats(mailing)}
      {WarningsBlock(mailing)}
    </section>
    <section class='address-block address-list-block'>
      <div class='address-block-head'><div><h2>Адресаты</h2><p class='muted'>Можно найти адрес, добавить новый вручную или удалить строку из текущего списка.</p></div></div>
      <form method='get' action='/mailings/{mailing.Id}/recipients' class='address-inline-form address-search-form'>
        <label class='address-inline-field'>Поиск по списку<input name='q' value='{H(query)}' placeholder='email или статус'></label>
        <button class='btn secondary compact'>Найти</button>
        <a class='control-link' href='/mailings/{mailing.Id}/recipients'>Сбросить</a>
      </form>
      <form method='post' action='/mailings/{mailing.Id}/recipients/add' class='address-inline-form address-add-form'>
        <label class='address-inline-field'>Добавить адрес вручную<input name='email' type='email' placeholder='new@example.ru' required></label>
        <button class='button compact'>Добавить</button>
      </form>
      {rows}
    </section>
    <div class='actions wizard-actions'>
      <a class='button' href='/mailings/{mailing.Id}/confirmation'>Перейти к финальному подтверждению</a>
      <a class='btn secondary' href='/mailings/{mailing.Id}/recipients?mode=replace'>Заменить список адресов</a>
      <a class='btn ghost' href='/mailings/{mailing.Id}/message'>Назад к письму</a>
    </div>
  </section>
</section>";
    }

    private static string ConfirmationPage(Mailing mailing)
    {
        var draft = mailing.MessageDraft;
        var options = string.Join("", BaseSourceLabels.All.Select(x => Option(x.Key.ToString(), x.Value, mailing.Declaration?.BaseSource.ToString())));
        var type = draft?.MessageType ?? MessageType.Transactional;
        var transactionalSelected = type == MessageType.Advertising ? string.Empty : " selected";
        var advertisingSelected = type == MessageType.Advertising ? " selected" : string.Empty;
        var baseChecked = mailing.Declaration?.IsBaseLegalityConfirmed == true ? " checked" : string.Empty;
        var advertisingChecked = mailing.Declaration?.IsAdvertisingConsentConfirmed == true ? " checked" : string.Empty;

        return $@"
<section class='wizard-shell confirmation-step'>
  {WizardSteps(4)}
  <section class='panel'>
    <p class='eyebrow'>Шаг 4 из 5</p>
    <h1>4. Финальное подтверждение</h1>
    <p class='muted'>Здесь фиксируются источник базы, тип письма и юридически значимые подтверждения. Рассылка будет запущена автоматически после успешной модерации.</p>
    <section class='box'><h2>Письмо</h2><p><b>{H(draft?.Subject ?? "Письмо ещё не заполнено")}</b></p><p class='muted'>Отправитель: {H(draft?.SenderName ?? string.Empty)}</p></section>
    <section class='box'><h2>Адресаты</h2>{Stats(mailing)}</section>
    <form method='post' action='/mailings/{mailing.Id}/confirmation' class='compact-base-form address-declaration-form'>
      <div class='compact-base-fields'>
        <label class='compact-base-field'><span>Источник базы</span><select name='baseSource' required><option value=''>Выберите источник</option>{options}</select></label>
        <label class='compact-base-field'><span>Тип письма</span><select name='messageType' id='messageTypeSelect'><option value='Transactional'{transactionalSelected}>Информационное</option><option value='Advertising'{advertisingSelected}>Рекламное</option></select></label>
      </div>
      <label class='compact-base-check'><input type='checkbox' name='baseLegality'{baseChecked}><span>подтверждаю правомерность использования базы и <a href='/legal/data-processing?returnUrl=/mailings/{mailing.Id}/confirmation'>поручаю техническую обработку email-адресов</a></span></label>
      <p class='muted'><a href='/legal/base-lawfulness?returnUrl=/mailings/{mailing.Id}/confirmation'>Подробнее о законности базы</a></p>
      <label class='compact-base-check compact-ad-consent' id='advertisingConsentBlock'><input type='checkbox' name='advertisingConsent'{advertisingChecked}><span><a href='/legal/advertising-consent?returnUrl=/mailings/{mailing.Id}/confirmation'>подтверждаю наличие рекламного согласия адресатов</a></span></label>
      <label class='check'><input type='checkbox' name='campaignLaunchConfirmation' required><span>Я проверил письмо и список адресатов, понимаю, что после оплаты рассылка уйдёт на проверку и будет запущена автоматически после успешной модерации.</span></label>
      <div class='actions'><button class='button'>Подтвердить и перейти к оплате</button><a class='btn secondary' href='/mailings/{mailing.Id}/recipients'>Назад к адресатам</a></div>
    </form>
  </section>
</section>";
    }

    private static string Stats(Mailing mailing)
    {
        var stats = mailing.LastImportStats;
        var blocked = stats.Invalid + stats.Duplicates + stats.GloballySuppressed + stats.ClientSuppressed;
        return $"<div class='stats import-summary'><div class='stat'><b>{stats.TotalRows}</b><span>Строк в файле</span></div><div class='stat'><b>{stats.Accepted}</b><span>Принято к отправке</span></div><div class='stat'><b>{stats.Duplicates + stats.Invalid}</b><span>Дублей и ошибок</span></div><div class='stat'><b>{blocked}</b><span>Не сможем отправить</span></div><div class='stat'><b>{stats.GloballySuppressed}</b><span>Ранее отписались</span></div></div>";
    }

    private static string WarningsBlock(Mailing mailing)
    {
        var warnings = (mailing.LastImportBatch?.Issues ?? Array.Empty<RecipientImportIssue>())
            .Where(IsWarningIssue)
            .Take(10)
            .ToArray();
        return warnings.Length == 0
            ? string.Empty
            : $"<section class='address-warning-block'><h2>Предупреждения</h2>{IssueBlock(warnings)}</section>";
    }

    private static string IssueBlock(IReadOnlyCollection<RecipientImportIssue> issues)
    {
        var rows = string.Join("", issues.Select(issue => $"<li><b>Строка {issue.RowNumber}</b><span>{H(issue.Email)}</span><em>{H(issue.Message)}</em></li>"));
        return $"<ul class='issue-list'>{rows}</ul>";
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

    private static async Task<ImportSource> BuildImportSource(IFormCollection form, IEnumerable<Mailing> allMailings, Guid currentMailingId, CancellationToken cancellationToken)
    {
        var file = form.Files.GetFile("file");
        if (file is { Length: > 0 })
        {
            if (file.Length > MaxUploadBytes)
            {
                return ImportSource.Fail("Файл слишком большой для dev-среза.");
            }

            var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);
            stream.Position = 0;
            return ImportSource.Pass(string.IsNullOrWhiteSpace(file.FileName) ? "recipients.csv" : file.FileName, stream);
        }

        var manual = form["manualAddresses"].ToString();
        if (!string.IsNullOrWhiteSpace(manual))
        {
            if (Encoding.UTF8.GetByteCount(manual) > MaxUploadBytes)
            {
                return ImportSource.Fail("Ручная вставка слишком большая. Загрузите CSV или XLSX-файл.");
            }

            var rows = manual.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n', StringSplitOptions.TrimEntries).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            if (rows.Length > RecipientImportService.MaxRows)
            {
                return ImportSource.Fail($"Ручная вставка содержит больше {RecipientImportService.MaxRows} строк.");
            }

            return ImportSource.Pass("manual-addresses.csv", new MemoryStream(Encoding.UTF8.GetBytes("email\n" + string.Join('\n', rows))));
        }

        if (Guid.TryParse(form["sourceMailingId"].ToString(), out var sourceMailingId) && sourceMailingId != currentMailingId)
        {
            var rows = allMailings.FirstOrDefault(x => x.Id == sourceMailingId)?.Recipients
                .Where(x => x.Status == RecipientStatus.Accepted)
                .Select(x => string.IsNullOrWhiteSpace(x.SourceEmail) ? x.Email : x.SourceEmail)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();
            if (rows.Length == 0)
            {
                return ImportSource.Fail("В выбранном списке нет адресов, принятых к отправке.");
            }

            return ImportSource.Pass($"existing-list-{sourceMailingId:N}.csv", new MemoryStream(Encoding.UTF8.GetBytes("email\n" + string.Join('\n', rows))));
        }

        return ImportSource.Fail("Загрузите файл, вставьте адреса вручную или выберите существующий список.");
    }

    private static bool IsWarningIssue(RecipientImportIssue issue) => issue.Message.Contains("Адрес не исключён", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyCollection<Mailing> ExistingRecipientSources(IEnumerable<Mailing> mailings, Guid currentMailingId) => mailings
        .Where(x => x.Id != currentMailingId && x.Recipients.Any(r => r.Status == RecipientStatus.Accepted))
        .OrderByDescending(x => x.CreatedAt)
        .Take(25)
        .ToArray();

    private static string ExistingListOptions(IEnumerable<Mailing> sourceMailings) => string.Join("", sourceMailings.Select(mailing =>
    {
        var title = string.IsNullOrWhiteSpace(mailing.MessageDraft?.Subject) ? mailing.Subject : mailing.MessageDraft.Subject;
        var label = $"{title} — {mailing.LastImportStats.Accepted} адресов — {mailing.CreatedAt:yyyy-MM-dd}";
        return $"<option value='{mailing.Id}'>{H(label)}</option>";
    }));

    private static string Option(string value, string label, string? selectedValue)
    {
        var selected = string.Equals(value, selectedValue, StringComparison.OrdinalIgnoreCase) ? " selected" : string.Empty;
        return $"<option value='{H(value)}'{selected}>{H(label)}</option>";
    }

    private static string ActionCell(Guid mailingId, RecipientDisplayRow row) =>
        $"<form method='post' action='/mailings/{mailingId}/recipients/remove'><input type='hidden' name='email' value='{H(row.Email)}'><input type='hidden' name='rowNumber' value='{row.Order}'><button class='btn ghost compact-action'>Удалить</button></form>";

    private static string StatusLabel(RecipientStatus status) => status switch
    {
        RecipientStatus.Accepted => "Принят к отправке",
        RecipientStatus.Invalid => "Некорректный адрес",
        RecipientStatus.Duplicate => "Дубль",
        RecipientStatus.GloballySuppressed => "Ранее отписался",
        RecipientStatus.ClientSuppressed => "Исключён клиентом",
        _ => status.ToString()
    };

    private static RequestMetadata ToRequestMetadata(HttpContext http) => new(http.Connection.RemoteIpAddress?.ToString() ?? "unknown", string.IsNullOrWhiteSpace(http.Request.Headers.UserAgent.ToString()) ? "unknown" : http.Request.Headers.UserAgent.ToString());

    private static IResult Page(string title, string body) => HtmlRenderer.Html(HtmlRenderer.Page(title, body, authenticated: true));

    private static string WizardSteps(int current) => $"<div class='wizard-steps'><span class='wizard-step {StepClass(current, 1)}'>1. Письмо</span><span class='wizard-step {StepClass(current, 2)}'>2. Адресаты</span><span class='wizard-step {StepClass(current, 3)}'>3. Просмотр списка</span><span class='wizard-step {StepClass(current, 4)}'>4. Подтверждение</span><span class='wizard-step {StepClass(current, 5)}'>5. Оплата</span></div>";

    private static string StepClass(int current, int step) => current == step ? "current" : current > step ? "done" : string.Empty;

    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private sealed record RecipientDisplayRow(string Email, string Status, string Source, int Order, int FallbackOrder);

    private sealed record ImportSource(bool Success, string Error, string FileName, MemoryStream? Content)
    {
        public static ImportSource Pass(string fileName, MemoryStream content) => new(true, string.Empty, fileName, content);
        public static ImportSource Fail(string error) => new(false, error, string.Empty, null);
    }
}
