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

        app.MapGet("/mailings/new", () => HtmlRenderer.Html(HtmlRenderer.Page(
            "Новая рассылка",
            NewMailingWizard(),
            authenticated: true))).RequireAuthorization();

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

    private static async Task<IResult> CreateMailing(HttpContext http, IMailingService mailings)
    {
        var email = CurrentEmail(http);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var form = await http.Request.ReadFormAsync();
        var result = mailings.CreateDraft(new CreateMailingCommand(email, form["subject"].ToString()), ToRequestMetadata(http));
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
        var body = $"<section class='card'><h1>{H(mailing.Subject)}</h1><p><span class='badge'>{mailing.StatusRu}</span></p>{importInfo}<p>Адресаты: принято {stats.Accepted}; дублей {stats.Duplicates}; невалидных {stats.Invalid}; исключены по глобальной отписке {stats.GloballySuppressed}.</p><h2>Ответы получателей</h2><p>{replyInfo}</p><p class='muted'>Ответы пересылаются клиенту на email отправителя; здесь показывается только счётчик и безопасный статус.</p><p>{next}</p><p><a href='/dashboard'>Вернуться в ЛК</a></p></section>";
        return HtmlRenderer.Html(HtmlRenderer.Page("Рассылка", body, authenticated: true));
    }

    private static IResult ShowUploadForm(Guid id, HttpContext http, IMailingService mailings)
    {
        var mailing = GetMailing(id, http, mailings);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена."), authenticated: true));
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Адреса получателей", AddressStepWizard(mailing, null), authenticated: true));
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

        return HtmlRenderer.Html(HtmlRenderer.Page("Результат проверки", ImportResultWizard(result.Mailing), authenticated: true));
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

        return HtmlRenderer.Html(HtmlRenderer.Page("Подтверждение базы", DeclarationForm(mailing, null), authenticated: true));
    }

    private static async Task<IResult> ConfirmDeclaration(Guid id, HttpContext http, IMailingDeclarationService declarations)
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
            return HtmlRenderer.Html(HtmlRenderer.Page("Подтверждение базы", DeclarationForm(result.Mailing, result.Error), authenticated: true));
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
            return Results.Redirect($"/mailings/{id}/declaration");
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

        var form = await http.Request.ReadFormAsync();
        var result = messages.Save(new SaveMailingMessageCommand(
            email,
            id,
            form["senderName"].ToString(),
            form["subject"].ToString(),
            form["body"].ToString(),
            TryParseMessageType(form["messageType"].ToString()),
            ToRequestMetadata(http)));

        var mailing = result.Mailing ?? mailings.GetForOwner(id, email);
        if (!result.Ok || mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Редактор письма", MessageForm(mailing, renderer, result.Error), authenticated: true));
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Письмо подготовлено", MessageForm(mailing, renderer, null), authenticated: true));
    }

    private static string NewMailingWizard() => @"
<section class='wizard-shell'>
  <div class='wizard-steps' aria-label='Шаги создания рассылки'>
    <span class='wizard-step current'>Черновик</span>
    <span class='wizard-step'>1. Адреса</span>
    <span class='wizard-step'>2. Письмо</span>
    <span class='wizard-step'>3. Проверка и оплата</span>
  </div>
  <section class='panel wizard-intro'>
    <p class='eyebrow'>Новая рассылка</p>
    <h1>Создайте черновик рассылки</h1>
    <p class='muted'>Сначала задайте рабочее название. На следующем шаге добавите адреса через файл или ручную вставку.</p>
    <form method='post' action='/mailings' class='form-grid'>
      <label>Название рассылки<input name='subject' required maxlength='160' placeholder='Например: Новости школы за июнь'></label>
      <div class='actions'>
        <button class='button'>Создать черновик</button>
        <a class='btn secondary' href='/dashboard'>Вернуться в ЛК</a>
      </div>
    </form>
  </section>
</section>";

    private static string AddressStepWizard(Mailing mailing, string? error)
    {
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error-message'>{H(error)}</p>";
        return $@"
<section class='wizard-shell'>
  <div class='wizard-steps' aria-label='Шаги создания рассылки'>
    <span class='wizard-step done'>Черновик</span>
    <span class='wizard-step current'>1. Адреса</span>
    <span class='wizard-step'>2. Письмо</span>
    <span class='wizard-step'>3. Проверка и оплата</span>
  </div>
  <section class='panel'>
    <div class='topline'>
      <div>
        <p class='eyebrow'>Шаг 1 из 3</p>
        <h1>1. Добавьте список адресов</h1>
        <p class='muted'>{H(mailing.Subject)}</p>
      </div>
      <span class='badge warn'>Проверка базы</span>
    </div>
    <div class='legal-warning'>Не используйте купленные или чужие базы. Добавляйте только адреса, по которым у вас есть законное основание для обращения.</div>
    {alert}
    <form method='post' action='/mailings/{mailing.Id}/recipients' enctype='multipart/form-data' class='wizard-grid'>
      <label class='dropzone'>
        <span>Загрузите Excel или CSV</span>
        <small>Файл `.xlsx` или `.csv` с колонкой email. Максимум 1000 строк на MVP-этапе.</small>
        <input type='file' name='file' accept='.csv,.xlsx'>
      </label>
      <label class='manual-addresses'>
        <span>Или вставьте адреса вручную</span>
        <small>Один адрес на строку. Максимум 1000 строк и 1 МБ. Дубликаты и отписавшиеся адреса будут исключены автоматически.</small>
        <textarea name='manualAddresses' rows='12' placeholder='client@example.ru&#10;reader@example.com'></textarea>
      </label>
      <div class='actions wizard-actions'>
        <button class='button'>Проверить адреса</button>
        <a class='btn secondary' href='/mailings/{mailing.Id}'>Вернуться к рассылке</a>
      </div>
    </form>
  </section>
</section>";
    }

    private static string ImportResultWizard(Mailing mailing)
    {
        var stats = mailing.LastImportStats;
        var issues = mailing.LastImportBatch?.Issues.Take(10).ToArray() ?? Array.Empty<RecipientImportIssue>();
        var blocked = stats.Invalid + stats.Duplicates + stats.GloballySuppressed + stats.ClientSuppressed;
        var issueRows = issues.Length == 0
            ? "<p class='muted'>Ошибок в первых строках не найдено.</p>"
            : string.Join("", issues.Select(issue => $"<li><b>Строка {issue.RowNumber}</b><span>{H(issue.Email)}</span><em>{H(issue.Message)}</em></li>"));
        var issueBlock = issues.Length == 0
            ? issueRows
            : $"<ul class='issue-list'>{issueRows}</ul>";

        return $@"
<section class='wizard-shell'>
  <div class='wizard-steps' aria-label='Шаги создания рассылки'>
    <span class='wizard-step done'>Черновик</span>
    <span class='wizard-step current'>1. Адреса</span>
    <span class='wizard-step'>2. Письмо</span>
    <span class='wizard-step'>3. Проверка и оплата</span>
  </div>
  <section class='panel'>
    <p class='eyebrow'>Результат проверки</p>
    <h1>Адреса проверены</h1>
    <p class='muted'>{H(mailing.Subject)}</p>
    <div class='stats import-summary'>
      <div class='stat'><b>{stats.TotalRows}</b><span>Строк в файле</span></div>
      <div class='stat'><b>{stats.Accepted}</b><span>Принято к отправке</span></div>
      <div class='stat'><b>{stats.Duplicates + stats.Invalid}</b><span>Дублей и ошибок</span></div>
      <div class='stat'><b>{blocked}</b><span>Не сможем отправить</span></div>
      <div class='stat'><b>{stats.GloballySuppressed}</b><span>Ранее отписались</span></div>
    </div>
    <h2>Что исключено</h2>
    {issueBlock}
    <div class='actions'>
      <a class='button' href='/mailings/{mailing.Id}/declaration'>Перейти к следующему шагу</a>
      <a class='btn secondary' href='/mailings/{mailing.Id}/recipients'>Загрузить другой список</a>
    </div>
  </section>
</section>";
    }

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

    private static string ToManualCsv(string value)
    {
        return "email\n" + string.Join('\n', ManualAddressLines(value));
    }

    private static IEnumerable<string> ManualAddressLines(string value) => value
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n')
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim())
        .Where(line => !string.IsNullOrWhiteSpace(line));

    private static string DeclarationForm(Mailing? mailing, string? error)
    {
        if (mailing is null)
        {
            return HtmlRenderer.Error(error ?? "Рассылка не найдена.");
        }

        var options = string.Join("", BaseSourceLabels.All.Select(x => $"<option value='{x.Key}'>{H(x.Value)}</option>"));
        var stats = mailing.LastImportStats;
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error'>{H(error)}</p>";
        return $"<section class='card form-card'><h1>Подтверждение базы</h1><p class='muted'>{H(mailing.Subject)}</p>{alert}<p>Принято адресов: {stats.Accepted}. Дублей: {stats.Duplicates}. Невалидных: {stats.Invalid}.</p><form method='post' action='/mailings/{mailing.Id}/declaration'><label>Источник базы<select name='baseSource' required><option value=''>Выберите источник</option>{options}</select></label><label>Тип письма<select name='messageType'><option value='Transactional'>Информационное</option><option value='Advertising'>Рекламное</option></select></label><label><input type='checkbox' name='baseLegality'> Подтверждаю правомерность использования базы</label><label><input type='checkbox' name='advertisingConsent'> Для рекламного письма подтверждаю наличие рекламного согласия адресатов</label><div class='card'><strong>Текст декларации, версия {BaseDeclarationText.CurrentVersion}</strong><p>{H(BaseDeclarationText.Text)}</p></div><button class='button'>Подтвердить базу</button></form><p><a href='/mailings/{mailing.Id}'>Вернуться к рассылке</a></p></section>";
    }

    private static string MessageForm(Mailing? mailing, IMessageRenderingService renderer, string? error)
    {
        if (mailing is null)
        {
            return HtmlRenderer.Error(error ?? "Рассылка не найдена.");
        }

        var draft = mailing.MessageDraft;
        var preview = renderer.RenderPreview(mailing);
        var alert = string.IsNullOrWhiteSpace(error) ? string.Empty : $"<p class='error'>{H(error)}</p>";
        var transactionalSelected = draft?.MessageType == MessageType.Advertising ? string.Empty : " selected";
        var advertisingSelected = draft?.MessageType == MessageType.Advertising ? " selected" : string.Empty;
        var prepared = draft is null ? string.Empty : $"<section class='card'><h2>Preview служебных блоков</h2><pre>{H(preview.PlainText)}</pre><p><a class='button' href='/mailings/{mailing.Id}/payment'>Перейти к проверке и оплате</a></p></section>";
        return $"<section class='card form-card'><h1>Редактор письма</h1><p class='muted'>{H(mailing.Subject)}</p>{alert}<form method='post' action='/mailings/{mailing.Id}/message'><label>Имя отправителя<input name='senderName' maxlength='80' required value='{H(draft?.SenderName ?? string.Empty)}'></label><label>Тема письма<input name='subject' maxlength='160' required value='{H(draft?.Subject ?? mailing.Subject)}'></label><label>Тип письма<select name='messageType'><option value='Transactional'{transactionalSelected}>Информационное</option><option value='Advertising'{advertisingSelected}>Рекламное</option></select></label><label>Текст письма<textarea name='body' rows='10' required>{H(draft?.Body ?? string.Empty)}</textarea></label><button class='button'>Сохранить письмо</button></form><p><a href='/mailings/{mailing.Id}/declaration'>Назад к подтверждению базы</a></p></section>{prepared}";
    }

    private static string NextStep(Mailing mailing)
    {
        if (mailing.LastImportStats.Accepted <= 0)
        {
            return $"<a class='button' href='/mailings/{mailing.Id}/recipients'>Загрузить адреса</a>";
        }

        if (mailing.Declaration is null)
        {
            return $"<a class='button' href='/mailings/{mailing.Id}/declaration'>Подтвердить базу</a>";
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

    private static BaseSource? TryParseBaseSource(string value) => Enum.TryParse<BaseSource>(value, out var source) ? source : null;

    private static MessageType TryParseMessageType(string value) => Enum.TryParse<MessageType>(value, out var type) ? type : MessageType.Transactional;

    private static Pismolet.Web.Domain.Mailings.Mailing? GetMailing(Guid id, HttpContext http, IMailingService mailings)
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
