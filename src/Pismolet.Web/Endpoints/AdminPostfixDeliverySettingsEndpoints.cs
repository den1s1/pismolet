using System.Net;
using Pismolet.Web.Infrastructure.Mail;

namespace Pismolet.Web.Endpoints;

public static class AdminPostfixDeliverySettingsEndpoints
{
    public static IEndpointRouteBuilder MapAdminPostfixDeliverySettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/delivery/postfix/settings", ShowSettings).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        app.MapPost("/admin/delivery/postfix/settings", SaveSettings).RequireAuthorization(AdminEndpoints.AdminPolicyName);
        return app;
    }

    private static IResult ShowSettings(HttpRequest request, IPostfixDeliveryAutomationSettingsRepository repository)
    {
        var settings = repository.Get().Normalize();
        var saved = string.Equals(request.Query["saved"], "1", StringComparison.Ordinal);
        var checkedAttribute = settings.Enabled ? " checked" : string.Empty;
        var savedNotice = saved ? "<p class='admin-muted'><b>Настройки сохранены.</b></p>" : string.Empty;
        var body = $"""
            <section class='admin-panel'>
                <p class='eyebrow'>Доставка</p>
                <h1>Настройки Postfix delivery reader</h1>
                <p class='admin-muted'>Автоматическое чтение новых строк Postfix-лога. Текущий интервал: {settings.IntervalSeconds} сек.</p>
                {savedNotice}
                <form method='post' action='/admin/delivery/postfix/settings'>
                    <p><label><input type='checkbox' name='enabled' value='true'{checkedAttribute}> Включить автоматическое чтение</label></p>
                    <p><label>Интервал, секунд: <input name='intervalSeconds' type='number' min='{PostfixDeliveryAutomationSettings.MinIntervalSeconds}' max='{PostfixDeliveryAutomationSettings.MaxIntervalSeconds}' value='{settings.IntervalSeconds}'></label></p>
                    <button class='admin-button' type='submit'>Сохранить</button>
                    <a class='admin-link' href='/admin/delivery/postfix'>Вернуться к чтению лога</a>
                </form>
            </section>
            """;
        return Html("Настройки Postfix delivery reader", body);
    }

    private static async Task<IResult> SaveSettings(HttpRequest request, IPostfixDeliveryAutomationSettingsRepository repository)
    {
        var form = await request.ReadFormAsync();
        var enabled = form.ContainsKey("enabled");
        var intervalSeconds = PostfixDeliveryAutomationSettings.DefaultIntervalSeconds;
        if (int.TryParse(form["intervalSeconds"], out var parsed))
        {
            intervalSeconds = parsed;
        }

        repository.Save(new PostfixDeliveryAutomationSettings(enabled, intervalSeconds));
        return Results.Redirect("/admin/delivery/postfix/settings?saved=1");
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
