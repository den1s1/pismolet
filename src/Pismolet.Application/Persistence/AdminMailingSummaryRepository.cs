using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Application.Persistence;

public interface IAdminCampaignRepository
{
    IReadOnlyCollection<AdminMailingSummary> ListSummaries();
}

public interface IAdminPaymentRepository
{
    IReadOnlyCollection<AdminMailingSummary> ListSummaries();
}

public sealed record AdminMailingSummary(
    Guid Id,
    string OwnerEmail,
    string ClientName,
    string Subject,
    string DisplaySubject,
    MailingStatus Status,
    string StatusRu,
    int TotalRows,
    int AcceptedRecipients,
    DateTimeOffset CreatedAt,
    bool HasMessageDraft);
