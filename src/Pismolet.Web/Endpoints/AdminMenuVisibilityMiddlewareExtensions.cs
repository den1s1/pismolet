using System.Security.Claims;
using System.Text;

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
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        return ReadAdminEmails(configuration).Contains(email);
    }

    private static IReadOnlySet<string> ReadAdminEmails(IConfiguration configuration)
    {
        var values = new List<string>();
        values.AddRange(Split(configuration["Admin:AllowedEmails"]));
        values.AddRange(Split(configuration["Admin:Emails"]));
        values.AddRange(Split(configuration["Pismolet:AdminEmails"]));
        values.AddRange(Split(configuration["PISMOLET_ADMIN_EMAILS"]));
        values.AddRange(Split(Environment.GetEnvironmentVariable("PISMOLET_ADMIN_EMAILS")));

        foreach (var child in configuration.GetSection("Admin:AllowedEmails").GetChildren())
        {
            values.AddRange(Split(child.Value));
        }

        return values
            .Select(item => item.Trim().ToLowerInvariant())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Split(string? value) => string.IsNullOrWhiteSpace(value)
        ? Array.Empty<string>()
        : value.Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
