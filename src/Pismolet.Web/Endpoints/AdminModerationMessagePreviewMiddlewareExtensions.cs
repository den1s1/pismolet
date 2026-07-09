using System.Text;

namespace Pismolet.Web.Endpoints;

public static class AdminModerationMessagePreviewMiddlewareExtensions
{
    private const string MessageSectionStart = "<h2>Письмо</h2>";
    private const string ServiceBlocksStart = "<h2>Служебные блоки</h2>";

    public static IApplicationBuilder UseAdminModerationMessagePreview(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await next();

        context.Response.Body = originalBody;
        buffer.Position = 0;

        if (!ShouldTransform(context))
        {
            await buffer.CopyToAsync(originalBody);
            return;
        }

        using var reader = new StreamReader(buffer, Encoding.UTF8);
        var html = await reader.ReadToEndAsync();
        html = TransformHtml(html);
        context.Response.ContentLength = null;
        await context.Response.WriteAsync(html);
    });

    public static string TransformHtml(string html)
    {
        var sectionStart = html.IndexOf(MessageSectionStart, StringComparison.Ordinal);
        if (sectionStart < 0)
        {
            return html;
        }

        var contentStart = sectionStart + MessageSectionStart.Length;
        var serviceBlocksStart = html.IndexOf(ServiceBlocksStart, contentStart, StringComparison.Ordinal);
        if (serviceBlocksStart < 0)
        {
            return html;
        }

        var messageContent = html[contentStart..serviceBlocksStart];
        var replacement = $"<details class='admin-message-preview'><summary class='admin-button'>Просмотр письма</summary><div class='admin-message-preview-content'>{messageContent}</div></details>{ServiceBlocksStart}";
        return html[..sectionStart] + replacement + html[(serviceBlocksStart + ServiceBlocksStart.Length)..];
    }

    private static bool ShouldTransform(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method) &&
        context.Request.Path.StartsWithSegments("/admin/moderation", out var remaining) &&
        remaining.HasValue &&
        remaining.Value!.StartsWith("/", StringComparison.Ordinal) &&
        context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true;
}
