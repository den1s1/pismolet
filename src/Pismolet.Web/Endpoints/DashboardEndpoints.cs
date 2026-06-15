using System.Security.Claims;
using Pismolet.Web.Application.Auth;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dashboard", (HttpContext http, IUserAccountService accounts) =>
        {
            var email = http.User.FindFirstValue(ClaimTypes.Email);
            if (email is null)
            {
                return Results.Redirect("/account/login");
            }

            var user = accounts.GetByEmail(email);
            if (user is null)
            {
                return Results.Redirect("/account/login");
            }

            return HtmlRenderer.Html(HtmlRenderer.Page("Личный кабинет", HtmlRenderer.Dashboard(user)));
        }).RequireAuthorization();

        return app;
    }
}
