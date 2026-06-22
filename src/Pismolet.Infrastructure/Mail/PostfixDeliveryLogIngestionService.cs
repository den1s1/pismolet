using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Infrastructure.Mail;

public sealed record PostfixDeliveryLogIngestionResult(
    int Parsed,
    int Stored,
    int Ignored,
    int MatchedSendEvents,
    int UpdatedSendEvents)
{
    public int Total => Parsed + Ignored;
}

public sealed class PostfixDeliveryLogIngestionService(
    IPostfixDeliveryEventRepository repository,
    ISendEventRepository sendEvents)
{
    public PostfixDeliveryLogIngestionResult IngestText(string text, int year, TimeSpan utcOffset)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new PostfixDeliveryLogIngestionResult(0, 0, 0, 0, 0);
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

            if (ApplyToSendEvent(saved))
            {
                matchedSendEvents++;
                updatedSendEvents++;
            }
        }

        return new PostfixDeliveryLogIngestionResult(parsed, stored, ignored, matchedSendEvents, updatedSendEvents);
    }

    private bool ApplyToSendEvent(PostfixDeliveryEvent deliveryEvent)
    {
        if (deliveryEvent.DeliveryStatus is DeliveryStatus.NotReported or DeliveryStatus.Unknown)
        {
            return false;
        }

        var sendEvent = sendEvents.GetByProviderMessageId(deliveryEvent.QueueId);
        if (sendEvent is null)
        {
            return false;
        }

        var updated = sendEvent.ApplyDeliveryStatus(
            deliveryEvent.DeliveryStatus,
            deliveryEvent.OccurredAt,
            BuildSummary(deliveryEvent));

        if (updated == sendEvent)
        {
            return false;
        }

        sendEvents.Save(updated);
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
}