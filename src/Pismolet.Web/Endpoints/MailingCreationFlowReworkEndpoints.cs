using System.Net;
using System.Security.Claims;
using System.Text;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class MailingCreationFlowReworkEndpoints
{
    private const string InitialMailingSubject = "Новая рассылка";
    private const int RecipientListLimit = 100;
    private const int MaxRecipientUploadBytes = 1024 * 1024;
    private const int MaxManualAddressBytes = MaxRecipientUploadBytes;

    public static IEndpointRouteBuilder MapMailingCreationFlowReworkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mailings/new", StartNewMailing).RequireAuthorization().WithOrder(-1000);
        app.MapPost("/mailings", CreateMailing).RequireAuthorization().WithOrder(-1000);
        app.MapGet("/mailings/{id:guid}/message", ShowMessage).RequireAuthorization().WithOrder(-1000);
        app.MapPost("/mailings/{id:guid}/message", SaveMessage).RequireAuthorization().WithOrder(-1000);
        app.MapGet("/mailings/{id:guid}/recipients", ShowRecipients).RequireAuthorization().WithOrder(-1000);
        app.MapPost("/mailings/{id:guid}/recipients", ImportRecipients).RequireAuthorization().WithOrder(-1000);
        app.MapGet("/mailings/{id:guid}/confirmation", ShowConfirmation).RequireAuthorization().WithOrder(-1000);
        app.MapPost("/mailings/{id:guid}/confirmation", ConfirmAndContinueToPayment).RequireAuthorization().WithOrder(-1000);
        return app;
    }

    private static IResult StartNewMailing(HttpContext http, IMailingService mailings)
    {
        var email = CurrentEmail(http);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var result = mailings.CreateDraft(new CreateMailingCommand(email, InitialMailingSubject), ToRequestMetadata(http));
        if (!result.Ok || result.Mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error(result.Error), authenticated: true));
        }

        return Results.Redirect($"/mailings/{result.Mailing.Id}/message");
    }

    private static async Task<IResult> CreateMailing(HttpContext http, IMailingService mailings)
    {
        var email = CurrentEmail(http);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var form = await http.Request.ReadFormAsync();
        var subject = form["subject"].ToString();
        if (string.IsNullOrWhiteSpace(subject))
        {
            subject = InitialMailingSubject;
        }

        var result = mailings.CreateDraft(new CreateMailingCommand(email, subject), ToRequestMetadata(http));
        if (!result.Ok || result.Mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error(result.Error), authenticated: true));
        }

        return Results.Redirect($"/mailings/{result.Mailing.Id}/message");
    }

    private static IResult ShowMessage(Guid id, HttpContext http, IMailingService mailings)
    {
        var mailing = GetMailing(id, http, mailings);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Письмо", MessagePage(mailing), authenticated: true));
    }

    private static async Task<IResult> SaveMessage(Guid id, HttpContext http, IMailingService mailings, IMailingMessageService messages)
    {
        var email = CurrentEmail(http);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var mailing = mailings.GetForOwner(id, email);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        var form = await http.Request.ReadFormAsync();
        var senderName = form["senderName"].ToString();
        var subject = form["subject"].ToString();
        var body = form["body"].ToString();
        var bodyFormat = string.Equals(form["bodyFormat"].ToString(), "html", StringComparison.OrdinalIgnoreCase)
            ? MessageBodyFormat.Html
            : MessageBodyFormat.Text;

        var result = messages.Save(new SaveMailingMessageCommand(
            email,
            id,
            senderName,
            subject,
            body,
            mailing.MessageDraft?.MessageType ?? MessageType.Transactional,
            ToRequestMetadata(http),
            mailing.MessageDraft?.Attachments,
            bodyFormat));

        if (!result.Ok)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Письмо", MessagePage(mailing, result.Error, senderName, subject, body, bodyFormat), authenticated: true));
        }

        var updated = result.Mailing ?? mailings.GetForOwner(id, email) ?? mailing;
        if (updated.LastImportStats.Accepted > 0 && updated.Declaration is not null)
        {
            return Results.Redirect($"/mailings/{id}/payment");
        }

        if (updated.LastImportStats.Accepted > 0)
        {
            return Results.Redirect($"/mailings/{id}/confirmation");
        }

        return Results.Redirect($"/mailings/{id}/recipients");
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
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        var allMailings = mailings.ListForOwner(email);
        var replaceMode = string.Equals(http.Request.Query["mode"].ToString(), "replace", StringComparison.OrdinalIgnoreCase);
        var query = http.Request.Query["q"].ToString();
        var body = (mailing.LastImportStats.TotalRows > 0 || mailing.Recipients.Count > 0) && !replaceMode
            ? RecipientReviewPage(mailing, query)
            : RecipientUploadPage(mailing, ExistingRecipientSources(allMailings, id));
        return HtmlRenderer.Html(HtmlRenderer.Page("Адресаты", body, authenticated: true));
    }

    private static async Task<IResult> ImportRecipients(Guid id, HttpContext http, IMailingService mailings, IRecipientImportService imports, CancellationToken cancellationToken)
    {
        var ownerEmail = CurrentEmail(http);
        if (ownerEmail is null)
        {
            return Results.Redirect("/account/login");
        }

        var mailing = mailings.GetForOwner(id, ownerEmail);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        var form = await http.Request.ReadFormAsync(cancellationToken);
        var allMailings = mailings.ListForOwner(ownerEmail);
        var source = await BuildImportSource(form, allMailings, id, cancellationToken);
        if (!source.Ok)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Адресаты", RecipientUploadPage(mailing, ExistingRecipientSources(allMailings, id), source.Error), authenticated: true));
        }

        await using var stream = source.Content!;
        var importResult = await imports.ImportAsync(new ImportRecipientsCommand(ownerEmail, id, source.FileName, stream, ToRequestMetadata(http)), cancellationToken);
        if (!importResult.Ok)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Адресаты", RecipientUploadPage(mailing, ExistingRecipientSources(allMailings, id), importResult.Error), authenticated: true));
        }

        return Results.Redirect($"/mailings/{id}/recipients");
    }

    private static IResult ShowConfirmation(Guid id, HttpContext http, IMailingService mailings)
    {
        var mailing = GetMailing(id, http, mailings);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        if (mailing.MessageDraft is null)
        {
            return Results.Redirect($"/mailings/{id}/message");
        }

        if (mailing.LastImportStats.Accepted <= 0)
        {
            return Results.Redirect($"/mailings/{id}/recipients");
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Финальное подтверждение", ConfirmationPage(mailing), authenticated: true));
    }

    private static async Task<IResult> ConfirmAndContinueToPayment(Guid id, HttpContext http, IMailingService mailings, IMailingDeclarationService declarations, IMailingMessageService messages)
    {
        var email = CurrentEmail(http);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var mailing = mailings.GetForOwner(id, email);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        var form = await http.Request.ReadFormAsync();
        var messageType = TryParseMessageType(form["messageType"].ToString());
        var declarationResult = declarations.Confirm(new ConfirmMailingDeclarationCommand(
            email,
            id,
            TryParseBaseSource(form["baseSource"].ToString()),
            form.ContainsKey("baseLegality"),
            form.ContainsKey("advertisingConsent"),
            messageType,
            ToRequestMetadata(http)));

        if (!declarationResult.Ok || declarationResult.Mailing is null)
        {
            var current = declarationResult.Mailing ?? mailing;
            return HtmlRenderer.Html(HtmlRenderer.Page("Финальное подтверждение", ConfirmationPage(current, declarationResult.Error), authenticated: true));
        }

        var updated = declarationResult.Mailing;
        if (updated.MessageDraft is not null && updated.MessageDraft.MessageType != messageType)
        {
            var saveResult = messages.Save(new SaveMailingMessageCommand(
                email,
                id,
                updated.MessageDraft.SenderName,
                updated.MessageDraft.Subject,
                updated.MessageDraft.Body,
                messageType,
                ToRequestMetadata(http),
                updated.MessageDraft.Attachments,
                updated.MessageDraft.BodyFormat));

            if (!saveResult.Ok)
            {
                return HtmlRenderer.Html(HtmlRenderer.Page("Финальное подтверждение", ConfirmationPage(updated, saveResult.Error), authenticated: true));
            }
        }

        return Results.Redirect($"/mailings/{id}/payment");
    }

    private static string MessagePage(Mailing mailing, string? error = null, string? senderOverride = null, string? subjectOverride = null, string? bodyOverride = null, MessageBodyFormat? formatOverride = null)
    {
        var draft = mailing.MessageDraft;
        var sender = H(senderOverride ?? draft?.SenderName ?? string.Empty);
        var subject = H(subjectOverride ?? draft?.Subject ?? string.Empty);
        var body = H(bodyOverride ?? draft?.Body ?? string.Empty);
        var format = formatOverride ?? draft?.BodyFormat ?? MessageBodyFormat.Text;
        var textSelected = format == MessageBodyFormat.Html ? string.Empty : " selected";
        var htmlSelected = format == MessageBodyFormat.Html ? " selected" : string.Empty;
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";

        return $@"
<section class='wizard-shell'>
  {WizardSteps(1)}
  <section class='panel'>
    <p class='eyebrow'>Шаг 1 из 5</p>
    <h1>1. Напишите письмо</h1>
    <p class='muted'>Сначала подготовьте текст письма. Адресатов, юридические подтверждения и оплату выберем дальше.</p>
    {alert}
    <form method='post' action='/mailings/{mailing.Id}/message' class='form-grid message-editor-form'>
      <label>От кого <input name='senderName' maxlength='{MailingMessageDraft.MaxSenderNameLength}' required value='{sender}' placeholder='Например: Библиотека №5'></label>
      <label>Тема письма <input name='subject' maxlength='{MailingMessageDraft.MaxSubjectLength}' required value='{subject}' placeholder='Например: Приглашаем на встречу'></label>
      <label>Формат письма <select name='bodyFormat'><option value='text'{textSelected}>Обычный текст</option><option value='html'{htmlSelected}>HTML</option></select></label>
      <label>Текст письма <textarea name='body' rows='16' required placeholder='Здравствуйте! Расскажите, почему вы пишете и что нужно сделать получателю.'>{body}</textarea></label>
      <div class='notice warn'>Письмолёт автоматически добавит причину получения письма, ссылку отписки и служебный идентификатор рассылки.</div>
      <div class='actions'><button class='button'>Сохранить письмо и перейти к адресатам</button><a class='btn ghost' href='/dashboard'>Вернуться в ЛК</a></div>
    </form>
  </section>
</section>";
    }

    private static string RecipientUploadPage(Mailing mailing, IReadOnlyCollection<Mailing> sourceMailings, string? error = null)
    {
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";
        var sourceOptions = ExistingListOptions(sourceMailings);
        var existingListBlock = sourceMailings.Count == 0
            ? "<p class='muted'>Сохранённых списков пока нет. Загрузите файл или вставьте адреса вручную.</p>"
            : $"<label>Выбрать существующий список <select name='sourceMailingId'><option value=''>Не использовать</option>{sourceOptions}</select><span class='field-hint'>Адреса будут скопированы в эту рассылку как snapshot.</span></label>";

        return $@"
<section class='wizard-shell address-step'>
  {WizardSteps(2)}
  <section class='panel'>
    <p class='eyebrow'>Шаг 2 из 5</p>
    <h1>2. Добавьте адресатов</h1>
    <p class='muted'>На этом шаге только формируем список. Подтверждение базы и тип письма будут на финальном экране.</p>
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

    private static string RecipientReviewPage(Mailing mailing, string query)
    {
        var rows = RecipientRows(mailing, query);
        return $@"
<section class='wizard-shell address-step'>
  {WizardSteps(3)}
  <section class='panel'>
    <p class='eyebrow'>Шаг 3 из 5</p>
    <h1>3. Проверьте список адресатов</h1>
    <p class='muted'>К оплате попадут только адреса со статусом «Принят к отправке».</p>
    {Stats(mailing)}
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

    private static string ConfirmationPage(Mailing mailing, string? error = null)
    {
        var draft = mailing.MessageDraft;
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";
        var options = string.Join("", BaseSourceLabels.All.Select(x => Option(x.Key.ToString(), x.Value, mailing.Declaration?.BaseSource.ToString())));
        var type = draft?.MessageType ?? MessageType.Transactional;
        var txSelected = type == MessageType.Advertising ? string.Empty : " selected";
        var adSelected = type == MessageType.Advertising ? " selected" : string.Empty;
        var baseChecked = mailing.Declaration?.IsBaseLegalityConfirmed == true ? " checked" : string.Empty;
        var adChecked = mailing.Declaration?.IsAdvertisingConsentConfirmed == true ? " checked" : string.Empty;

        return $@"
<section class='wizard-shell confirmation-step'>
  {WizardSteps(4)}
  <section class='panel'>
    <p class='eyebrow'>Шаг 4 из 5</p>
    <h1>4. Финальное подтверждение</h1>
    <p class='muted'>Здесь фиксируются источник базы, тип письма и юридически значимые подтверждения.</p>
    {alert}
    <section class='box'><h2>Письмо</h2><p><b>{H(draft?.Subject ?? "Без темы")}</b></p><p class='muted'>Отправитель: {H(draft?.SenderName ?? string.Empty)}</p></section>
    <section class='box'><h2>Адресаты</h2>{Stats(mailing)}</section>
    <form method='post' action='/mailings/{mailing.Id}/confirmation' class='compact-base-form address-declaration-form'>
      <div class='compact-base-fields'>
        <label class='compact-base-field'><span>Источник базы</span><select name='baseSource' required><option value=''>Выберите источник</option>{options}</select></label>
        <label class='compact-base-field'><span>Тип письма</span><select name='messageType' id='messageTypeSelect'><option value='Transactional'{txSelected}>Информационное</option><option value='Advertising'{adSelected}>Рекламное</option></select></label>
      </div>
      <label class='compact-base-check'><input type='checkbox' name='baseLegality'{baseChecked}><span>подтверждаю правомерность использования базы и <a href='/legal/data-processing?returnUrl=/mailings/{mailing.Id}/confirmation'>поручаю техническую обработку email-адресов</a></span></label>
      <label class='compact-base-check compact-ad-consent' id='advertisingConsentBlock'><input type='checkbox' name='advertisingConsent'{adChecked}><span><a href='/legal/advertising-consent?returnUrl=/mailings/{mailing.Id}/confirmation'>подтверждаю наличие рекламного согласия адресатов</a></span></label>
      <label class='check'><input type='checkbox' name='campaignLaunchConfirmation' required><span>Я проверил письмо и список адресатов, понимаю, что после оплаты рассылка уйдёт на проверку и будет запущена автоматически после успешной модерации.</span></label>
      <div class='actions'><button class='button'>Подтвердить и перейти к оплате</button><a class='btn secondary' href='/mailings/{mailing.Id}/recipients'>Назад к адресатам</a></div>
    </form>
  </section>
</section>
{AdvertisingConsentScript()}";
    }

    private static string WizardSteps(int current) => $"<div class='wizard-steps'><span class='wizard-step {StepClass(current, 1)}'>1. Письмо</span><span class='wizard-step {StepClass(current, 2)}'>2. Адресаты</span><span class='wizard-step {StepClass(current, 3)}'>3. Просмотр списка</span><span class='wizard-step {StepClass(current, 4)}'>4. Подтверждение</span><span class='wizard-step {StepClass(current, 5)}'>5. Оплата</span></div>";

    private static string StepClass(int current, int step) => current == step ? "current" : current > step ? "done" : string.Empty;

    private static string Stats(Mailing mailing)
    {
        var stats = mailing.LastImportStats;
        var blocked = stats.Invalid + stats.Duplicates + stats.GloballySuppressed + stats.ClientSuppressed;
        return $"<div class='stats import-summary'><div class='stat'><b>{stats.TotalRows}</b><span>строк в источнике</span></div><div class='stat'><b>{stats.Accepted}</b><span>принято к отправке</span></div><div class='stat'><b>{stats.Duplicates}</b><span>дублей</span></div><div class='stat'><b>{stats.Invalid}</b><span>некорректных</span></div><div class='stat'><b>{stats.GloballySuppressed}</b><span>отписались через Письмолёт</span></div><div class='stat'><b>{blocked}</b><span>не пойдёт в оплату</span></div></div>";
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
            yield return new RecipientDisplayRow(email, status, source, rowNumber, fallbackOrder);
        }
    }

    private static async Task<ImportSourceInput> BuildImportSource(IFormCollection form, IReadOnlyCollection<Mailing> allMailings, Guid currentMailingId, CancellationToken cancellationToken)
    {
        var file = form.Files.GetFile("file");
        if (file is { Length: > 0 })
        {
            if (file.Length > MaxRecipientUploadBytes)
            {
                return ImportSourceInput.Failure("Файл слишком большой для dev-среза.");
            }

            var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);
            stream.Position = 0;
            return ImportSourceInput.Success(string.IsNullOrWhiteSpace(file.FileName) ? "recipients.csv" : file.FileName, stream);
        }

        var manual = form["manualAddresses"].ToString();
        if (!string.IsNullOrWhiteSpace(manual))
        {
            if (Encoding.UTF8.GetByteCount(manual) > MaxManualAddressBytes)
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

        if (Guid.TryParse(form["sourceMailingId"].ToString(), out var sourceMailingId) && sourceMailingId != currentMailingId)
        {
            var source = allMailings.FirstOrDefault(x => x.Id == sourceMailingId);
            var rows = source?.Recipients
                .Where(x => x.Status == RecipientStatus.Accepted)
                .Select(x => string.IsNullOrWhiteSpace(x.SourceEmail) ? x.Email : x.SourceEmail)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();
            if (rows.Length == 0)
            {
                return ImportSourceInput.Failure("В выбранном списке нет адресов, принятых к отправке.");
            }

            var csv = "email\n" + string.Join('\n', rows);
            var fileName = $"existing-list-{sourceMailingId:N}.csv";
            return ImportSourceInput.Success(fileName, new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        }

        return ImportSourceInput.Failure("Загрузите файл, вставьте адреса вручную или выберите существующий список.");
    }

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

    private static string StatusLabel(RecipientStatus status) => status switch
    {
        RecipientStatus.Accepted => "Принят к отправке",
        RecipientStatus.Invalid => "Некорректный адрес",
        RecipientStatus.Duplicate => "Дубль",
        RecipientStatus.GloballySuppressed => "Ранее отписался",
        RecipientStatus.ClientSuppressed => "Исключён клиентом",
        _ => status.ToString()
    };

    private static BaseSource? TryParseBaseSource(string? value) => Enum.TryParse<BaseSource>(value, out var source) ? source : null;

    private static MessageType TryParseMessageType(string? value) => Enum.TryParse<MessageType>(value, out var type) ? type : MessageType.Transactional;

    private static Mailing? GetMailing(Guid id, HttpContext http, IMailingService mailings)
    {
        var email = CurrentEmail(http);
        return email is null ? null : mailings.GetForOwner(id, email);
    }

    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);

    private static RequestMetadata ToRequestMetadata(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = http.Request.Headers.UserAgent.ToString();
        return new RequestMetadata(ip, string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);
    }

    private static string AdvertisingConsentScript() => "<script class='compact-ad-consent-script'>(function(){var s=document.getElementById('messageTypeSelect');var b=document.getElementById('advertisingConsentBlock');if(!s||!b)return;function x(){b.style.display=s.value==='Advertising'?'flex':'none';}s.addEventListener('change',x);x();})();</script>";

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private sealed record RecipientDisplayRow(string Email, string Status, string Source, int Order, int FallbackOrder);

    private sealed record ImportSourceInput(bool Ok, string Error, string FileName, MemoryStream? Content)
    {
        public static ImportSourceInput Success(string fileName, MemoryStream content) => new(true, string.Empty, fileName, content);
        public static ImportSourceInput Failure(string error) => new(false, error, string.Empty, null);
    }
}
