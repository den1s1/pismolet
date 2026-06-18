using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Database;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class EfSendEventRepository(PismoletDbContext db) : ISendEventRepository
{
    public SendEvent? Get(Guid mailingId, string recipientEmail)
    {
        var normalized = Normalize(recipientEmail);
        return db.SendEvents
            .AsNoTracking()
            .FirstOrDefault(x => x.MailingId == mailingId && x.RecipientEmail == normalized) is { } entity
                ? ToDomain(entity)
                : null;
    }

    public IReadOnlyCollection<SendEvent> ListByMailingId(Guid mailingId) => db.SendEvents
        .AsNoTracking()
        .Where(x => x.MailingId == mailingId)
        .OrderBy(x => x.CreatedAt)
        .ThenBy(x => x.RecipientEmail)
        .Select(x => ToDomain(x))
        .ToArray();

    public IReadOnlyCollection<SendEvent> GetPendingBatch(Guid mailingId, int batchSize) => db.SendEvents
        .AsNoTracking()
        .Where(x => x.MailingId == mailingId && x.Status == SendEventStatus.Pending.ToString())
        .OrderBy(x => x.CreatedAt)
        .ThenBy(x => x.RecipientEmail)
        .Take(Math.Max(1, batchSize))
        .Select(x => ToDomain(x))
        .ToArray();

    public int CountAcceptedForOwnerOnUtcDate(string ownerEmail, DateOnly utcDate)
    {
        var normalized = Normalize(ownerEmail);
        var start = new DateTimeOffset(utcDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var end = start.AddDays(1);

        return db.SendEvents.Count(x =>
            x.Status == SendEventStatus.Accepted.ToString() &&
            x.OwnerEmail == normalized &&
            x.UpdatedAt >= start &&
            x.UpdatedAt < end);
    }

    public void Save(SendEvent sendEvent)
    {
        var recipientEmail = Normalize(sendEvent.RecipientEmail);
        var entity = db.SendEvents.FirstOrDefault(x => x.MailingId == sendEvent.MailingId && x.RecipientEmail == recipientEmail);
        if (entity is null)
        {
            db.SendEvents.Add(ToEntity(sendEvent));
        }
        else
        {
            entity.OwnerEmail = Normalize(sendEvent.OwnerEmail);
            entity.Status = sendEvent.Status.ToString();
            entity.Reason = sendEvent.Reason.ToString();
            entity.Provider = sendEvent.Provider;
            entity.ProviderMessageId = sendEvent.ProviderMessageId;
            entity.Attempt = sendEvent.Attempt;
            entity.ErrorCode = sendEvent.ErrorCode;
            entity.ErrorMessage = sendEvent.ErrorMessage;
            entity.CreatedAt = sendEvent.CreatedAt.ToUniversalTime();
            entity.UpdatedAt = sendEvent.UpdatedAt.ToUniversalTime();
        }

        db.SaveChanges();
    }

    public MailingSendSummary GetSummary(Guid mailingId, int totalAcceptedRecipients)
    {
        var events = ListByMailingId(mailingId);
        var suppressed = events.Count(x => x.Status == SendEventStatus.Skipped && x.Reason == SendSkipReason.GlobalSuppression);
        var pausedByLimit = events.Count(x => x.Status == SendEventStatus.Paused && x.Reason == SendSkipReason.DailyLimit);
        var skippedOther = events.Count(x => x.Status == SendEventStatus.Skipped && x.Reason != SendSkipReason.GlobalSuppression);
        return new MailingSendSummary(
            mailingId,
            events.Count(x => x.Status is SendEventStatus.Pending or SendEventStatus.Accepted or SendEventStatus.Failed),
            events.Count(x => x.Status == SendEventStatus.Accepted),
            events.Count(x => x.Status == SendEventStatus.Failed),
            suppressed,
            pausedByLimit,
            skippedOther,
            events.Count(x => x.Status == SendEventStatus.Pending),
            totalAcceptedRecipients);
    }

    private static SendEventEntity ToEntity(SendEvent sendEvent) => new()
    {
        Id = sendEvent.Id == Guid.Empty ? Guid.NewGuid() : sendEvent.Id,
        MailingId = sendEvent.MailingId,
        OwnerEmail = Normalize(sendEvent.OwnerEmail),
        RecipientEmail = Normalize(sendEvent.RecipientEmail),
        Status = sendEvent.Status.ToString(),
        Reason = sendEvent.Reason.ToString(),
        Provider = sendEvent.Provider,
        ProviderMessageId = sendEvent.ProviderMessageId,
        Attempt = sendEvent.Attempt,
        ErrorCode = sendEvent.ErrorCode,
        ErrorMessage = sendEvent.ErrorMessage,
        CreatedAt = sendEvent.CreatedAt.ToUniversalTime(),
        UpdatedAt = sendEvent.UpdatedAt.ToUniversalTime()
    };

    private static SendEvent ToDomain(SendEventEntity entity) => new(
        entity.Id,
        entity.MailingId,
        entity.OwnerEmail,
        entity.RecipientEmail,
        Enum.Parse<SendEventStatus>(entity.Status),
        Enum.Parse<SendSkipReason>(entity.Reason),
        entity.Provider,
        entity.ProviderMessageId,
        entity.Attempt,
        entity.ErrorCode,
        entity.ErrorMessage,
        entity.CreatedAt,
        entity.UpdatedAt);

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
