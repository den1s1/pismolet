using System.Security.Claims;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class MailingDeclarationCompatibilityEndpoints
{
    public static IEndpointRouteBuilder MapMailingDeclarationCompatibilityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/mailings/{id:guid}/declaration", ConfirmDeclaration)
            .RequireAuthorization()
            .WithOrder(-3000);
        return app;
    }

    private static async Task<IResult> ConfirmDeclaration(Guid id, HttpContext http, IMailingDeclarationService declarations)
    {
        var email = http.User.FindFirstValue(ClaimTypes.Email);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var form = await http.Request.ReadFormAsync();
        var result = declarations.Confirm(new ConfirmMailingDeclarationCommand(
            email,
            id,
            TryParseBaseSource(form["baseSource"].ToString()),
            form.ContainsKey("baseLegality"),
            form.ContainsKey("advertisingConsent"),
            TryParseMessageType(form["messageType"].ToString()),
            ToRequestMetadata(http)));

        if (!result.Ok || result.Mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Финальное подтверждение", HtmlRenderer.Error(result.Error), authenticated: true));
        }

        return Results.Redirect($"/mailings/{id}/message");
    }

    private static BaseSource? TryParseBaseSource(string? value) => Enum.TryParse<BaseSource>(value, out var source) ? source : null;

    private static MessageType TryParseMessageType(string? value) => Enum.TryParse<MessageType>(value, out var type) ? type : MessageType.Transactional;

    private static RequestMetadata ToRequestMetadata(HttpContext http) => new(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        string.IsNullOrWhiteSpace(http.Request.Headers.UserAgent.ToString()) ? "unknown" : http.Request.Headers.UserAgent.ToString());
}
