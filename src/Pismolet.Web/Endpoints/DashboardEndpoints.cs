using System.Net;
using System.Security.Claims;
using Pismolet.Web.Application.Auth;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class DashboardEndpoints
{
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
            return HtmlRenderer.Html(HtmlRenderer.Page("Личный кабинет", HtmlRenderer.Dashboard(shownUser)));
        }).RequireAuthorization();

        app.MapGet("/mailings/new", () => HtmlRenderer.Html(HtmlRenderer.Page(
            "Создать рассылку",
            "<section class='card form-card'><h1>Создать рассылку</h1><form method='post' action='/mailings'><label>Название рассылки<input name='subject' required maxlength='160'></label><button class='button'>Создать</button></form><p><a href='/dashboard'>Вернуться в ЛК</a></p></section>"))).RequireAuthorization();

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
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error(result.Error)));
        }

        return Results.Redirect($"/mailings/{result.Mailing.Id}/recipients");
    }

    private static IResult ShowMailing(Guid id, HttpContext http, IMailingService mailings)
    {
        var mailing = GetMailing(id, http, mailings);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена.")));
        }

        var stats = mailing.LastImportStats;
        var next = NextStep(mailing);
        var importInfo = mailing.LastImportBatch is null
            ? string.Empty
            : $"<p class='muted'>Последний импорт: {H(mailing.LastImportBatch.FileName)} ({mailing.LastImportBatch.SourceFormat})</p>";
        var body = $"<section class='card'><h1>{H(mailing.Subject)}</h1><p><span class='badge'>{mailing.StatusRu}</span></p>{importInfo}<p>Адресаты: принято {stats.Accepted}; дублей {stats.Duplicates}; невалидных {stats.Invalid}; исключены по глобальной отписке {stats.GloballySuppressed}.</p><p>{next}</p><p><a href='/dashboard'>Вернуться в ЛК</a></p></section>";
        return HtmlRenderer.Html(HtmlRenderer.Page("Рассылка", body));
    }

    private static IResult ShowUploadForm(Guid id, HttpContext http, IMailingService mailings)
    {
        var mailing = GetMailing(id, http, mailings);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена.")));
        }

        var body = $"<section class='card form-card'><h1>Загрузка адресов</h1><p class='muted'>{H(mailing.Subject)}</p><form method='post' action='/mailings/{mailing.Id}/recipients' enctype='multipart/form-data'><label>CSV или XLSX-файл с колонкой email<input type='file' name='file' accept='.csv,.xlsx' required></label><button class='button'>Загрузить</button></form><p><a href='/mailings/{mailing.Id}'>Вернуться к рассылке</a></p></section>";
        return HtmlRenderer.Html(HtmlRenderer.Page("Загрузка адресов", body));
    }

    private static async Task<IResult> ImportRecipients(Guid id, HttpContext http, IMailingService mailings, IRecipientImportService imports)
    {
        var email = CurrentEmail(http);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        if (mailings.GetForOwner(id, email) is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена.")));
        }

        var form = await http.Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Выберите CSV или XLSX-файл.")));
        }

        if (file.Length > 1024 * 1024)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Файл слишком большой для dev-среза.")));
        }

        await using var stream = file.OpenReadStream();
        var result = await imports.ImportAsync(new ImportRecipientsCommand(email, id, file.FileName, stream, ToRequestMetadata(http)));
        if (!result.Ok || result.Mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error(result.Error)));
        }

        var s = result.Stats;
        var body = $"<section class='card'><h1>Результат проверки</h1><p class='muted'>{H(result.Mailing.Subject)}</p><ul><li>Всего строк: {s.TotalRows}</li><li>Принято адресов: {s.Accepted}</li><li>Дублей: {s.Duplicates}</li><li>Невалидных email: {s.Invalid}</li><li>Исключены по глобальной отписке: {s.GloballySuppressed}</li></ul><p><a class='button' href='/mailings/{result.Mailing.Id}/declaration'>Подтвердить базу и написать письмо</a> <a href='/mailings/{result.Mailing.Id}/recipients'>Загрузить другой файл</a></p></section>";
        return HtmlRenderer.Html(HtmlRenderer.Page("Результат проверки", body));
    }

    private static IResult ShowDeclaration(Guid id, HttpContext http, IMailingService mailings)
    {
        var mailing = GetMailing(id, http, mailings);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена.")));
        }

        if (mailing.LastImportStats.Accepted <= 0)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Сначала загрузите адреса для рассылки.")));
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Подтверждение базы", DeclarationForm(mailing, null)));
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
            return HtmlRenderer.Html(HtmlRenderer.Page("Подтверждение базы", DeclarationForm(result.Mailing, result.Error)));
        }

        return Results.Redirect($"/mailings/{id}/message");
    }

    private static IResult ShowMessageEditor(Guid id, HttpContext http, IMailingService mailings, IMessageRenderingService renderer)
    {
        var mailing = GetMailing(id, http, mailings);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена.")));
        }

        if (mailing.Declaration is null)
        {
            return Results.Redirect($"/mailings/{id}/declaration");
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Редактор письма", MessageForm(mailing, renderer, null)));
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
            return HtmlRenderer.Html(HtmlRenderer.Page("Редактор письма", MessageForm(mailing, renderer, result.Error)));
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Письмо подготовлено", MessageForm(mailing, renderer, null)));
    }

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
