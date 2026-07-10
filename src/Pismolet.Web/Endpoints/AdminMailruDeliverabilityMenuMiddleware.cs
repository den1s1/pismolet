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
                var updatedHtml = AddDeliverabilityLink(html);
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

    private static bool IsHtmlResponse(HttpResponse response) =>
        response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;
}
