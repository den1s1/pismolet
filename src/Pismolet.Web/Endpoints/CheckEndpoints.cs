using System.Security.Claims;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;

namespace Pismolet.Web.Endpoints;

public static class CheckEndpoints
{
    public static IEndpointRouteBuilder MapCheckEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mailings/{id:guid}/checks", ShowChecks).RequireAuthorization();
        app.MapPost("/mailings/{id:guid}/checks/start", StartChecks).RequireAuthorization();
        return app;
    }

    private static IResult ShowChecks(Guid id, HttpContext http)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        return Results.Redirect($"/mailings/{id}/send");
    }

    private static IResult StartChecks(Guid id, HttpContext http, IMailingReviewService reviews)
    {
        var email = CurrentEmail(http);
        if (email is null) return Results.Redirect("/account/login");
        reviews.StartChecks(email, id, ToRequestMetadata(http));
        return Results.Redirect($"/mailings/{id}/send");
    }

    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);

    private static RequestMetadata ToRequestMetadata(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = http.Request.Headers.UserAgent.ToString();
        return new RequestMetadata(ip, string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);
    }

}
