using Pismolet.Web.Infrastructure.Postmaster;

namespace Pismolet.Web.Endpoints;

public static class MailruPostmasterDiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapMailruPostmasterDiagnosticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/admin/integrations/mailru-postmaster/diagnostics", RunDiagnostics)
            .RequireAuthorization(AdminEndpoints.AdminPolicyName);
        return endpoints;
    }

    private static async Task<IResult> RunDiagnostics(
        MailruPostmasterOptions options,
        IMailruPostmasterClient client,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return Results.Ok(new
            {
                enabled = false,
                configured = false,
                domain = options.Domain,
                status = "disabled",
                checkedAt = DateTimeOffset.UtcNow
            });
        }

        if (!options.IsConfigured)
        {
            return Results.Json(new
            {
                enabled = true,
                configured = false,
                domain = options.Domain,
                status = "not_configured",
                error = "Не задан refresh_token Mail.ru Postmaster.",
                checkedAt = DateTimeOffset.UtcNow
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var domains = await client.GetRegisteredDomainsAsync(cancellationToken);
        if (!domains.IsSuccess)
        {
            return Results.Json(new
            {
                enabled = true,
                configured = true,
                domain = options.Domain,
                status = "api_error",
                errorCode = domains.ErrorCode,
                error = domains.ErrorMessage,
                httpStatusCode = domains.HttpStatusCode,
                retryAfterSeconds = domains.RetryAfter?.TotalSeconds,
                checkedAt = DateTimeOffset.UtcNow
            }, statusCode: StatusCodes.Status502BadGateway);
        }

        var troubles = await client.GetTroublesAsync(cancellationToken);
        if (!troubles.IsSuccess)
        {
            return Results.Json(new
            {
                enabled = true,
                configured = true,
                domain = options.Domain,
                status = "partial_api_error",
                registeredDomain = domains.Data?.Any(x => x.Domain.Equals(options.Domain, StringComparison.OrdinalIgnoreCase)) == true,
                errorCode = troubles.ErrorCode,
                error = troubles.ErrorMessage,
                httpStatusCode = troubles.HttpStatusCode,
                retryAfterSeconds = troubles.RetryAfter?.TotalSeconds,
                checkedAt = DateTimeOffset.UtcNow
            }, statusCode: StatusCodes.Status502BadGateway);
        }

        var domainRegistered = domains.Data?.Any(x => x.Domain.Equals(options.Domain, StringComparison.OrdinalIgnoreCase)) == true;
        var domainTroubles = troubles.Data?
            .Where(x => x.Domain.Equals(options.Domain, StringComparison.OrdinalIgnoreCase))
            .Select(x => new { x.Code, x.Message })
            .ToArray() ?? [];

        return Results.Ok(new
        {
            enabled = true,
            configured = true,
            domain = options.Domain,
            status = domainRegistered ? "ok" : "domain_not_registered",
            registeredDomain = domainRegistered,
            troubles = domainTroubles,
            checkedAt = DateTimeOffset.UtcNow
        });
    }
}
