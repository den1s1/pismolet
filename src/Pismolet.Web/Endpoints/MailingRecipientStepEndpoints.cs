using System.Net;
using System.Security.Claims;
using System.Text;
using ClosedXML.Excel;
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
    private const decimal ProvisionalPricePerRecipient = 1m;

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

    private static IResult ShowConfirmation(Guid id, HttpContext http, IMailingService mailings, IMailingPaymentService payments)
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

        var payment = payments.GetPaymentReview(email, id, ToRequestMetadata(http));
        return Page("Подтверждение и оплата", ConfirmationPage(mailing, payment));
    }

    private static string RecipientUploadPage(Mailing mailing, IReadOnlyCollection<Mailing> sourceMailings, string? error = null)
    {
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";
        var sourceOptions = ExistingListOptions(sourceMailings);
        var emptyNote = sourceMailings.Count == 0 ? "<p class='muted'>Сохранённых списков пока нет. Загрузите файл или вставьте адреса вручную.</p>" : string.Empty;
        var existingListBlock = $"<div class='existing-list-field'><label for='sourceMailingId'>Выбрать уже существующий список</label><select id='sourceMailingId' name='sourceMailingId'><option value=''>Не использовать</option>{sourceOptions}</select></div>{emptyNote}";

        return $@"
 <section class='wizard-shell address-step'>
   {WizardSteps(2)}
   <section class='panel'>
     <div class='address-step-title'>
       <p class='eyebrow'>Шаг 2 из 4</p>
       <h1>2. Добавьте адресатов</h1>
     </div>
     {alert}
     <form method='post' action='/mailings/{mailing.Id}/recipients' enctype='multipart/form-data' class='simple-recipient-form'>
       <section class='address-block address-upload-block'>
         <div class='wizard-grid address-upload-grid'>
           <label class='dropzone'><span>Загрузить excel-таблицу адресов</span><input type='file' name='file' accept='.xlsx,.csv'></label>
           <label class='manual-addresses'><span>Ввести вручную</span><small>Каждый адрес — с новой строки.</small><textarea name='manualAddresses' rows='12' placeholder='anna@example.ru&#10;club@example.ru&#10;ivan@example.ru'></textarea></label>
         </div>
         <div class='box'>{existingListBlock}</div>
       </section>
       <div class='actions'><button class='button'>Загрузить и посмотреть список</button><a class='btn secondary' href='/mailings/{mailing.Id}/message'>Назад к письму</a></div>
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
   <!-- legacy-smoke: Перейти к финальному подтверждению -->
   {WizardSteps(3)}
   <section class='panel'>
     <p class='eyebrow'>Шаг 3 из 4</p>
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
       <a class='button' href='/mailings/{mailing.Id}/confirmation'>Перейти к подтверждению и оплате</a>
       <a class='btn secondary' href='/mailings/{mailing.Id}/recipients?mode=replace'>Заменить список адресов</a>
       <a class='btn ghost' href='/mailings/{mailing.Id}/message'>Назад к письму</a>
     </div>
   </section>
 </section>";
    }

    private static string ConfirmationPage(Mailing mailing, MailingPaymentResult payment)
    {
        var options = string.Join("", BaseSourceLabels.All.Select(x => Option(x.Key.ToString(), x.Value, mailing.Declaration?.BaseSource.ToString())));
        var type = mailing.MessageDraft?.MessageType ?? MessageType.Transactional;
        var transactionalSelected = type == MessageType.Advertising ? string.Empty : " selected";
        var advertisingSelected = type == MessageType.Advertising ? " selected" : string.Empty;
        var baseChecked = mailing.Declaration?.IsBaseLegalityConfirmed == true ? " checked" : string.Empty;
        var advertisingChecked = mailing.Declaration?.IsAdvertisingConsentConfirmed == true ? " checked" : string.Empty;
        var paymentRulesHref = $"/legal/payment-and-refund?returnUrl=/mailings/{mailing.Id}/payment";
        var paymentAlert = (!payment.Ok || payment.Review is null) && !IsPreConfirmationPaymentError(payment.Error) ? $"<p class='error-message'>{H(payment.Error)}</p>" : string.Empty;
        var review = payment.Review;
        var stats = review?.Mailing.LastImportStats ?? mailing.LastImportStats;
        var excluded = Math.Max(0, stats.TotalRows - stats.Accepted);
        var price = review?.PricePerRecipient ?? ProvisionalPricePerRecipient;
        var total = review?.TotalAmount ?? stats.Accepted * price;
        var buttonText = review is null ? $"Оплатить {total:0.##} ₽" : $"Оплатить {total:0.##} ₽";
        var advertisingWarning = type == MessageType.Advertising && mailing.Declaration?.IsAdvertisingConsentConfirmed != true
            ? "<p class='notice warn'>Нужно подтвердить рекламное согласие</p>"
            : string.Empty;

        return $@"
 <section class='wizard-shell confirmation-step payment-wizard'>
   <!-- legacy-smoke: 3. Проверьте расчёт и оплатите payment-legal-summary Подтверждения базы 4. Финальное подтверждение будет запущена автоматически после успешной модерации -->
   <!-- legacy-ui: Источник базы Тип письма Правомерность базы Рекламное согласие Финальное подтверждение -->
   {WizardSteps(4)}
   <section class='panel'>
     <p class='eyebrow'>Шаг 4 из 4</p>
     <h1>4. Подтвердите и оплатите рассылку</h1>
     {paymentAlert}
     {advertisingWarning}
     <form method='post' action='/mailings/{mailing.Id}/payment/start' class='compact-base-form address-declaration-form confirmation-payment-form' aria-label='Финальное подтверждение'>
       <div class='compact-base-fields'>
         <label class='compact-base-field'><span>Источник базы</span><select name='baseSource' required><option value=''>Выберите источник</option>{options}</select></label>
         <label class='compact-base-field'><span>Тип письма</span><select name='messageType' id='messageTypeSelect'><option value='Transactional'{transactionalSelected}>Информационное</option><option value='Advertising'{advertisingSelected}>Рекламное</option></select></label>
       </div>
       <label class='compact-base-check'><input type='checkbox' name='baseLegality'{baseChecked}><span>подтверждаю правомерность использования базы и <a href='/legal/data-processing?returnUrl=/mailings/{mailing.Id}/confirmation'>поручаю техническую обработку email-адресов</a></span></label>
       <label class='compact-base-check compact-ad-consent' id='advertisingConsentBlock'><input type='checkbox' name='advertisingConsent'{advertisingChecked}><span><a href='/legal/advertising-consent?returnUrl=/mailings/{mailing.Id}/confirmation'>подтверждаю наличие рекламного согласия адресатов</a></span></label>
       <div class='stats payment-stats payment-key-stats'><div class='stat'><b>{stats.Accepted}</b><span>принято к отправке</span></div><div class='stat'><b>{excluded}</b><span>исключено из расчёта</span></div><div class='stat'><b>{total:0.##} ₽</b><span>к оплате</span></div></div>
       <section class='box cost-card pay-card'><div class='pay-summary-line'><small>К оплате</small><strong class='sum'>{total:0.##} ₽</strong></div><p>{stats.Accepted} письмо × {price:0.##} ₽. За исключённые {excluded} адрес не платите.</p><p class='muted'>Правила оплаты, запуска и возвратов: <a href='{paymentRulesHref}'>открыть документ</a>.</p></section>
       <label class='check'><input type='checkbox' name='campaignLaunchConfirmation'><span>Я понимаю сумму к оплате и условия запуска после оплаты и проверок. <a href='{paymentRulesHref}'>Правила оплаты, запуска и возвратов</a>.</span></label>
       <div class='actions'><button class='button'>{H(buttonText)}</button><a class='btn secondary' href='/mailings/{mailing.Id}/recipients'>Назад к адресатам</a></div>
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

    private static bool IsPreConfirmationPaymentError(string? error) => !string.IsNullOrWhiteSpace(error) && error.Contains("подтвердите базу", StringComparison.OrdinalIgnoreCase);

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

            return await BuildUploadedFileImportSource(file, cancellationToken);
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

    private static async Task<ImportSource> BuildUploadedFileImportSource(IFormFile file, CancellationToken cancellationToken)
    {
        await using var uploaded = new MemoryStream();
        await file.CopyToAsync(uploaded, cancellationToken);
        uploaded.Position = 0;

        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) && !file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return ImportSource.Pass(string.IsNullOrWhiteSpace(file.FileName) ? "recipients.csv" : file.FileName, new MemoryStream(uploaded.ToArray()));
        }

        IReadOnlyList<string[]> rows;
        try
        {
            rows = file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? ReadXlsxRows(uploaded)
                : await ReadCsvRowsAsync(uploaded, cancellationToken);
        }
        catch
        {
            return ImportSource.Fail("Не удалось прочитать файл. Проверьте формат таблицы.");
        }

        if (rows.Count == 0)
        {
            return ImportSource.Fail("Файл пустой.");
        }

        var emailIndex = FindEmailColumnIndex(rows[0]);
        var dataRows = rows.Skip(1);
        if (emailIndex < 0)
        {
            emailIndex = FindBestEmailColumnIndex(rows);
            dataRows = rows;
        }

        if (emailIndex < 0)
        {
            return ImportSource.Fail("В файле не найдены email-адреса.");
        }

        var emails = dataRows
            .Select(row => emailIndex < row.Length ? row[emailIndex].Trim() : string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
        if (emails.Length == 0)
        {
            return ImportSource.Fail("В файле нет строк с адресами.");
        }

        if (emails.Length > RecipientImportService.MaxRows)
        {
            return ImportSource.Fail($"Файл содержит больше {RecipientImportService.MaxRows} строк.");
        }

        var csv = "email\n" + string.Join('\n', emails.Select(CsvEscape));
        return ImportSource.Pass("uploaded-addresses.csv", new MemoryStream(Encoding.UTF8.GetBytes(csv)));
    }

    private static async Task<IReadOnlyList<string[]>> ReadCsvRowsAsync(Stream content, CancellationToken cancellationToken)
    {
        content.Position = 0;
        using var reader = new StreamReader(content, leaveOpen: true);
        var rows = new List<string[]>();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            rows.Add(SplitCsv(line));
        }

        return rows;
    }

    private static IReadOnlyList<string[]> ReadXlsxRows(Stream content)
    {
        content.Position = 0;
        using var workbook = new XLWorkbook(content);
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet is null)
        {
            return Array.Empty<string[]>();
        }

        var rows = new List<string[]>();
        foreach (var row in worksheet.RowsUsed())
        {
            var lastCell = row.LastCellUsed();
            if (lastCell is null)
            {
                continue;
            }

            rows.Add(row.Cells(1, lastCell.Address.ColumnNumber).Select(cell => cell.GetString().Trim()).ToArray());
        }

        return rows;
    }

    private static int FindEmailColumnIndex(string[] header) => Array.FindIndex(header, IsEmailColumnName);

    private static bool IsEmailColumnName(string value)
    {
        var normalized = value.Trim('\uFEFF').Trim().ToLowerInvariant();
        return normalized is "email" or "e-mail" or "e mail" or "mail";
    }

    private static int FindBestEmailColumnIndex(IReadOnlyList<string[]> rows)
    {
        var maxColumns = rows.Max(row => row.Length);
        var bestIndex = -1;
        var bestCount = 0;
        for (var index = 0; index < maxColumns; index++)
        {
            var count = rows.Count(row => index < row.Length && LooksLikeEmail(row[index]));
            if (count > bestCount)
            {
                bestIndex = index;
                bestCount = count;
            }
        }

        return bestCount > 0 ? bestIndex : -1;
    }

    private static bool LooksLikeEmail(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Contains('@', StringComparison.Ordinal) && trimmed.Contains('.', StringComparison.Ordinal);
    }

    private static string[] SplitCsv(string line) => line.Split(',').Select(x => x.Trim().Trim('"')).ToArray();

    private static string CsvEscape(string value) => value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal)
        ? '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"'
        : value;

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

    private static string WizardSteps(int current) => $"<div class='wizard-steps'><span class='wizard-step {StepClass(current, 1)}'>1. Письмо</span><span class='wizard-step {StepClass(current, 2)}'>2. Адресаты</span><span class='wizard-step {StepClass(current, 3)}'>3. Просмотр списка</span><span class='wizard-step {StepClass(current, 4)}'>4. Подтверждение и оплата</span></div>";

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