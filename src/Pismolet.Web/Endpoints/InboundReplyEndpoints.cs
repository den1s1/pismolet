using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mail;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class InboundReplyEndpoints
{
    public static IEndpointRouteBuilder MapInboundReplyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/email/fake/inbound", ReceiveFakeInbound);
        app.MapPost("/webhooks/email/{provider}/inbound", ReceiveProviderInbound);
        return app;
    }

    public static IEndpointRouteBuilder MapDevInboundReplyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dev/replies/fake", ShowFakeReplies);
        app.MapPost("/dev/replies/fake", SendFakeReply);
        app.MapPost("/dev/replies/cleanup", RunCleanup);
        return app;
    }

    private static Task<IResult> ReceiveProviderInbound(string provider, HttpContext http, IEmailProviderAdapter adapter, IInboundReplyProcessingService processor, IConfiguration configuration, IHostEnvironment environment) =>
        Receive(http, adapter, processor, configuration, environment);

    private static Task<IResult> ReceiveFakeInbound(HttpContext http, IEmailProviderAdapter adapter, IInboundReplyProcessingService processor, IConfiguration configuration, IHostEnvironment environment) =>
        Receive(http, adapter, processor, configuration, environment);

    private static async Task<IResult> Receive(HttpContext http, IEmailProviderAdapter adapter, IInboundReplyProcessingService processor, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!IsWebhookAllowed(http, configuration, environment))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        using var reader = new StreamReader(http.Request.Body);
        var raw = await reader.ReadToEndAsync();
        var headers = http.Request.Headers.ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var parsed = await adapter.ParseInboundWebhookAsync(raw, headers, http.RequestAborted);
        if (!parsed.Ok || parsed.Event is null)
        {
            return Results.BadRequest(new { status = "invalid_payload" });
        }

        var result = await processor.ProcessAsync(parsed.Event, ToRequestMetadata(http), http.RequestAborted);
        return Results.Ok(new { status = result.Status, correlationId = result.CorrelationId });
    }

    private static IResult ShowFakeReplies(IFakeMailer fakeMailer, IReplyEventRepository replies, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!IsDevSenderAllowed(configuration, environment))
        {
            return Results.NotFound();
        }

        var sentRows = fakeMailer.GetOutbox()
            .Where(x => !string.IsNullOrWhiteSpace(x.ReplyToken) && !string.IsNullOrWhiteSpace(x.ProviderMessageId))
            .Take(50)
            .Select(x => $"<tr><td>{H(MaskEmail(x.To))}</td><td>{H(x.Subject)}</td><td>{H(x.ProviderMessageId)}</td><td>{H(x.ReplyToAddress)}</td><td><form method='post' action='/dev/replies/fake'><input name='providerMessageId' value='{H(x.ProviderMessageId)}'><input name='replyToken' value='{H(x.ReplyToken)}'><input name='to' value='{H(x.ReplyToAddress)}'><input name='from' value='recipient@example.test'><input name='subject' value='Re: {H(x.Subject)}'><textarea name='textBody'>Спасибо, получил письмо.</textarea><button class='button'>Ответить</button></form></td></tr>");
        var replyRows = replies.ListRecent(20)
            .Select(x => $"<tr><td>{H(x.ReceivedAt.ToString("yyyy-MM-dd HH:mm"))}</td><td>{H(x.ProcessingStatus.ToRu())}</td><td>{H(x.SubjectPreview)}</td><td>{H(x.BodyStorageStatus.ToString())}</td><td>{H(x.ErrorCode ?? "")}</td></tr>");
        var body = "<section class='card'><h1>Fake inbound replies</h1><p class='muted'>Dev-инструмент для проверки ответов получателей.</p>" +
            $"<h2>Fake-письма с reply identity</h2><table><tbody>{string.Join(string.Empty, sentRows.DefaultIfEmpty("<tr><td>Нет fake-писем.</td></tr>"))}</tbody></table>" +
            $"<h2>Последние ReplyEvent</h2><table><tbody>{string.Join(string.Empty, replyRows.DefaultIfEmpty("<tr><td>Ответов пока нет.</td></tr>"))}</tbody></table>" +
            "<form method='post' action='/dev/replies/cleanup'><button class='button secondary'>Запустить cleanup</button></form></section>";
        return HtmlRenderer.Html(HtmlRenderer.Page("Fake inbound replies", body));
    }

    private static async Task<IResult> SendFakeReply(HttpContext http, IInboundReplyProcessingService processor, IEmailProviderAdapter adapter, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!IsDevSenderAllowed(configuration, environment))
        {
            return Results.NotFound();
        }

        var providerMessageId = http.Request.Form["providerMessageId"].ToString().Trim();
        var from = http.Request.Form["from"].ToString().Trim();
        var to = http.Request.Form["to"].ToString().Trim();
        var replyToken = http.Request.Form["replyToken"].ToString().Trim();
        var subject = http.Request.Form["subject"].ToString().Trim();
        var textBody = http.Request.Form["textBody"].ToString().Trim();
        var providerInboundEventId = FakeEmailProviderAdapter.BuildProviderInboundEventId(providerMessageId, from + subject);
        var payload = JsonSerializer.Serialize(new { providerInboundEventId, from, to, replyToken, subject, textBody, receivedAt = DateTimeOffset.UtcNow });
        var parsed = await adapter.ParseInboundWebhookAsync(payload, new Dictionary<string, string>(), http.RequestAborted);
        if (!parsed.Ok || parsed.Event is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Fake inbound replies", HtmlRenderer.Error("Некорректный fake inbound payload.")));
        }

        var result = await processor.ProcessAsync(parsed.Event, ToRequestMetadata(http), http.RequestAborted);
        return HtmlRenderer.Html(HtmlRenderer.Page("Fake inbound replies", $"<section class='card'><h1>Fake inbound replies</h1><p class='success'>Ответ обработан: {H(result.Status)}</p><p><a href='/dev/replies/fake'>Назад</a></p></section>"));
    }

    private static async Task<IResult> RunCleanup(IInboundReplyProcessingService processor, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!IsDevSenderAllowed(configuration, environment))
        {
            return Results.NotFound();
        }

        var count = await processor.CleanupExpiredBodiesAsync(CancellationToken.None);
        return HtmlRenderer.Html(HtmlRenderer.Page("Cleanup входящих ответов", $"<section class='card'><p class='success'>Удалено тел ответов: {count}</p><p><a href='/dev/replies/fake'>Назад</a></p></section>"));
    }

    private static bool IsWebhookAllowed(HttpContext http, IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredSecret = configuration["Webhooks:FakeProviderSecret"];
        var secret = string.IsNullOrWhiteSpace(configuredSecret) && environment.IsDevelopment() ? "dev-fake-webhook-secret" : configuredSecret;
        return !string.IsNullOrWhiteSpace(secret) && string.Equals(http.Request.Headers["X-Pismolet-Webhook-Secret"].ToString(), secret, StringComparison.Ordinal);
    }

    private static bool IsDevSenderAllowed(IConfiguration configuration, IHostEnvironment environment) =>
        environment.IsDevelopment() || string.Equals(configuration["Webhooks:FakeSenderEnabled"], "true", StringComparison.OrdinalIgnoreCase);

    private static RequestMetadata ToRequestMetadata(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = http.Request.Headers.UserAgent.ToString();
        return new RequestMetadata(ip, string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? email : $"{email[..1]}***{email[at..]}";
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
