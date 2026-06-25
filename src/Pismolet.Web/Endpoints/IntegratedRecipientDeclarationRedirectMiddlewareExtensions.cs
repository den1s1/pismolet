using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Endpoints;

public static class IntegratedRecipientDeclarationRedirectMiddlewareExtensions
{
    public static IApplicationBuilder UseIntegratedRecipientDeclarationRedirect(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (!IsRecipientPost(context))
        {
            await next();
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await next();

        if (context.Response.StatusCode == StatusCodes.Status200OK && await TryConfirm(context))
        {
            context.Response.Body = originalBody;
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = $"/mailings/{ExtractMailingId(context.Request.Path)}/message";
            context.Response.ContentLength = 0;
            return;
        }

        buffer.Position = 0;
        context.Response.Body = originalBody;
        await buffer.CopyToAsync(originalBody);
    });

    private static bool IsRecipientPost(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        return HttpMethods.IsPost(context.Request.Method)
            && path.StartsWith("/mailings/", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith("/recipients", StringComparison.OrdinalIgnoreCase)
            && context.Request.HasFormContentType;
    }

    private static async Task<bool> TryConfirm(HttpContext context)
    {
        var id = ExtractMailingId(context.Request.Path);
        if (id == Guid.Empty) return false;
        var email = context.User.FindFirstValue(ClaimTypes.Email);
        if (email is null) return false;
        var form = await context.Request.ReadFormAsync();
        if (!form.ContainsKey("baseSource") || !form.ContainsKey("baseLegality")) return false;
        var type = ParseType(form["messageType"].ToString());
        if (type == MessageType.Advertising && !form.ContainsKey("advertisingConsent")) return false;
        var mailings = context.RequestServices.GetRequiredService<IMailingService>();
        var mailing = mailings.GetForOwner(id, email);
        if (mailing is null || mailing.LastImportStats.Accepted <= 0) return false;
        var declarations = context.RequestServices.GetRequiredService<IMailingDeclarationService>();
        var result = declarations.Confirm(new ConfirmMailingDeclarationCommand(email, id, ParseBase(form["baseSource"].ToString()), true, form.ContainsKey("advertisingConsent"), type, Request(context)));
        return result.Ok;
    }

    private static Guid ExtractMailingId(PathString path)
    {
        var parts = (path.Value ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && Guid.TryParse(parts[1], out var id) ? id : Guid.Empty;
    }

    private static BaseSource? ParseBase(string value) => Enum.TryParse<BaseSource>(value, out var source) ? source : null;
    private static MessageType ParseType(string value) => Enum.TryParse<MessageType>(value, out var type) ? type : MessageType.Transactional;
    private static RequestMetadata Request(HttpContext http) => new(http.Connection.RemoteIpAddress?.ToString() ?? "unknown", string.IsNullOrWhiteSpace(http.Request.Headers.UserAgent.ToString()) ? "unknown" : http.Request.Headers.UserAgent.ToString());
}
