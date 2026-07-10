using System.Net;
using System.Security.Claims;
using System.Text;

namespace Pismolet.Web.Endpoints;

public static class AdminMailruDeliverabilityMenuMiddleware
{
    private const string DeliverabilityUrl = "/admin/deliverability/mailru";
    private const string DeliverabilityLinkText = ">Доставляемость Mail.ru</a>";

    public static IApplicationBuilder UseAdminMailruDeliverabilityMenuLink(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/admin"))
            {
                await next();
                return;
            }

            var originalBody = context.Response.Body;
            await using var buffer = new MemoryStream();
            context.Response.Body = buffer;

            try
            {
                await next();
                buffer.Position = 0;

                if (!IsHtmlResponse(context.Response) || context.Response.StatusCode != StatusCodes.Status200OK)
                {
                    await buffer.CopyToAsync(originalBody, context.RequestAborted);
                    return;
                }

                using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                var html = await reader.ReadToEndAsync(context.RequestAborted);
                var updatedHtml = AddDeliverabilityLayout(
                    html,
                    context.Request.Path,
                    context.User.FindFirstValue(ClaimTypes.Email));
                var bytes = Encoding.UTF8.GetBytes(updatedHtml);
                context.Response.ContentLength = bytes.Length;
                await originalBody.WriteAsync(bytes, context.RequestAborted);
            }
            finally
            {
                context.Response.Body = originalBody;
            }
        });
    }

    public static string AddDeliverabilityLayout(string html, PathString path, string? adminEmail)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return html;
        }

        if (path.Equals(DeliverabilityUrl, StringComparison.OrdinalIgnoreCase) &&
            !html.Contains("class='admin-shell'", StringComparison.Ordinal))
        {
            return WrapInAdminShell(html, adminEmail);
        }

        return AddDeliverabilityLink(html);
    }

    public static string AddDeliverabilityLink(string html)
    {
        if (string.IsNullOrWhiteSpace(html) || html.Contains(DeliverabilityLinkText, StringComparison.Ordinal))
        {
            return html;
        }

        const string sidebarLinksMarker = "<div class='admin-sidebar-links'>";
        if (html.Contains(sidebarLinksMarker, StringComparison.Ordinal))
        {
            return html.Replace(
                sidebarLinksMarker,
                sidebarLinksMarker + $"<a href='{DeliverabilityUrl}'>Доставляемость Mail.ru</a>",
                StringComparison.Ordinal);
        }

        const string navEndMarker = "</nav>";
        if (html.Contains(navEndMarker, StringComparison.Ordinal))
        {
            return html.Replace(
                navEndMarker,
                $"<a class='admin-nav-link' href='{DeliverabilityUrl}'>Доставляемость Mail.ru</a>{navEndMarker}",
                StringComparison.Ordinal);
        }

        return html;
    }

    private static string WrapInAdminShell(string html, string? adminEmail)
    {
        const string mainStart = "<main class='page'>";
        const string mainEnd = "</main>";
        if (!html.Contains(mainStart, StringComparison.Ordinal) ||
            !html.Contains(mainEnd, StringComparison.Ordinal))
        {
            return html;
        }

        var safeEmail = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(adminEmail)
            ? "admin@example.test"
            : adminEmail);
        var shellStart = $"""
            {mainStart}
            <section class='admin-shell'>
                <aside class='admin-sidebar'>
                    <a class='admin-brand' href='/admin'><span>П</span><b>Письмолёт</b></a>
                    <div class='admin-current'><small>Администратор</small><strong>{safeEmail}</strong></div>
                    <nav class='admin-nav'>
                        <a class='admin-nav-link' href='/admin/users'>Пользователи</a>
                        <a class='admin-nav-link' href='/admin/recipients'>Получатели</a>
                        <a class='admin-nav-link' href='/admin/campaigns'>Кампании</a>
                        <a class='admin-nav-link' href='/admin/payments'>Оплаты</a>
                        <a class='admin-nav-link' href='/admin/settings'>Настройки</a>
                        <a class='admin-nav-link active' href='{DeliverabilityUrl}'>Доставляемость Mail.ru</a>
                    </nav>
                    <div class='admin-sidebar-links'>
                        <a href='/admin/moderation'>Очередь модерации</a>
                        <a href='/admin/limits'>Дневные лимиты</a>
                        <a href='/dashboard'>В ЛК</a>
                    </div>
                </aside>
                <div class='admin-content'>
            """;
        const string shellEnd = """
                </div>
            </section>
            </main>
            """;

        return html
            .Replace(mainStart, shellStart, StringComparison.Ordinal)
            .Replace(mainEnd, shellEnd, StringComparison.Ordinal);
    }

    private static bool IsHtmlResponse(HttpResponse response) =>
        response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;
}
