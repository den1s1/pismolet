using System.Net;
using Pismolet.Web.Infrastructure.Mail;

namespace Pismolet.Web.Endpoints;

public static class AdminPostfixDeliveryEndpoints
{
    public static IEndpointRouteBuilder MapAdminPostfixDeliveryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/delivery/postfix", ShowPostfixDeliveryReader).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapPost("/admin/delivery/postfix/read", ReadPostfixDeliveryLog).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        return app;
    }

    private static IResult ShowPostfixDeliveryReader(PostfixDeliveryLogReaderOptions options)
    {
        var body = $"""
            <section class='admin-panel'>
                <p class='eyebrow'>Доставка</p>
                <h1>Postfix delivery log</h1>
                <p class='admin-muted'>Ручной запуск чтения новых строк Postfix-лога. Первый запуск при отсутствующем cursor-файле только выставит cursor в конец файла, чтобы не импортировать старую историю.</p>
                <div class='admin-profile-grid'>
                    <div><span>Log file</span><b>{H(options.LogPath)}</b></div>
                    <div><span>Cursor file</span><b>{H(options.CursorPath)}</b></div>
                    <div><span>Год для syslog-строк</span><b>{options.Year}</b></div>
                    <div><span>UTC offset</span><b>{options.UtcOffset}</b></div>
                </div>
                <form method='post' action='/admin/delivery/postfix/read'>
                    <button class='admin-button' type='submit'>Прочитать новые строки</button>
                    <a class='admin-link' href='/admin'>Вернуться в админку</a>
                </form>
            </section>
            """;
        return Html("Postfix delivery log", body);
    }

    private static IResult ReadPostfixDeliveryLog(PostfixDeliveryLogReaderService reader)
    {
        try
        {
            var result = reader.ReadNewLines();
            return Html("Postfix delivery log", BuildResult(result));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            var body = $"""
                <section class='admin-panel'>
                    <p class='eyebrow'>Доставка</p>
                    <h1>Postfix delivery log</h1>
                    <p class='admin-muted'>Не удалось прочитать лог или сохранить cursor.</p>
                    <div class='admin-table-wrap'><table class='admin-table'><tbody>
                        <tr><th>Ошибка</th><td>{H(ex.GetType().Name)}</td></tr>
                        <tr><th>Сообщение</th><td>{H(ex.Message)}</td></tr>
                    </tbody></table></div>
                    <p><a class='admin-link' href='/admin/delivery/postfix'>Назад к запуску</a></p>
                </section>
                """;
            return Html("Postfix delivery log - ошибка", body);
        }
    }

    private static string BuildResult(PostfixDeliveryLogReaderResult result)
    {
        var body = $"""
            <section class='admin-panel'>
                <p class='eyebrow'>Доставка</p>
                <h1>Postfix delivery log</h1>
                <div class='admin-stats'>
                    <div class='admin-stat'><b>{(result.LogExists ? "Да" : "Нет")}</b><span>Лог найден</span></div>
                    <div class='admin-stat'><b>{result.LinesRead}</b><span>Новых строк</span></div>
                    <div class='admin-stat'><b>{result.Ingestion.Parsed}</b><span>Распознано</span></div>
                    <div class='admin-stat'><b>{result.Ingestion.Stored}</b><span>Сохранено</span></div>
                    <div class='admin-stat'><b>{result.Ingestion.MatchedSendEvents}</b><span>Найдено писем</span></div>
                    <div class='admin-stat'><b>{result.Ingestion.UpdatedSendEvents}</b><span>Обновлено статусов</span></div>
                </div>
                <div class='admin-table-wrap'><table class='admin-table'><tbody>
                    <tr><th>Предыдущая позиция</th><td>{result.PreviousPosition}</td></tr>
                    <tr><th>Новая позиция</th><td>{result.NewPosition}</td></tr>
                    <tr><th>Cursor инициализирован</th><td>{(result.CursorInitialized ? "Да" : "Нет")}</td></tr>
                    <tr><th>Cursor сброшен</th><td>{(result.CursorReset ? "Да" : "Нет")}</td></tr>
                    <tr><th>Игнорировано</th><td>{result.Ingestion.Ignored}</td></tr>
                </tbody></table></div>
                <p class='admin-muted'>Если cursor был только инициализирован, отправь ещё одно письмо или дождись новых строк в логе, затем запусти чтение повторно.</p>
                <p><a class='admin-link' href='/admin/delivery/postfix'>Назад к запуску</a></p>
            </section>
            """;
        return body;
    }

    private static IResult Html(string title, string body) => Results.Content($"""
        <!doctype html>
        <html lang='ru'>
        <head>
            <meta charset='utf-8'>
            <meta name='viewport' content='width=device-width, initial-scale=1'>
            <title>{H(title)}</title>
            <link rel='stylesheet' href='/css/admin.css'>
            <link rel='stylesheet' href='/css/payment.css'>
        </head>
        <body class='admin-page'>
            <main class='admin-shell'>{body}</main>
        </body>
        </html>
        """, "text/html; charset=utf-8");

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
