using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pismolet.Web.Application.Auth;
using Pismolet.Web.Application.Legal;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Legal;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class ProfileEndpoints
{
    private const string ClientProfileConfirmationDocumentKey = "client_profile_confirmation";

    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/profile", ShowProfile).RequireAuthorization();
        app.MapPost("/profile/confirm", ConfirmProfile).RequireAuthorization();
        app.MapGet("/payments", ShowPayments).RequireAuthorization();
        return app;
    }

    private static IResult ShowProfile(HttpContext http, [FromServices] IUserAccountService accounts)
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

        var confirmed = string.Equals(http.Request.Query["confirmed"], "1", StringComparison.Ordinal);
        return HtmlRenderer.Html(HtmlRenderer.Page("Профиль", HtmlRenderer.UserProfile(user, confirmed), authenticated: true));
    }

    private static IResult ConfirmProfile(
        HttpContext http,
        [FromServices] IUserAccountService accounts,
        [FromServices] ILegalEvidenceService legalEvidence)
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

        var snapshot = LegalEvidenceTextSnapshots.ClientProfileConfirmationText;
        legalEvidence.RecordEvent(new LegalEvidenceEventDraft(
            LegalEventTypes.ClientProfileConfirmed,
            user.Email,
            user.Email,
            null,
            null,
            ClientProfileConfirmationDocumentKey,
            LegalEvidenceTextSnapshots.CurrentVersion,
            legalEvidence.ComputeTextHash(snapshot),
            snapshot,
            LegalEventResults.Confirmed,
            http.Connection.RemoteIpAddress?.ToString(),
            http.Request.Headers.UserAgent.ToString(),
            http.Request.Path.ToString(),
            JsonSerializer.Serialize(new
            {
                user.Email,
                user.DisplayName,
                ReplyToEmail = user.Email,
                DefaultSenderName = "Письмолёт",
                user.Profile.Status,
                user.Profile.DailySendLimit,
                user.Profile.TotalSendLimit,
                user.Profile.PremoderationRequired
            })));

        return Results.Redirect("/profile?confirmed=1");
    }

    private static IResult ShowPayments(HttpContext http, [FromServices] IUserAccountService accounts, [FromServices] IMailingService mailings)
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
