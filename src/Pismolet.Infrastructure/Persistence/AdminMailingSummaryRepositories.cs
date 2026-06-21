using Microsoft.EntityFrameworkCore;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Database;

namespace Pismolet.Web.Infrastructure.Persistence;

public sealed class InMemoryAdminMailingSummaryRepository(
    IUserRepository users,
    IMailingRepository mailings) : IAdminCampaignRepository, IAdminPaymentRepository
{
    public IReadOnlyCollection<AdminMailingSummary> ListSummaries() => users.ListAll()
        .SelectMany(user => mailings.ListForOwner(user.Email)
            .Select(mailing => ToSummary(mailing, user.DisplayName)))
        .OrderByDescending(row => row.CreatedAt)
        .ToArray();

    private static AdminMailingSummary ToSummary(Mailing mailing, string clientName) => new(
        mailing.Id,
        mailing.OwnerEmail,
        string.IsNullOrWhiteSpace(clientName) ? mailing.OwnerEmail : clientName,
        mailing.Subject,
        mailing.MessageDraft?.Subject ?? mailing.Subject,
        mailing.Status,
        mailing.StatusRu,
        mailing.LastImportStats.TotalRows,
        mailing.LastImportStats.Accepted,
        mailing.CreatedAt,
        mailing.MessageDraft is not null);
}

public sealed class EfAdminMailingSummaryRepository(PismoletDbContext db) : IAdminCampaignRepository, IAdminPaymentRepository
{
    public IReadOnlyCollection<AdminMailingSummary> ListSummaries()
    {
        var users = db.Users
            .AsNoTracking()
            .Select(user => new { user.NormalizedEmail, user.DisplayName })
            .ToDictionary(user => user.NormalizedEmail, user => user.DisplayName, StringComparer.OrdinalIgnoreCase);

        var latestBatchTimes = db.ImportBatches
            .AsNoTracking()
            .GroupBy(batch => batch.MailingId)
            .Select(group => new { MailingId = group.Key, CreatedAt = group.Max(batch => batch.CreatedAt) })
            .ToArray();

        var latestBatchIds = latestBatchTimes.Select(batch => batch.MailingId).Distinct().ToArray();
        var latestBatchLookup = db.ImportBatches
            .AsNoTracking()
            .Where(batch => latestBatchIds.Contains(batch.MailingId))
            .ToArray()
            .GroupBy(batch => batch.MailingId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(batch => batch.CreatedAt).First());

        var draftLookup = db.MailingMessageDrafts
            .AsNoTracking()
            .Select(draft => new { draft.MailingId, draft.Subject })
            .ToDictionary(draft => draft.MailingId, draft => draft.Subject);

        return db.Mailings
            .AsNoTracking()
            .OrderByDescending(mailing => mailing.CreatedAt)
            .ToArray()
            .Select(mailing =>
            {
                latestBatchLookup.TryGetValue(mailing.Id, out var batch);
                draftLookup.TryGetValue(mailing.Id, out var draftSubject);
                return new AdminMailingSummary(
                    mailing.Id,
                    mailing.OwnerEmail,
                    users.GetValueOrDefault(mailing.OwnerEmail) ?? mailing.OwnerEmail,
                    mailing.Subject,
                    string.IsNullOrWhiteSpace(draftSubject) ? mailing.Subject : draftSubject,
                    MailingStatusLabels.FromRu(mailing.StatusRu),
                    mailing.StatusRu,
                    batch?.TotalRows ?? 0,
                    batch?.Accepted ?? 0,
                    mailing.CreatedAt,
                    draftSubject is not null);
            })
            .ToArray();
    }
}
