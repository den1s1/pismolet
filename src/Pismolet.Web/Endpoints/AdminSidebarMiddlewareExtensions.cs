using System.Text;

namespace Pismolet.Web.Endpoints;

public static class AdminSidebarMiddlewareExtensions
{
    public static IApplicationBuilder UseUnifiedAdminSidebar(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (!context.Request.Path.StartsWithSegments("/admin"))
        {
            await next();
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await next();

        context.Response.Body = originalBody;
        buffer.Position = 0;

        if (context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) != true)
        {
            await buffer.CopyToAsync(originalBody);
            return;
        }

        using var reader = new StreamReader(buffer, Encoding.UTF8);
        var html = await reader.ReadToEndAsync();
        html = NormalizeSidebar(html, context.Request.Path.Value ?? string.Empty);
        context.Response.ContentLength = null;
        await context.Response.WriteAsync(html);
    });

    private static string NormalizeSidebar(string html, string path)
    {
        var navStart = html.IndexOf("<nav class='admin-nav'>", StringComparison.Ordinal);
        if (navStart < 0)
        {
            return html;
        }

        var navEnd = html.IndexOf("</nav>", navStart, StringComparison.Ordinal);
        if (navEnd < 0)
        {
            return html;
        }

        navEnd += "</nav>".Length;
        var sidebarNav = BuildNav(path);
        var updated = html[..navStart] + sidebarNav + html[navEnd..];
        return RemoveSidebarLinks(updated);
    }

    private static string RemoveSidebarLinks(string html)
    {
        var linksStart = html.IndexOf("<div class='admin-sidebar-links'>", StringComparison.Ordinal);
        if (linksStart < 0)
        {
            return html;
        }

        var linksEnd = html.IndexOf("</div>", linksStart, StringComparison.Ordinal);
        if (linksEnd < 0)
        {
            return html;
        }

        linksEnd += "</div>".Length;
        return html[..linksStart] + html[linksEnd..];
    }

    private static string BuildNav(string path) => $"""
                <nav class='admin-nav'>
                    {Link(path, "/admin", "Dashboard")}
                    {Link(path, "/admin/users", "Пользователи")}
                    {Link(path, "/admin/campaigns", "Рассылки")}
                    {Link(path, "/admin/moderation", "Модерация")}
                    {Link(path, "/admin/imports", "Импорты")}
                    {Link(path, "/admin/payments", "Платежи")}
                    {Link(path, "/admin/recipients", "Отписки")}
                    {Link(path, "/admin/complaints", "Жалобы")}
                    {Link(path, "/admin/delivery-errors", "Ошибки доставки")}
                    {Link(path, "/admin/replies", "Ответы")}
                    {Link(path, "/admin/audit", "Audit log")}
                    {Link(path, "/admin/settings/mvp", "Настройки")}
                </nav>
        """;

    private static string Link(string currentPath, string href, string text)
    {
        var active = IsActive(currentPath, href) ? " active" : string.Empty;
        return $"<a class='admin-nav-link{active}' href='{href}'>{text}</a>";
    }

    private static bool IsActive(string currentPath, string href)
    {
        if (href == "/admin")
        {
            return string.Equals(currentPath, "/admin", StringComparison.OrdinalIgnoreCase);
        }

        return currentPath.StartsWith(href, StringComparison.OrdinalIgnoreCase);
    }
}
