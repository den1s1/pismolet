using System.Security.Claims;
using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class AdminModerationAutoLaunchEndpoints
{
    public static IEndpointRouteBuilder MapAdminModerationAutoLaunchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/admin/moderation/{reviewId:guid}/approve", Approve)
            .RequireAuthorization(AdminEndpoints.AdminPolicyName)
            .WithOrder(-1000);
        return app;
    }

    private static async Task<IResult> Approve(
        Guid reviewId,
        HttpContext http,
        IModerationAdminService moderation,
        IMailingSendService sender,
        IAuditLogger auditLogger)
    {
        var email = CurrentEmail(http);
        if (email is null)
        {
            return Results.Redirect("/account/login");
        }

        var request = ToRequestMetadata(http);
        var form = await http.Request.ReadFormAsync();
        var result = moderation.Approve(reviewId, email, form["comment"].ToString(), request);
        if (!result.Ok || result.Mailing is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Ошибка модерации", HtmlRenderer.Error(result.Error), authenticated: true));
        }

        var launch = sender.StartSending(result.Mailing.OwnerEmail, result.Mailing.Id, request);
        AuditAutoLaunch(auditLogger, email, request, reviewId, result.Mailing.Id, launch);

        return Results.Redirect($"/admin/moderation/{reviewId}");
    }

    private static void AuditAutoLaunch(
        IAuditLogger auditLogger,
        string adminEmail,
        RequestMetadata request,
        Guid reviewId,
        Guid mailingId,
        MailingSendResult launch)
    {
        var eventType = launch.Ok
            ? "mailing_auto_send_started_after_admin_approve"
            : "mailing_auto_send_start_failed_after_admin_approve";
        var status = launch.Ok ? "queued" : "failed";
        var error = JsonValue(launch.Ok ? null : launch.Error);
        auditLogger.Write(new AuditRecord(
            DateTimeOffset.UtcNow,
            adminEmail,
            eventType,
            request.Ip,
            request.UserAgent,
            $"{{\"mailingId\":\"{mailingId}\",\"reviewId\":\"{reviewId}\",\"status\":\"{status}\",\"error\":{error}}}"));
    }

    private static string JsonValue(string? value) => string.IsNullOrWhiteSpace(value)
        ? "null"
        : $"\"{EscapeJson(value)}\"";

    private static string EscapeJson(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string? CurrentEmail(HttpContext http) => http.User.FindFirstValue(ClaimTypes.Email);

    private static RequestMetadata ToRequestMetadata(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = http.Request.Headers.UserAgent.ToString();
        return new RequestMetadata(ip, string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);
    }
}
