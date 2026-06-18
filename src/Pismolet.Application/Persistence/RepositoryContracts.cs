using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;

namespace Pismolet.Web.Application.Persistence;

public interface IUserRepository
{
    bool Exists(string email);

    bool TryAdd(UserAccount user);

    UserAccount? GetByEmail(string email);

    UserAccount? FindByConfirmationToken(string token);

    void Update(UserAccount user);
}

public interface IMailingRepository
{
    bool TryAdd(Mailing mailing);

    Mailing? Get(Guid id);

    Mailing? GetForOwner(Guid id, string ownerEmail);

    IReadOnlyCollection<Mailing> ListForOwner(string ownerEmail);

    void Update(Mailing mailing);
}

public interface IGlobalSuppressionRepository
{
    bool IsSuppressed(string normalizedEmail);

    void Add(string normalizedEmail);
}

public interface IPaymentRepository
{
    Payment? GetByMailingId(Guid mailingId);

    Payment? GetByProviderOperationId(string providerOperationId);

    void Save(Payment payment);
}

public interface IPriceSettingsRepository
{
    PriceSettings GetActive();

    void Save(PriceSettings settings);
}

public interface IRiskCheckRepository
{
    RiskCheckResult? GetByMailingId(Guid mailingId);

    void Save(RiskCheckResult result);
}

public interface IModerationReviewRepository
{
    ModerationReview? Get(Guid id);

    ModerationReview? GetOpenByMailingId(Guid mailingId);

    IReadOnlyCollection<ModerationReview> ListOpen();

    void Save(ModerationReview review);
}

public interface IModerationActionLogRepository
{
    void Add(ModerationActionLog log);

    IReadOnlyCollection<ModerationActionLog> ListForReview(Guid reviewId);
}

public interface ISendEventRepository
{
    SendEvent? Get(Guid mailingId, string recipientEmail);

    IReadOnlyCollection<SendEvent> ListByMailingId(Guid mailingId);

    IReadOnlyCollection<SendEvent> GetPendingBatch(Guid mailingId, int batchSize);

    int CountAcceptedForOwnerOnUtcDate(string ownerEmail, DateOnly utcDate);

    void Save(SendEvent sendEvent);

    MailingSendSummary GetSummary(Guid mailingId, int totalAcceptedRecipients);
}
