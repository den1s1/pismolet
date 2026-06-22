using Pismolet.Web.Application.Persistence;

namespace Pismolet.Web.Endpoints;

public static class OpenTrackingEndpoints
{
    private static readonly byte[] TransparentGif =
    [
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00,
        0x01, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xff, 0xff, 0xff, 0x21, 0xf9, 0x04, 0x01, 0x00,
        0x00, 0x00, 0x00, 0x2c, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x44,
        0x01, 0x00, 0x3b
    ];

    public static IEndpointRouteBuilder MapOpenTrackingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/t/open/{token}.gif", RecordOpen);
        return app;
    }

    private static IResult RecordOpen(string token, ISendEventRepository sendEvents, HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        http.Response.Headers.Pragma = "no-cache";
        http.Response.Headers.Expires = "0";

        var normalizedToken = token.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedToken) && sendEvents.GetByTrackingToken(normalizedToken) is { } sendEvent)
        {
            sendEvents.Save(sendEvent.MarkOpened(DateTimeOffset.UtcNow));
        }

        return Results.File(TransparentGif, "image/gif");
    }
}
