using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Legal;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Legal;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Rendering;

namespace Pismolet.Web.Endpoints;

public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/email/fake", ReceiveFakeWebhook);
        app.MapPost("/webhooks/email/{provider}", ReceiveProviderWebhook);
        return app;
    }

    public static IEndpointRouteBuilder MapDevWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dev/webhooks/fake", ShowFakeSender);
        app.MapPost("/dev/webhooks/fake", SendFakeWebhook);
        return app;
    }

    private static Task<IResult> ReceiveProviderWebhook(string provider, HttpContext http, IEmailProviderAdapter adapter, IEmailWebhookProcessingService processor, ILegalEvidenceService legalEvidence, IConfiguration configuration, IHostEnvironment environment) =>
        Receive(http, adapter, processor, legalEvidence, configuration, environment);

    private static Task<IResult> ReceiveFakeWebhook(HttpContext http, IEmailProviderAdapter adapter, IEmailWebhookProcessingService processor, ILegalEvidenceService legalEvidence, IConfiguration configuration, IHostEnvironment environment) =>
        Receive(http, adapter, processor, legalEvidence, configuration, environment);

    private static async Task<IResult> Receive(HttpContext http, IEmailProviderAdapter adapter, IEmailWebhookProcessingService processor, ILegalEvidenceService legalEvidence, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!IsWebhookAllowed(http, configuration, environment))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        using var reader = new StreamReader(http.Request.Body);
        var raw = await reader.ReadToEndAsync();
        var headers = http.Request.Headers.ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var parsed = await adapter.ParseWebhookAsync(raw, headers, http.RequestAborted);
        if (!parsed.Ok || parsed.Event is null)
        {
            return Results.BadRequest(new { status = "invalid_payload" });
        }

        var requestMetadata = ToRequestMetadata(http);
        var result = processor.Process(parsed.Event, requestMetadata);
        RecordComplaintLegalEvidence(parsed.Event, requestMetadata, http.Request.Path.ToString(), result.Status, legalEvidence);
        return Results.Ok(new { status = result.Status, correlationId = result.CorrelationId });
    }

    private static IResult ShowFakeSender(ISendEventRepository sendEvents, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!IsDevSenderAllowed(configuration, environment))
        {
            return Results.NotFound();
        }

        var body = "<section class='card'><h1>Fake webhook sender</h1>" +
                   "<p class='muted'>Dev-инструмент для проверки delivery/bounce/complaint. Не является клиентской функцией.</p>" +
                   "<form method='post' action='/dev/webhooks/fake'>" +
                   "<label>ProviderMessageId<input name='providerMessageId' required></label>" +
                   "<label>Тип события<select name='eventType'><option value='accepted'>accepted</option><option value='delivered'>delivered</option><option value='soft_bounce'>soft_bounce</option><option value='hard_bounce'>hard_bounce</option><option value='complaint'>complaint</option><option value='rejected'>rejected</option><option value='unknown'>unknown</option></select></label>" +
                   "<label>ProviderEventId для повтора<input name='providerEventId' placeholder='оставьте пустым для стабильного id'></label>" +
                   "<button class='button'>Отправить fake webhook</button>" +
                   "</form></section>";
        return HtmlRenderer.Html(HtmlRenderer.Page("Fake webhook sender", body));
    }

    private static IResult SendFakeWebhook(HttpContext http, IEmailWebhookProcessingService processor, ISendEventRepository sendEvents, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!IsDevSenderAllowed(configuration, environment))
        {
            return Results.NotFound();
        }

        var providerMessageId = http.Request.Form["providerMessageId"].ToString().Trim();
        var eventTypeRaw = http.Request.Form["eventType"].ToString().Trim();
        var sendEvent = sendEvents.GetByProviderMessageId(providerMessageId);
        if (sendEvent is null)
        {
            return HtmlRenderer.Html(HtmlRenderer.Page("Fake webhook sender", HtmlRenderer.Error("ProviderMessageId не найден.")));
        }

        var eventType = MapEventType(eventTypeRaw);
        var providerEventId = http.Request.Form["providerEventId"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(providerEventId))
        {
            providerEventId = FakeEmailProviderAdapter.BuildProviderEventId(providerMessageId, eventType);
        }

        var providerEvent = new EmailProviderWebhookEvent(
            SendEvent.FakeProvider,
            providerEventId,
            providerMessageId,
            sendEvent.MailingId,
            sendEvent.RecipientEmail,
            eventType,
            DateTimeOffset.UtcNow,
            eventTypeRaw,
            $"Fake {eventTypeRaw}",
            JsonSerializer.Serialize(new
            {
                providerEventId,
                providerMessageId,
                mailingId = sendEvent.MailingId,
                recipientEmail = sendEvent.RecipientEmail,
                eventType = eventTypeRaw,
                occurredAt = DateTimeOffset.UtcNow
            }));

        var result = processor.Process(providerEvent, ToRequestMetadata(http));
        var body = $"<section class='card'><h1>Fake webhook sender</h1><p class='success'>Событие обработано: {H(result.Status)}</p><p>CorrelationId: {H(result.CorrelationId.ToString())}</p><p><a href='/dev/webhooks/fake'>Отправить ещё</a></p></section>";
        return HtmlRenderer.Html(HtmlRenderer.Page("Fake webhook sender", body));
    }

    private static void RecordComplaintLegalEvidence(EmailProviderWebhookEvent providerEvent, RequestMetadata requestMetadata, string route, string processingStatus, ILegalEvidenceService legalEvidence)
    {
        if (providerEvent.EventType != ProviderWebhookEventType.Complaint)
        {
            return;
        }

        var snapshot = LegalEvidenceTextSnapshots.RecipientComplaintReceivedText;
        var clientId = string.IsNullOrWhiteSpace(providerEvent.RecipientEmail)
            ? "unknown-recipient"
            : providerEvent.RecipientEmail.Trim().ToLowerInvariant();
        var metadataJson = JsonSerializer.Serialize(new
        {
            provider = providerEvent.Provider,
            providerEventId = providerEvent.ProviderEventId,
            providerMessageId = providerEvent.ProviderMessageId,
            mailingId = providerEvent.MailingId,
            recipientEmail = providerEvent.RecipientEmail,
            rawEventType = providerEvent.RawEventType,
            description = providerEvent.Description,
            occurredAt = providerEvent.OccurredAt,
            processingStatus,
            rawPayload = providerEvent.RawPayload
        });

        legalEvidence.RecordEvent(new LegalEvidenceEventDraft(
            LegalEventTypes.RecipientComplaintReceived,
            clientId,
            null,
            null,
            providerEvent.MailingId,
            LegalDocumentKeys.RecipientComplaint,
            LegalEvidenceTextSnapshots.CurrentVersion,
            legalEvidence.ComputeTextHash(snapshot),
            snapshot,
            LegalEventResults.Received,
            requestMetadata.Ip,
            requestMetadata.UserAgent,
            route,
            metadataJson));
    }

    private static bool IsWebhookAllowed(HttpContext http, IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredSecret = configuration["Webhooks:FakeProviderSecret"];
        var secret = string.IsNullOrWhiteSpace(configuredSecret) && environment.IsDevelopment()
            ? "dev-fake-webhook-secret"
            : configuredSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        var provided = http.Request.Headers["X-Pismolet-Webhook-Secret"].ToString();
        return string.Equals(provided, secret, StringComparison.Ordinal);
    }

    private static bool IsDevSenderAllowed(IConfiguration configuration, IHostEnvironment environment)
    {
        var enabled = string.Equals(configuration["Webhooks:FakeSenderEnabled"], "true", StringComparison.OrdinalIgnoreCase);
        return environment.IsDevelopment() || enabled;
    }

    private static ProviderWebhookEventType MapEventType(string value) => value.Trim().ToLowerInvariant() switch
    {
        "accepted" => ProviderWebhookEventType.Accepted,
        "delivered" => ProviderWebhookEventType.Delivered,
        "soft_bounce" => ProviderWebhookEventType.SoftBounce,
        "hard_bounce" => ProviderWebhookEventType.HardBounce,
        "complaint" => ProviderWebhookEventType.Complaint,
        "rejected" => ProviderWebhookEventType.Rejected,
        _ => ProviderWebhookEventType.Unknown
    };

    private static RequestMetadata ToRequestMetadata(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = http.Request.Headers.UserAgent.ToString();
        return new RequestMetadata(ip, string.IsNullOrWhiteSpace(userAgent) ? "unknown" : userAgent);
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
