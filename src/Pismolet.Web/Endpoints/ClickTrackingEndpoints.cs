using System.Security.Cryptography;
using System.Text;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Endpoints;

public static class ClickTrackingEndpoints
{
    public static IEndpointRouteBuilder MapClickTrackingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/t/click/{token}", RecordClick);
        return app;
    }

    private static IResult RecordClick(string token, IClickTrackingRepository clickTracking, HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        http.Response.Headers.Pragma = "no-cache";
        http.Response.Headers.Expires = "0";

        var normalizedToken = token.Trim();
        if (string.IsNullOrWhiteSpace(normalizedToken) || clickTracking.GetByToken(normalizedToken) is not { } trackedLink)
        {
            return Results.NotFound("Tracked link not found.");
        }

        var clickedAt = DateTimeOffset.UtcNow;
        var updatedLink = trackedLink.MarkClicked(clickedAt);
        clickTracking.SaveLink(updatedLink);
        clickTracking.AddEvent(ClickEvent.Create(
            updatedLink,
            clickedAt,
            Hash(http.Connection.RemoteIpAddress?.ToString()),
            Hash(http.Request.Headers.UserAgent.ToString())));

        return Results.Redirect(updatedLink.OriginalUrl, permanent: false, preserveMethod: false);
    }

    private static string? Hash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
