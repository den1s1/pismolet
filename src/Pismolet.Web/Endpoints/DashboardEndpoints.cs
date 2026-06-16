using System.Security.Claims;
using Pismolet.Web.Application.Auth;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
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
        var body = $"<section class='card'><h1>{mailing.Subject}</h1><p><span class='badge'>{mailing.StatusRu}</span></p><p>Адресаты: принято {stats.Accepted}; дублей {stats.Duplicates}; невалидных {stats.Invalid}; исключены по глобальной отписке {stats.GloballySuppressed}.</p><p><a class='button' href='/mailings/{mailing.Id}/recipients'>Загрузить адреса</a> <a href='/dashboard'>Вернуться в ЛК</a></p></section>";
        return HtmlRenderer.Html(HtmlRenderer.Page("Рассылка", body));
    }

    private static IResult ShowUploadForm(Guid id, HttpContext http, IMailingService mailings)
    {
        var mailing = GetMailing(id, http, mailings);
        if (mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Рассылка не найдена.")));
        }

        var body = $"<section class='card form-card'><h1>Загрузка адресов</h1><p class='muted'>{mailing.Subject}</p><form method='post' action='/mailings/{mailing.Id}/recipients' enctype='multipart/form-data'><label>CSV-файл с колонкой email<input type='file' name='file' accept='.csv' required></label><button class='button'>Загрузить</button></form><p><a href='/mailings/{mailing.Id}'>Вернуться к рассылке</a></p></section>";
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
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Выберите CSV-файл.")));
        }

        if (file.Length > 256 * 1024)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error("Файл слишком большой для dev-среза.")));
        }

        await using var stream = file.OpenReadStream();
        var result = await imports.ImportCsvAsync(new ImportRecipientsCommand(email, id, file.FileName, stream, ToRequestMetadata(http)));
        if (!result.Ok || result.Mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error(result.Error)));
        }

        var s = result.Stats;
        var body = $"<section class='card'><h1>Результат проверки</h1><p class='muted'>{result.Mailing.Subject}</p><ul><li>Всего строк: {s.TotalRows}</li><li>Принято адресов: {s.Accepted}</li><li>Дублей: {s.Duplicates}</li><li>Невалидных email: {s.Invalid}</li><li>Исключены по глобальной отписке: {s.GloballySuppressed}</li></ul><p><a class='button' href='/mailings/{result.Mailing.Id}'>Вернуться к рассылке</a> <a href='/mailings/{result.Mailing.Id}/recipients'>Загрузить другой файл</a></p></section>";
        return HtmlRenderer.Html(HtmlRenderer.Page("Результат проверки", body));
    }

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
}
