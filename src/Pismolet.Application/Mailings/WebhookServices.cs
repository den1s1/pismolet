using System.Security.Cryptography;
using System.Text;
using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Mailings;

public sealed record EmailWebhookProcessingResult(bool Ok, bool Duplicate, string Status, Guid CorrelationId, ProviderWebhookProcessingStatus ProcessingStatus)
{
    public static EmailWebhookProcessingResult Processed(Guid correlationId, ProviderWebhookProcessingStatus status) => new(true, false, "processed", correlationId, status);

    public static EmailWebhookProcessingResult Duplicate(Guid correlationId) => new(true, true, "duplicate_ignored", correlationId, ProviderWebhookProcessingStatus.IgnoredDuplicate);

    public static EmailWebhookProcessingResult Failed(Guid correlationId, string status) => new(false, false, status, correlationId, ProviderWebhookProcessingStatus.Failed);
}

public interface IEmailWebhookProcessingService
{
    EmailWebhookProcessingResult Process(EmailProviderWebhookEvent providerEvent, RequestMetadata request);
}

public sealed class EmailWebhookProcessingService(
    ISendEventRepository sendEvents,
    IProviderWebhookEventRepository webhookEvents,
    IClientSuppressionRepository clientSuppressions,
    IGlobalSuppressionRepository globalSuppressions,
    IEmailNormalizer normalizer,
    IAuditLogger auditLogger) : IEmailWebhookProcessingService
{
    public EmailWebhookProcessingResult Process(EmailProviderWebhookEvent providerEvent, RequestMetadata request)
    {
        var correlationId = Guid.NewGuid();
        var provider = providerEvent.Provider.Trim();
        var providerEventId = providerEvent.ProviderEventId.Trim();
        var existing = webhookEvents.GetByProviderEventId(provider, providerEventId);
        if (existing is not null)
        {
            auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, "system", "webhook_duplicate_ignored", request.Ip, request.UserAgent, $"correlationId={correlationId};provider={provider};providerEventId={providerEventId};eventType={providerEvent.EventType}"));
            return EmailWebhookProcessingResult.Duplicate(correlationId);
        }

        var receivedAt = DateTimeOffset.UtcNow;
        var sendEvent = FindSendEvent(providerEvent);
        var processingStatus = ProviderWebhookProcessingStatus.Processed;
        if (sendEvent is null)
        {
            processingStatus = providerEvent.EventType == ProviderWebhookEventType.Unknown
                ? ProviderWebhookProcessingStatus.IgnoredUnknown
                : ProviderWebhookProcessingStatus.Unmatched;
        }
        else if (providerEvent.EventType == ProviderWebhookEventType.Unknown)
        {
            processingStatus = ProviderWebhookProcessingStatus.IgnoredUnknown;
        }

        var eventRecord = new ProviderWebhookEvent(
            Guid.NewGuid(),
            provider,
            providerEventId,
            providerEvent.ProviderMessageId,
            sendEvent?.MailingId ?? providerEvent.MailingId,
            sendEvent?.OwnerEmail,
            sendEvent?.RecipientEmail ?? Normalize(providerEvent.RecipientEmail),
            providerEvent.EventType,
            providerEvent.OccurredAt,
            receivedAt,
            Hash(providerEvent.RawPayload),
            StoreRawPayload(providerEvent.RawPayload),
            providerEvent.ReasonCode,
            providerEvent.ReasonMessage,
            processingStatus,
            correlationId);

        webhookEvents.Save(eventRecord);

        if (sendEvent is null)
        {
            auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, "system", "webhook_unmatched", request.Ip, request.UserAgent, $"correlationId={correlationId};provider={provider};providerEventId={providerEventId};eventType={providerEvent.EventType};payloadHash={eventRecord.RawPayloadHash}"));
            return EmailWebhookProcessingResult.Processed(correlationId, processingStatus);
        }

        if (providerEvent.EventType == ProviderWebhookEventType.Unknown)
        {
            auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, sendEvent.OwnerEmail, "webhook_unknown_ignored", request.Ip, request.UserAgent, $"correlationId={correlationId};mailingId={sendEvent.MailingId};providerEventId={providerEventId};payloadHash={eventRecord.RawPayloadHash}"));
            return EmailWebhookProcessingResult.Processed(correlationId, processingStatus);
        }

        var deliveryStatus = DeliveryStatusLabels.FromEventType(providerEvent.EventType);
        var summary = string.IsNullOrWhiteSpace(providerEvent.ReasonMessage)
            ? deliveryStatus.ToRu()
            : providerEvent.ReasonMessage;
        var updatedSendEvent = sendEvent.ApplyDeliveryStatus(deliveryStatus, providerEvent.OccurredAt, summary);
        sendEvents.Save(updatedSendEvent);

        if (providerEvent.EventType == ProviderWebhookEventType.HardBounce)
        {
            clientSuppressions.AddOrUpdate(ClientSuppression.FromHardBounce(
                sendEvent.OwnerEmail,
                sendEvent.RecipientEmail,
                sendEvent.MailingId,
                sendEvent.ProviderMessageId));
            auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, sendEvent.OwnerEmail, "client_suppression_added_from_hard_bounce", request.Ip, request.UserAgent, $"correlationId={correlationId};mailingId={sendEvent.MailingId};providerMessageId={sendEvent.ProviderMessageId};emailHash={Hash(sendEvent.RecipientEmail)}"));
        }
        else if (providerEvent.EventType == ProviderWebhookEventType.Complaint)
        {
            globalSuppressions.AddOrGet(new GlobalSuppression(
                Guid.NewGuid(),
                sendEvent.RecipientEmail,
                Hash(sendEvent.RecipientEmail),
                GlobalSuppressionSource.Complaint,
                sendEvent.MailingId,
                sendEvent.ProviderMessageId,
                DateTimeOffset.UtcNow,
                Hash(request.Ip),
                Hash(request.UserAgent)));
            auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, sendEvent.OwnerEmail, "global_suppression_added_from_complaint", request.Ip, request.UserAgent, $"correlationId={correlationId};mailingId={sendEvent.MailingId};providerMessageId={sendEvent.ProviderMessageId};emailHash={Hash(sendEvent.RecipientEmail)}"));
        }

        auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, sendEvent.OwnerEmail, "webhook_delivery_updated", request.Ip, request.UserAgent, $"correlationId={correlationId};mailingId={sendEvent.MailingId};provider={provider};providerEventId={providerEventId};eventType={providerEvent.EventType};deliveryStatus={updatedSendEvent.DeliveryStatus}"));
        return EmailWebhookProcessingResult.Processed(correlationId, processingStatus);
    }

    private SendEvent? FindSendEvent(EmailProviderWebhookEvent providerEvent)
    {
        if (!string.IsNullOrWhiteSpace(providerEvent.ProviderMessageId))
        {
            var byProviderMessage = sendEvents.GetByProviderMessageId(providerEvent.ProviderMessageId);
            if (byProviderMessage is not null)
            {
                return byProviderMessage;
            }
        }

        var recipient = Normalize(providerEvent.RecipientEmail);
        return providerEvent.MailingId is { } mailingId && !string.IsNullOrWhiteSpace(recipient)
            ? sendEvents.Get(mailingId, recipient)
            : null;
    }

    private string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : normalizer.Normalize(value);

    private static string StoreRawPayload(string rawPayload) => rawPayload.Length <= 4096 ? rawPayload : rawPayload[..4096];

    private static string Hash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}