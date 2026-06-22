using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Infrastructure.Mail;

public sealed record PostfixDeliveryLogIngestionResult(
    int Parsed,
    int Stored,
    int Ignored,
    int MatchedSendEvents,
    int UpdatedSendEvents,
    int ClientSuppressions)
{
    public int Total => Parsed + Ignored;
}

public sealed class PostfixDeliveryLogIngestionService(
    IPostfixDeliveryEventRepository repository,
    ISendEventRepository sendEvents,
    IClientSuppressionRepository clientSuppressions)
{
    public PostfixDeliveryLogIngestionResult IngestText(string text, int year, TimeSpan utcOffset)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new PostfixDeliveryLogIngestionResult(0, 0, 0, 0, 0, 0);
        }

        return IngestLines(
            text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            year,
            utcOffset);
    }

    public PostfixDeliveryLogIngestionResult IngestLines(IEnumerable<string> lines, int year, TimeSpan utcOffset)
    {
        var parsed = 0;
        var stored = 0;
        var ignored = 0;
        var matchedSendEvents = 0;
        var updatedSendEvents = 0;
        var clientSuppressionCount = 0;

        foreach (var line in lines)
        {
            if (!PostfixDeliveryLogParser.TryParse(line, year, utcOffset, out var parsedEvent) || parsedEvent is null)
            {
                ignored++;
                continue;
            }

            parsed++;
            var deliveryEvent = PostfixDeliveryEvent.FromParsed(
                parsedEvent.QueueId,
                parsedEvent.RecipientEmail,
                MapStatus(parsedEvent.Status),
                parsedEvent.DeliveryStatus,
                parsedEvent.Dsn,
                parsedEvent.Relay,
                parsedEvent.Diagnostic,
                parsedEvent.OccurredAt);

            var saved = repository.AddIfNotExists(deliveryEvent);
            if (saved.Id == deliveryEvent.Id)
            {
                stored++;
            }

            var application = ApplyToSendEvent(saved);
            if (application.Matched)
            {
                matchedSendEvents++;
            }

            if (application.Updated)
            {
                updatedSendEvents++;
            }

            if (application.Suppressed)
            {
                clientSuppressionCount++;
            }
        }

        return new PostfixDeliveryLogIngestionResult(parsed, stored, ignored, matchedSendEvents, updatedSendEvents, clientSuppressionCount);
    }

    private DeliveryApplicationResult ApplyToSendEvent(PostfixDeliveryEvent deliveryEvent)
    {
        if (deliveryEvent.DeliveryStatus is DeliveryStatus.NotReported or DeliveryStatus.Unknown)
        {
            return DeliveryApplicationResult.Empty;
        }

        var sendEvent = sendEvents.GetByProviderMessageId(deliveryEvent.QueueId);
        if (sendEvent is null)
        {
            return DeliveryApplicationResult.Empty;
        }

        var updated = sendEvent.ApplyDeliveryStatus(
            deliveryEvent.DeliveryStatus,
            deliveryEvent.OccurredAt,
            BuildSummary(deliveryEvent));

        var statusUpdated = updated != sendEvent;
        if (statusUpdated)
        {
            sendEvents.Save(updated);
        }

        var suppressed = SuppressHardBounceRecipient(updated);
        return new DeliveryApplicationResult(true, statusUpdated, suppressed);
    }

    private bool SuppressHardBounceRecipient(SendEvent sendEvent)
    {
        if (sendEvent.DeliveryStatus != DeliveryStatus.HardBounce)
        {
            return false;
        }

        var suppression = ClientSuppression.FromHardBounce(
            sendEvent.OwnerEmail,
            sendEvent.RecipientEmail,
            sendEvent.MailingId,
            sendEvent.ProviderMessageId);

        clientSuppressions.AddOrUpdate(suppression);
        return true;
    }

    private static string BuildSummary(PostfixDeliveryEvent deliveryEvent)
    {
        var status = deliveryEvent.Status.ToString().ToLowerInvariant();
        var dsn = string.IsNullOrWhiteSpace(deliveryEvent.Dsn) ? null : $"dsn={deliveryEvent.Dsn}";
        var diagnostic = string.IsNullOrWhiteSpace(deliveryEvent.Diagnostic) ? null : deliveryEvent.Diagnostic;

        return string.Join("; ", new[] { status, dsn, diagnostic }.Where(x => !string.IsNullOrWhiteSpace(x))!);
    }

    private static PostfixDeliveryEventStatus MapStatus(PostfixDeliveryLogStatus status) => status switch
    {
        PostfixDeliveryLogStatus.Sent => PostfixDeliveryEventStatus.Sent,
        PostfixDeliveryLogStatus.Deferred => PostfixDeliveryEventStatus.Deferred,
        PostfixDeliveryLogStatus.Bounced => PostfixDeliveryEventStatus.Bounced,
        PostfixDeliveryLogStatus.Expired => PostfixDeliveryEventStatus.Expired,
        _ => PostfixDeliveryEventStatus.Unknown
    };

    private readonly record struct DeliveryApplicationResult(bool Matched, bool Updated, bool Suppressed)
    {
        public static DeliveryApplicationResult Empty { get; } = new(false, false, false);
    }
}