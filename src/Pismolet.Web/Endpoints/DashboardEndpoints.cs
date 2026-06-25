using System.Net;
using System.Security.Claims;
using System.Text;
using Pismolet.Web.Application.Auth;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class DashboardEndpoints
{
    private const int MaxRecipientUploadBytes = 1024 * 1024;
    private const int MaxManualAddressBytes = MaxRecipientUploadBytes;
    private const int MaxManualAddressRows = RecipientImportService.MaxRows;
    private const string InitialMailingSubject = "Новая рассылка";

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dashboard", (HttpContext http, IUserAccountService accounts, IMailingService mailings) =>
        {
            var email = CurrentEmail(http);
            if (email is null)
            {
                return Results.Redirect("/account/login");
            }

            var user = accounts.GetByEmail(email);
            if (user is null)
            {
                return Results.Redirect("/account/login");
            }

            var shownUser = user with { Mailings = mailings.ListForOwner(email).ToList() };
            return HtmlRenderer.Html(HtmlRenderer.Page("Личный кабинет", HtmlRenderer.Dashboard(shownUser), authenticated: true));
        }).RequireAuthorization();

        app.MapGet("/mailings/new", StartNewMailing).RequireAuthorization();
        app.MapPost("/mailings", CreateMailing).RequireAuthorization();
        app.MapGet("/mailings/{id:guid}", ShowMailing).RequireAuthorization();
        app.MapGet("/mailings/{id:guid}/recipients", ShowUploadForm).RequireAuthorization();
        app.MapPost("/mailings/{id:guid}/recipients", ImportRecipients).RequireAuthorization();
        app.MapGet("/mailings/{id:guid}/declaration", ShowDeclaration).RequireAuthorization();
        app.MapPost("/mailings/{id:guid}/declaration", ConfirmDeclaration).RequireAuthorization();
        app.MapGet("/mailings/{id:guid}/message", ShowMessageEditor).RequireAuthorization();
        app.MapPost("/mailings/{id:guid}/message", SaveMessage).RequireAuthorization();

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

        return Results.Redirect($"/mailings/{result.Mailing.Id}/recipients");
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

        return Results.Redirect($"/mailings/{result.Mailing.Id}/recipients");
    }

    private static IResult ShowMailing(Guid id, HttpContext http, IMailingService mailings, IReplyEventRepository replies)
    {
        var mailing = GetMailing(id, http, mailings);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        var stats = mailing.LastImportStats;
        var next = NextStep(mailing);
        var importInfo = mailing.LastImportBatch is null
            ? string.Empty
            : $"<p class='muted'>Последний импорт: {H(mailing.LastImportBatch.FileName)} ({mailing.LastImportBatch.SourceFormat})</p>";
        var replySummary = replies.GetSummary(mailing.Id);
        var replyInfo = replySummary.TotalReplies == 0
            ? "Ответов пока нет."
            : $"Ответы: {replySummary.TotalReplies}; последний: {replySummary.LastReplyAt:yyyy-MM-dd HH:mm} UTC; статус: {H(replySummary.LastStatus?.ToRu() ?? "неизвестно")}.";
        var body = $"<section class='card'><h1>{H(DisplayTitle(mailing))}</h1><p><span class='badge'>{mailing.StatusRu}</span></p>{importInfo}<p>Адресаты: принято {stats.Accepted}; дублей {stats.Duplicates}; невалидных: {stats.Invalid}; исключены по глобальной отписке {stats.GloballySuppressed}; исключены из-за ошибок доставки {stats.ClientSuppressed}.</p><h2>Ответы получателей</h2><p>{replyInfo}</p><p class='muted'>Ответы пересылаются клиенту на email отправителя; здесь показывается только счётчик и безопасный статус.</p><p>{next}</p><p><a href='/dashboard'>Вернуться в ЛК</a></p></section>";
        return HtmlRenderer.Html(HtmlRenderer.Page("Рассылка", body, authenticated: true));
    }

    private static IResult ShowUploadForm(Guid id, HttpContext http, IMailingService mailings)
    {
        var mailing = GetMailing(id, http, mailings);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        var body = mailing.LastImportStats.TotalRows > 0
            ? ImportResultWizard(mailing)
            : AddressStepWizard(mailing, null);
        return HtmlRenderer.Html(HtmlRenderer.Page("Адреса получателей", body, authenticated: true));
    }

    private static async Task<IResult> ImportRecipients(Guid id, HttpContext http, IMailingService mailings, IRecipientImportService imports)
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
        var file = form.Files.GetFile("file");
        var manualAddresses = form["manualAddresses"].ToString();
        var hasFile = file is { Length: > 0 };
        var hasManualAddresses = !string.IsNullOrWhiteSpace(manualAddresses);

        if (!hasFile && !hasManualAddresses)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Адреса получателей", AddressStepWizard(mailing, "Загрузите CSV/XLSX-файл или вставьте адреса вручную."), authenticated: true));
        }

        if (hasFile && file!.Length > MaxRecipientUploadBytes)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Адреса получателей", AddressStepWizard(mailing, "Файл слишком большой для dev-среза."), authenticated: true));
        }

        if (hasManualAddresses)
        {
            var manualError = ValidateManualAddresses(manualAddresses);
            if (manualError is not null)
            {
                return HtmlRenderer.Html(HtmlRenderer.Page("Адреса получателей", AddressStepWizard(mailing, manualError), authenticated: true));
            }
        }

        ImportRecipientsResult result;
        if (hasFile)
        {
            await using var stream = file!.OpenReadStream();
            result = await imports.ImportAsync(new ImportRecipientsCommand(email, id, file.FileName, stream, ToRequestMetadata(http)));
        }
        else
        {
            var manualCsv = ToManualCsv(manualAddresses);
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(manualCsv));
            result = await imports.ImportAsync(new ImportRecipientsCommand(email, id, "manual-addresses.csv", stream, ToRequestMetadata(http)));
        }

        if (!result.Ok || result.Mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Адреса получателей", AddressStepWizard(mailing, result.Error), authenticated: true));
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Адреса получателей", ImportResultWizard(result.Mailing), authenticated: true));
    }

    private static IResult ShowDeclaration(Guid id, HttpContext http, IMailingService mailings)
    {
        var mailing = GetMailing(id, http, mailings);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        if (mailing.LastImportStats.Accepted <= 0)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Сначала загрузите адреса для рассылки."), authenticated: true));
        }

        return Results.Redirect($"/mailings/{id}/recipients");
    }

    private static async Task<IResult> ConfirmDeclaration(Guid id, HttpContext http, IMailingDeclarationService declarations, IMailingService mailings)
    {
        var email = CurrentEmail(http);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var form = await http.Request.ReadFormAsync();
        var result = declarations.Confirm(new ConfirmMailingDeclarationCommand(
            email,
            id,
            TryParseBaseSource(form["baseSource"].ToString()),
            form.ContainsKey("baseLegality"),
            form.ContainsKey("advertisingConsent"),
            TryParseMessageType(form["messageType"].ToString()),
            ToRequestMetadata(http)));

        if (!result.Ok || result.Mailing is null)
        {
            var mailing = result.Mailing ?? GetMailing(id, http, mailings);
            return HtmlRenderer.Html(HtmlRenderer.Page("Адреса получателей", mailing is null ? HtmlRenderer.Error(result.Error) : ImportResultWizard(mailing, result.Error), authenticated: true));
        }

        return Results.Redirect($"/mailings/{id}/message");
    }

    private static IResult ShowMessageEditor(Guid id, HttpContext http, IMailingService mailings, IMessageRenderingService renderer)
    {
        var mailing = GetMailing(id, http, mailings);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        if (mailing.Declaration is null)
        {
            return Results.Redirect($"/mailings/{id}/recipients");
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Редактор письма", MessageForm(mailing, renderer, null), authenticated: true));
    }

    private static async Task<IResult> SaveMessage(Guid id, HttpContext http, IMailingMessageService messages, IMailingService mailings, IMessageRenderingService renderer)
    {
        var email = CurrentEmail(http);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var existing = mailings.GetForOwner(id, email);
        var form = await http.Request.ReadFormAsync();
        var messageType = ResolveMessageType(form, existing);
        var result = messages.Save(new SaveMailingMessageCommand(
            email,
            id,
            form["senderName"].ToString(),
            form["subject"].ToString(),
            form["body"].ToString(),
            messageType,
            ToRequestMetadata(http)));

        var mailing = result.Mailing ?? existing;
        if (!result.Ok || mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Редактор письма", MessageForm(mailing, renderer, result.Error), authenticated: true));
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Письмо подготовлено", MessageForm(mailing, renderer, null, saved: true), authenticated: true));
    }

    private static string WizardSteps(int currentStep) => $@"
  <div class='wizard-steps' aria-label='Шаги создания рассылки'>
    <span class='wizard-step {(currentStep > 1 ? "done" : currentStep == 1 ? "current" : string.Empty)}'>1. Адреса</span>
    <span class='wizard-step {(currentStep > 2 ? "done" : currentStep == 2 ? "current" : string.Empty)}'>2. Письмо</span>
    <span class='wizard-step {(currentStep > 3 ? "done" : currentStep == 3 ? "current" : string.Empty)}'>3. Расчёт и оплата</span>
    <span class='wizard-step {(currentStep == 4 ? "current" : string.Empty)}'>4. Готово</span>
  </div>";

    private static string AddressStepWizard(Mailing mailing, string? error)
    {
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";
        return $@"
<section class='wizard-shell'>
  {WizardSteps(1)}
  <section class='panel'>
    <div class='topline'>
      <div>
        <p class='eyebrow'>Шаг 1 из 4</p>
        <h1>1. Добавьте список адресов</h1>
        <p class='muted'>Не используйте купленные или чужие базы.</p>
      </div>
      <span class='badge warn'>Проверка базы</span>
    </div>
    {alert}
    <form method='post' action='/mailings/{mailing.Id}/recipients' enctype='multipart/form-data' class='wizard-grid'>
      <label class='dropzone'>
        <span>Перетащите таблицу Excel сюда</span>
        <small>или нажмите, чтобы выбрать файл. Подойдут `.xlsx` или `.csv` с колонкой email.</small>
        <input type='file' name='file' accept='.csv,.xlsx'>
      </label>
      <label class='manual-addresses'>
        <span>Или вставьте адреса вручную</span>
        <small>Каждый адрес — с новой строки. Дубликаты и отписавшиеся адреса будут исключены автоматически.</small>
        <textarea name='manualAddresses' rows='12' placeholder='anna@example.ru&#10;club@example.ru&#10;ivan@example.ru'></textarea>
      </label>
      <div class='actions wizard-actions'>
        <button class='button'>Адреса добавлены, дальше</button>
        <a class='btn secondary' href='/dashboard'>Вернуться в ЛК</a>
      </div>
    </form>
  </section>
</section>";
    }

    private static string ImportResultWizard(Mailing mailing, string? error = null)
    {
        var stats = mailing.LastImportStats;
        var allIssues = mailing.LastImportBatch?.Issues.ToArray() ?? Array.Empty<RecipientImportIssue>();
        var warningIssues = allIssues.Where(IsWarningIssue).Take(10).ToArray();
        var excludedIssues = allIssues.Where(issue => !IsWarningIssue(issue)).Take(10).ToArray();
        var blocked = stats.Invalid + stats.Duplicates + stats.GloballySuppressed + stats.ClientSuppressed;
        var warningsBlock = warningIssues.Length == 0
            ? string.Empty
            : $"<h2>Предупреждения</h2>{IssueBlock(warningIssues, "Предупреждений нет.")}";
        var excludedBlock = IssueBlock(excludedIssues, "Исключённых адресов нет.");
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";

        return $@"
<section class='wizard-shell'>
  {WizardSteps(1)}
  <section class='panel'>
    <p class='eyebrow'>Шаг 1 из 4</p>
    <h1>Адреса проверены</h1>
    <div class='stats import-summary'>
      <div class='stat'><b>{stats.TotalRows}</b><span>Строк в файле</span></div>
      <div class='stat'><b>{stats.Accepted}</b><span>Принято к отправке</span></div>
      <div class='stat'><b>{stats.Duplicates + stats.Invalid}</b><span>Дублей и ошибок</span></div>
      <div class='stat'><b>{blocked}</b><span>Не сможем отправить</span></div>
      <div class='stat'><b>{stats.GloballySuppressed}</b><span>Ранее отписались</span></div>
    </div>
    {warningsBlock}
    <h2>Что исключено</h2>
    {excludedBlock}
    <div class='split-grid'>
      <section class='box'>
        <h2>Подтвердите базу</h2>
        <p class='muted'>Источник и подтверждения фиксируются вместе с этим шагом.</p>
        {alert}
        {BaseConfirmationForm(mailing)}
      </section>
      <section class='box muted-box'>
        <h2>Декларация законности базы</h2>
        <p>Полный текст вынесен в отдельный юридический документ.</p>
        <a class='btn secondary' href='/legal/base-lawfulness?returnUrl=/mailings/{mailing.Id}/recipients'>Открыть декларацию</a>
      </section>
    </div>
  </section>
</section>";
    }

    private static string BaseConfirmationForm(Mailing mailing)
    {
        var options = string.Join("", BaseSourceLabels.All.Select(x => $"<option value='{x.Key}'>{H(x.Value)}</option>"));
        return $@"
<form method='post' action='/mailings/{mailing.Id}/declaration' class='form-grid confirmation-list'>
  <label>Источник базы<select name='baseSource' required><option value=''>Выберите источник</option>{options}</select></label>
  <label>Тип письма<select name='messageType'><option value='Transactional'>Информационное</option><option value='Advertising'>Рекламное</option></select></label>
  <label class='check'><input type='checkbox' name='baseLegality'><span>Подтверждаю правомерность использования базы и <a href='/legal/data-processing'>поручаю Письмолёту технически обработать загруженные email-адреса</a></span></label>
  <label class='check'><input type='checkbox' name='advertisingConsent'><span>Для рекламного письма <a href='/legal/advertising-consent'>подтверждаю наличие рекламного согласия адресатов</a></span></label>
  <button class='button'>Перейти к письму</button>
</form>";
    }

    private static string IssueBlock(IReadOnlyCollection<RecipientImportIssue> issues, string emptyText)
    {
        if (issues.Count == 0)
        {
            return $"<p class='muted'>{H(emptyText)}</p>";
        }

        var rows = string.Join("", issues.Select(issue => $"<li><b>Строка {issue.RowNumber}</b><span>{H(issue.Email)}</span><em>{H(issue.Message)}</em></li>"));
        return $"<ul class='issue-list'>{rows}</ul>";
    }

    private static bool IsWarningIssue(RecipientImportIssue issue) =>
        issue.Message.Contains("Адрес не исключён", StringComparison.OrdinalIgnoreCase);

    private static string? ValidateManualAddresses(string value)
    {
        if (Encoding.UTF8.GetByteCount(value) > MaxManualAddressBytes)
        {
            return "Ручная вставка слишком большая для dev-среза. Максимум 1 МБ.";
        }

        var rows = ManualAddressLines(value).Take(MaxManualAddressRows + 1).Count();
        if (rows > MaxManualAddressRows)
        {
            return $"Ручная вставка содержит больше {MaxManualAddressRows} строк.";
        }

        return null;
    }

    private static string ToManualCsv(string value) => "email\n" + string.Join('\n', ManualAddressLines(value));

    private static IEnumerable<string> ManualAddressLines(string value) => value
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n')
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim())
        .Where(line => !string.IsNullOrWhiteSpace(line));

    private static string MessageForm(Mailing? mailing, IMessageRenderingService renderer, string? error, bool saved = false)
    {
        if (mailing is null)
        {
            return HtmlRenderer.Error(error ?? "Рассылка не найдена.");
        }

        var draft = mailing.MessageDraft;
        var preview = renderer.RenderPreview(mailing);
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";
        var success = saved ? "<p class='notice'>Письмо сохранено. Проверьте превью и переходите к расчёту.</p>" : string.Empty;
        var senderName = H(draft?.SenderName ?? string.Empty);
        var messageSubject = H(draft?.Subject ?? string.Empty);
        var bodyText = draft?.Body ?? string.Empty;
        var previewSender = string.IsNullOrWhiteSpace(draft?.SenderName) ? "Письмолёт" : H(draft!.SenderName);
        var previewSubject = string.IsNullOrWhiteSpace(draft?.Subject) ? "Тема письма" : H(draft!.Subject);
        var previewBody = string.IsNullOrWhiteSpace(bodyText)
            ? "<p class='muted'>Сохраните текст письма, чтобы увидеть его в превью.</p>"
            : $"<p>{ToHtmlText(bodyText)}</p>";
        var reasonBlock = string.IsNullOrWhiteSpace(preview.ReasonBlock)
            ? "Служебный блок с причиной получения и ссылкой отписки будет добавлен автоматически после сохранения письма."
            : H(preview.ReasonBlock);
        var serviceBlock = string.IsNullOrWhiteSpace(preview.ServiceIdentifier)
            ? H($"Служебный идентификатор рассылки: {mailing.PublicId}")
            : H(preview.ServiceIdentifier);
        var unsubscribeUrl = string.IsNullOrWhiteSpace(preview.UnsubscribeUrl) ? "/unsubscribe/example-token" : H(preview.UnsubscribeUrl);
        var continueAction = draft is null
            ? "<button class='button'>Сохранить письмо</button>"
            : $"<button class='button'>Сохранить письмо</button><a class='btn secondary' href='/mailings/{mailing.Id}/payment'>Перейти к проверке и оплате</a>";

        return $@"
<section class='wizard-shell'>
  {WizardSteps(2)}
  <section class='panel'>
    <div class='topline'>
      <div>
        <p class='eyebrow'>Шаг 2 из 4</p>
        <h1>2. Напишите письмо</h1>
      </div>
      <span class='badge warn'>Письмо</span>
    </div>
    {alert}
    {success}
    <div class='message-wizard-grid'>
      <form method='post' action='/mailings/{mailing.Id}/message' class='form-grid message-editor-form'>
        <div class='row write-row'>
          <label class='write-field'>
            <span class='field-title'>От кого <span class='required'>*</span></span>
            <input name='senderName' maxlength='{MailingMessageDraft.MaxSenderNameLength}' required value='{senderName}' placeholder='Например: Библиотека №5'>
            <span class='field-hint'>Получатели увидят это имя в письме.</span>
          </label>
        </div>
        <label>Тема письма
          <input name='subject' maxlength='{MailingMessageDraft.MaxSubjectLength}' required value='{messageSubject}' placeholder='Например: Приглашаем на встречу в субботу'>
        </label>
        <label>Текст письма
          <textarea name='body' rows='12' required placeholder='Здравствуйте!&#10;&#10;Расскажите, почему вы пишете и что нужно сделать получателю.'>{H(bodyText)}</textarea>
        </label>
        <div class='notice warn'>Письмолёт автоматически добавит причину получения письма, ссылку отписки и служебный идентификатор рассылки.</div>
        <div class='actions'>
          {continueAction}
          <a class='btn ghost' href='/mailings/{mailing.Id}/recipients'>Назад к адресам</a>
        </div>
      </form>
      <aside class='box message-preview-card'>
        <h3>Превью письма</h3>
        <div class='mail-preview'>
          <div class='mail-preview-header'>От: <span>{previewSender}</span> &lt;info@pismolet.ru&gt;</div>
          <div class='mail-preview-body'>
            <h4>{previewSubject}</h4>
            {previewBody}
            <div class='unsubscribe'>
              <p>{reasonBlock}</p>
              <p>Отписаться: <code>{unsubscribeUrl}</code></p>
              <p>{serviceBlock}</p>
            </div>
          </div>
        </div>
      </aside>
    </div>
  </section>
</section>";
    }

    private static string ToHtmlText(string value) => H(value)
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace("\r", "\n", StringComparison.Ordinal)
        .Replace("\n", "<br>", StringComparison.Ordinal);

    private static string NextStep(Mailing mailing)
    {
        if (mailing.LastImportStats.Accepted <= 0)
        {
            return $"<a class='button' href='/mailings/{mailing.Id}/recipients'>Загрузить адреса</a>";
        }

        if (mailing.Declaration is null)
        {
            return $"<a class='button' href='/mailings/{mailing.Id}/recipients'>Подтвердить базу</a>";
        }

        if (mailing.MessageDraft is null)
        {
            return $"<a class='button' href='/mailings/{mailing.Id}/message'>Написать письмо</a>";
        }

        if (mailing.StatusRu is "Оплачено" or "Проверяем перед отправкой" or "На ручной проверке" or "Одобрено" or "Отклонено")
        {
            return $"<a class='button' href='/mailings/{mailing.Id}/checks'>Открыть проверку перед отправкой</a>";
        }

        return $"<a class='button' href='/mailings/{mailing.Id}/payment'>Перейти к проверке и оплате</a>";
    }

    private static string DisplayTitle(Mailing mailing) => string.IsNullOrWhiteSpace(mailing.MessageDraft?.Subject)
        ? "Новая рассылка"
        : mailing.MessageDraft!.Subject;

    private static MessageType ResolveMessageType(IFormCollection form, Mailing? mailing)
    {
        var value = form["messageType"].ToString();
        return string.IsNullOrWhiteSpace(value)
            ? mailing?.MessageDraft?.MessageType ?? MessageType.Transactional
            : TryParseMessageType(value);
    }

    private static BaseSource? TryParseBaseSource(string value) => Enum.TryParse<BaseSource>(value, out var source) ? source : null;

    private static MessageType TryParseMessageType(string value) => Enum.TryParse<MessageType>(value, out var type) ? type : MessageType.Transactional;

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

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
