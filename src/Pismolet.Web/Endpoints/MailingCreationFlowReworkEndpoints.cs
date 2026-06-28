using System.Security.Claims;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class MailingCreationFlowReworkEndpoints
{
    private const string InitialMailingSubject = "Новая рассылка";

    public static IEndpointRouteBuilder MapMailingCreationFlowReworkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mailings/new", StartNewMailing).RequireAuthorization().WithOrder(-1000);
        app.MapPost("/mailings", CreateMailing).RequireAuthorization().WithOrder(-1000);
        return app;
    }

    private static IResult StartNewMailing(HttpContext http, IMailingService mailings)
    {
        var email = CurrentEmail(http);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var result = mailings.CreateDraft(new CreateMailingCommand(email, InitialMailingSubject), ToRequestMetadata(http));
        if (!result.Ok || result.Mailing is null)
        {
            return ErrorPage(result.Error);
        }

        return Results.Redirect($"/mailings/{result.Mailing.Id}/message");
    }

    private static async Task<IResult> CreateMailing(HttpContext http, IMailingService mailings)
    {
        var email = CurrentEmail(http);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var form = await http.Request.ReadFormAsync();
        var subject = form["subject"].ToString();
        if (string.IsNullOrWhiteSpace(subject))
        {
            subject = InitialMailingSubject;
        }

        var result = mailings.CreateDraft(new CreateMailingCommand(email, subject), ToRequestMetadata(http));
        if (!result.Ok || result.Mailing is null)
        {
            return ErrorPage(result.Error);
        }

        return Results.Redirect($"/mailings/{result.Mailing.Id}/message");
    }

    private static IResult ErrorPage(string message) => HtmlRenderer.Html(HtmlRenderer.Page("Ошибка", HtmlRenderer.Error(message), authenticated: true));

    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);

    private static RequestMetadata ToRequestMetadata(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = http.Request.Headers.UserAgent.ToString();
        return new RequestMetadata(ip, string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);
    }
}
