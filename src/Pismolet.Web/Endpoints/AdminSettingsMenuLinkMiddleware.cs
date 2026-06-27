using System.Text;

namespace Pismolet.Web.Endpoints;

public static class AdminSettingsMenuLinkMiddleware
{
    public static IApplicationBuilder UseAdminSettingsMenuLink(this IApplicationBuilder app) => app.Use(async (context, next) =>
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
            if (!IsHtml(context.Response.ContentType))
            {
                await buffer.CopyToAsync(originalBody);
                return;
            }

            using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var html = await reader.ReadToEndAsync();
            var normalized = html
                .Replace("href='/admin/settings/mvp'>Настройки</a>", "href='/admin/settings'>Настройки</a>", StringComparison.Ordinal)
                .Replace("href=\"/admin/settings/mvp\">Настройки</a>", "href=\"/admin/settings\">Настройки</a>", StringComparison.Ordinal);

            var bytes = Encoding.UTF8.GetBytes(normalized);
            context.Response.ContentLength = bytes.Length;
            await originalBody.WriteAsync(bytes);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    });

    private static bool IsHtml(string? contentType) =>
        contentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true;
}
