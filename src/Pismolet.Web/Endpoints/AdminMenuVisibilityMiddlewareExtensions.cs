using System.Security.Claims;
using System.Text;
using Pismolet.Web.Application.Common;

namespace Pismolet.Web.Endpoints;

public static class AdminMenuVisibilityMiddlewareExtensions
{
    private const string AdminMenuLink = "<a href='/admin'>Админка</a>";

    public static IApplicationBuilder UseAdminMenuVisibility(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await next();

        context.Response.Body = originalBody;
        buffer.Position = 0;

        if (!ShouldFilter(context))
        {
            await buffer.CopyToAsync(originalBody);
            return;
        }

        using var reader = new StreamReader(buffer, Encoding.UTF8);
        var html = await reader.ReadToEndAsync();
        html = html.Replace(AdminMenuLink, string.Empty, StringComparison.Ordinal);
        context.Response.ContentLength = null;
        await context.Response.WriteAsync(html);
    });

    private static bool ShouldFilter(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (IsAdmin(context))
        {
            return false;
        }

        return context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsAdmin(HttpContext context)
    {
        var email = context.User.FindFirstValue(ClaimTypes.Email);
        return !string.IsNullOrWhiteSpace(email) && context.RequestServices.GetRequiredService<IAdminAccessService>().IsAdminEmail(email);
    }
}
