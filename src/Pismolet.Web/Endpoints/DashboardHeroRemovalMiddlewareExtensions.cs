using System.Text;
using System.Text.RegularExpressions;

namespace Pismolet.Web.Endpoints;

public static class DashboardHeroRemovalMiddlewareExtensions
{
    private static readonly Regex DashboardHeroRegex = new(
        @"\s*<section class='panel quick-start'>\s*<div>\s*<p class='eyebrow'>Личный кабинет</p>.*?</div>\s*</section>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public static IApplicationBuilder UseDashboardHeroRemoval(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (!string.Equals(context.Request.Path.Value, "/dashboard", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await next();

        buffer.Position = 0;
        if (context.Response.StatusCode != StatusCodes.Status200OK || context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) != true)
        {
            context.Response.Body = originalBody;
            await buffer.CopyToAsync(originalBody);
            return;
        }

        using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var html = await reader.ReadToEndAsync();
        var transformed = DashboardHeroRegex.Replace(html, string.Empty);
        var bytes = Encoding.UTF8.GetBytes(transformed);

        context.Response.Body = originalBody;
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes);
    });
}
