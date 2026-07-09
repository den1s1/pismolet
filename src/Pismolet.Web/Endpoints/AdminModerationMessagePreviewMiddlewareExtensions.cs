using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Pismolet.Web.Endpoints;

public static class AdminModerationMessagePreviewMiddlewareExtensions
{
    private const string MessageSectionStart = "<h2>Письмо</h2>";
    private const string ServiceBlocksStart = "<h2>Служебные блоки</h2>";
    private static readonly Regex BodyPreRegex = new("<pre>(?<body>.*?)</pre>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex HtmlBodyRegex = new(@"<\s*(?:!doctype|html|head|body|table|div|p|section|article|h[1-6]|img|a|span|br)(?:\s|>|/)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        var renderedPreview = BuildRenderedPreview(messageContent);
        var replacement = $"<details class='admin-message-preview'><summary class='admin-button'>Просмотр письма</summary><div class='admin-message-preview-content' style='display:grid;gap:20px;margin-top:18px;'><section class='admin-message-source'><h3>Исходный текст</h3>{messageContent}</section><section class='admin-message-rendered'><h3>Как будет выглядеть письмо</h3>{renderedPreview}<p class='admin-muted'>Предпросмотр показывает письмо в отдельном безопасном окне. В конкретном почтовом клиенте возможны небольшие отличия.</p></section></div></details>{ServiceBlocksStart}";
        return html[..sectionStart] + replacement + html[(serviceBlocksStart + ServiceBlocksStart.Length)..];
    }

    private static string BuildRenderedPreview(string messageContent)
    {
        var match = BodyPreRegex.Match(messageContent);
        if (!match.Success)
        {
            return "<p class='admin-muted'>Не удалось сформировать визуальный предпросмотр письма.</p>";
        }

        var source = WebUtility.HtmlDecode(match.Groups["body"].Value).Trim();
        var document = HtmlBodyRegex.IsMatch(source)
            ? source
            : BuildPlainTextDocument(source);
        var encodedDocument = WebUtility.HtmlEncode(document);

        return $"<iframe class='admin-email-preview-frame' title='Предпросмотр письма' sandbox loading='lazy' referrerpolicy='no-referrer' srcdoc='{encodedDocument}' style='display:block;width:100%;min-height:680px;border:1px solid #dbe4ef;border-radius:16px;background:#fff;'></iframe>";
    }

    private static string BuildPlainTextDocument(string source)
    {
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var body = WebUtility.HtmlEncode(normalized).Replace("\n", "<br>\n", StringComparison.Ordinal);
        return $"<!doctype html><html lang='ru'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'></head><body style='margin:0;padding:24px;background:#fff;color:#222;font-family:Arial,Helvetica,sans-serif;font-size:16px;line-height:1.55;'><div>{body}</div></body></html>";
    }

    private static bool ShouldTransform(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method) &&
        context.Request.Path.StartsWithSegments("/admin/moderation", out var remaining) &&
        remaining.HasValue &&
        remaining.Value!.StartsWith("/", StringComparison.Ordinal) &&
        context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true;
}
