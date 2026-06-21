using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Database;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class InMemoryAdminRecipientRepository(
    IUserRepository users,
    IMailingRepository mailings,
    IGlobalSuppressionRepository suppressions) : IAdminRecipientRepository
{
    public IReadOnlyCollection<AdminRecipientSummary> ListSummaries()
    {
        var mailingList = users.ListAll()
            .SelectMany(user => mailings.ListForOwner(user.Email))
            .ToArray();

        return mailingList
            .SelectMany(mailing => mailing.Recipients.Select(recipient => new RecipientSnapshot(
                recipient.Email,
                mailing.Id,
                mailing.OwnerEmail,
                mailing.Subject,
                mailing.Status,
                mailing.Recipients.Count,
                mailing.CreatedAt,
                null)))
            .GroupBy(item => item.Email, StringComparer.OrdinalIgnoreCase)
            .Select(group => AdminRecipientMapper.ToSummary(group.Key, group, suppressions.GetByEmail(group.Key), Array.Empty<SendSnapshot>()))
            .OrderBy(row => row.Email, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public AdminRecipientProfile? GetProfile(string email)
    {
        var normalized = AdminRecipientMapper.Normalize(email);
        var mailingList = users.ListAll()
            .SelectMany(user => mailings.ListForOwner(user.Email))
            .ToArray();
        var snapshots = mailingList
            .SelectMany(mailing => mailing.Recipients
                .Where(recipient => string.Equals(recipient.Email, normalized, StringComparison.OrdinalIgnoreCase))
                .Select(_ => new RecipientSnapshot(
                    normalized,
                    mailing.Id,
                    mailing.OwnerEmail,
                    mailing.Subject,
                    mailing.Status,
                    mailing.Recipients.Count,
                    mailing.CreatedAt,
                    null)))
            .ToArray();

        if (snapshots.Length == 0)
        {
            return null;
        }

        return AdminRecipientMapper.ToProfile(normalized, snapshots, suppressions.GetByEmail(normalized), Array.Empty<SendSnapshot>());
    }
}

public sealed class EfAdminRecipientRepository(PismoletDbContext db) : IAdminRecipientRepository
{
    public IReadOnlyCollection<AdminRecipientSummary> ListSummaries()
    {
        var recipients = LoadRecipients(null);
        var sendEvents = LoadSendEvents(recipients.Select(x => x.Email).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        var suppressions = LoadSuppressions(null);
        var emails = recipients.Select(x => x.Email)
            .Concat(suppressions.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return emails
            .Select(email => AdminRecipientMapper.ToSummary(
                email,
                recipients.Where(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase)),
                suppressions.GetValueOrDefault(email),
                sendEvents.Where(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(row => row.Email, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public AdminRecipientProfile? GetProfile(string email)
    {
        var normalized = AdminRecipientMapper.Normalize(email);
        var recipients = LoadRecipients(normalized);
        var suppression = LoadSuppressions(normalized).GetValueOrDefault(normalized);
        if (recipients.Length == 0 && suppression is null)
        {
            return null;
        }

        return AdminRecipientMapper.ToProfile(normalized, recipients, suppression, LoadSendEvents([normalized]));
    }

    private RecipientSnapshot[] LoadRecipients(string? normalizedEmail)
    {
        var recipientRows = (from recipient in db.Recipients.AsNoTracking()
                             join mailing in db.Mailings.AsNoTracking() on recipient.MailingId equals mailing.Id
                             where recipient.Status == nameof(RecipientStatus.Accepted)
                             select new
                             {
                                 recipient.NormalizedEmail,
                                 mailing.Id,
                                 mailing.OwnerEmail,
                                 mailing.Subject,
                                 mailing.StatusRu,
                                 mailing.CreatedAt
                             }).ToArray();

        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            recipientRows = recipientRows
                .Where(x => string.Equals(x.NormalizedEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        var mailingIds = recipientRows.Select(x => x.Id).Distinct().ToArray();
        var acceptedCounts = db.Recipients
            .AsNoTracking()
            .Where(x => mailingIds.Contains(x.MailingId) && x.Status == nameof(RecipientStatus.Accepted))
            .GroupBy(x => x.MailingId)
            .Select(group => new { MailingId = group.Key, Count = group.Count() })
            .ToDictionary(x => x.MailingId, x => x.Count);

        return recipientRows
            .Select(x => new RecipientSnapshot(
                x.NormalizedEmail,
                x.Id,
                x.OwnerEmail,
                x.Subject,
                MailingStatusLabels.FromRu(x.StatusRu),
                acceptedCounts.GetValueOrDefault(x.Id),
                x.CreatedAt,
                null))
            .ToArray();
    }

    private IReadOnlyDictionary<string, GlobalSuppression> LoadSuppressions(string? normalizedEmail)
    {
        var query = db.GlobalSuppressions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            query = query.Where(x => x.EmailNormalized == normalizedEmail);
        }

        return query
            .ToArray()
            .Select(entity => new GlobalSuppression(
                entity.Id,
                entity.EmailNormalized,
                entity.EmailHash,
                Enum.Parse<GlobalSuppressionSource>(entity.Source),
                entity.SourceMailingId,
                entity.SourceRecipientKey,
                entity.CreatedAt,
                entity.CreatedIpHash,
                entity.UserAgentHash))
            .ToDictionary(x => x.EmailNormalized, StringComparer.OrdinalIgnoreCase);
    }

    private SendSnapshot[] LoadSendEvents(IReadOnlyCollection<string> emails)
    {
        if (emails.Count == 0)
        {
            return [];
        }

        return db.SendEvents
            .AsNoTracking()
            .Where(x => emails.Contains(x.RecipientEmail))
            .ToArray()
            .Select(x => new SendSnapshot(
                x.RecipientEmail,
                x.MailingId,
                Enum.Parse<SendEventStatus>(x.Status),
                Enum.Parse<DeliveryStatus>(x.DeliveryStatus),
                x.UpdatedAt))
            .ToArray();
    }
}

file sealed record RecipientSnapshot(
    string Email,
    Guid MailingId,
    string OwnerEmail,
    string Subject,
    MailingStatus Status,
    int AcceptedRecipients,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastMessageAt);

file sealed record SendSnapshot(
    string Email,
    Guid MailingId,
    SendEventStatus Status,
    DeliveryStatus DeliveryStatus,
    DateTimeOffset UpdatedAt);

file static class AdminRecipientMapper
{
    public static AdminRecipientSummary ToSummary(
        string email,
        IEnumerable<RecipientSnapshot> recipientRows,
        GlobalSuppression? suppression,
        IEnumerable<SendSnapshot> sendRows)
    {
        var recipients = recipientRows.ToArray();
        var sends = sendRows.ToArray();
        var owners = recipients.Select(x => x.OwnerEmail).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var mailings = recipients.Select(x => x.MailingId).Distinct().Count();
        var sent = sends.Count(x => x.Status == SendEventStatus.Accepted);
        var firstSeen = recipients.Length == 0 ? suppression?.CreatedAt : recipients.Min(x => x.CreatedAt);
        var lastMessage = sends.Length == 0 ? null : sends.Max(x => (DateTimeOffset?)x.UpdatedAt);
        var (code, text) = ResolveStatus(suppression, sends);

        return new AdminRecipientSummary(
            Normalize(email),
            code,
            text,
            mailings,
            owners,
            sent,
            firstSeen,
            lastMessage,
            suppression?.CreatedAt,
            suppression?.Source);
    }

    public static AdminRecipientProfile ToProfile(
        string email,
        IEnumerable<RecipientSnapshot> recipientRows,
        GlobalSuppression? suppression,
        IEnumerable<SendSnapshot> sendRows)
    {
        var recipients = recipientRows.ToArray();
        var sends = sendRows.ToArray();
        var summary = ToSummary(email, recipients, suppression, sends);
        var owners = recipients
            .GroupBy(x => x.OwnerEmail, StringComparer.OrdinalIgnoreCase)
            .Select(group => new AdminRecipientOwnerSummary(group.Key, group.Select(x => x.MailingId).Distinct().Count()))
            .OrderBy(x => x.OwnerEmail, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var mailingRows = recipients
            .GroupBy(x => x.MailingId)
            .Select(group =>
            {
                var first = group.First();
                var last = sends
                    .Where(x => x.MailingId == first.MailingId)
                    .Select(x => (DateTimeOffset?)x.UpdatedAt)
                    .DefaultIfEmpty(null)
                    .Max();
                return new AdminRecipientMailingSummary(first.MailingId, first.OwnerEmail, first.Subject, first.Status, first.AcceptedRecipients, first.CreatedAt, last);
            })
            .OrderByDescending(x => x.CreatedAt)
            .ToArray();

        return new AdminRecipientProfile(summary, owners, mailingRows);
    }

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private static (string Code, string Text) ResolveStatus(GlobalSuppression? suppression, IReadOnlyCollection<SendSnapshot> sends)
    {
        if (suppression?.Source == GlobalSuppressionSource.UnsubscribeLink)
        {
            return ("unsubscribed", "Отписался");
        }

        if (suppression?.Source == GlobalSuppressionSource.Admin)
        {
            return ("blocked", "Заблокирован вручную");
        }

        if (suppression?.Source == GlobalSuppressionSource.Complaint)
        {
            return ("unavailable", "Недоступен");
        }

        if (sends.Any(x => x.DeliveryStatus == DeliveryStatus.HardBounce))
        {
            return ("hard_bounce", "Hard bounce");
        }

        if (sends.Any(x => x.DeliveryStatus is DeliveryStatus.Rejected or DeliveryStatus.Complaint))
        {
            return ("unavailable", "Недоступен");
        }

        return ("active", "Активен");
    }
}
