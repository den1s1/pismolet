using System.Security.Claims;
using Pismolet.Web.Application.Auth;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/profile", ShowProfile).RequireAuthorization();
        app.MapGet("/payments", ShowPayments).RequireAuthorization();
        return app;
    }

    private static IResult ShowProfile(HttpContext http, IUserAccountService accounts)
    {
        var email = CurrentEmail(http);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var user = accounts.GetByEmail(email);
        if (user is null)
        {
            return Results.Redirect("/account/login");
        }

        return HtmlRenderer.Html(HtmlRenderer.Page("Профиль", HtmlRenderer.UserProfile(user), authenticated: true));
    }

    private static IResult ShowPayments(HttpContext http, IUserAccountService accounts, IMailingService mailings)
    {
        var email = CurrentEmail(http);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var user = accounts.GetByEmail(email);
        if (user is null)
        {
            return Results.Redirect("/account/login");
        }

        var shownUser = user with { Mailings = mailings.ListForOwner(email).ToList() };
        return HtmlRenderer.Html(HtmlRenderer.Page("Платежи", HtmlRenderer.Payments(shownUser), authenticated: true));
    }

    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);
}
