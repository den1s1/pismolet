using Pismolet.Web.Application.Common;

namespace Pismolet.Web.Endpoints;

internal static class RequestMetadataMapper
{
    public static RequestMetadata ToRequestMetadata(this HttpContext http) => new(
        Ip: http.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
        UserAgent: http.Request.Headers.UserAgent.ToString());
}
