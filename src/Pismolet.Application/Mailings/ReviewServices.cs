using System.Text.RegularExpressions;
using Pismolet.Web.Application.Admin;
using Pismolet.Web.Application.Audit;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Audit;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;

namespace Pismolet.Web.Application.Mailings;

public sealed record MailingReviewState(Mailing Mailing, Payment? Payment, RiskCheckResult? RiskResult, ModerationReview? Review);

public sealed record MailingReviewResult(bool Ok, string Error, MailingReviewState? State)
{
    public static MailingReviewResult Success(MailingReviewState state) => new(true, string.Empty, state);

    public static MailingReviewResult Failure(string error) => new(false, error, null);
}

public sealed record ModerationQueueItem(ModerationReview Review, Mailing? Mailing, RiskCheckResult? RiskResult);

public sealed record AdminModerationResult(bool Ok, string Error, ModerationReview? Review, Mailing? Mailing, RiskCheckResult? RiskResult, IReadOnlyCollection<ModerationActionLog> Logs)
{
    public static AdminModerationResult Success(ModerationReview review, Mailing? mailing, RiskCheckResult? riskResult, IReadOnlyCollection<ModerationActionLog>? logs = null) => new(true, string.Empty, review, mailing, riskResult, logs ?? Array.Empty<ModerationActionLog>());

    public static AdminModerationResult Failure(string error) => new(false, error, null, null, null, Array.Empty<ModerationActionLog>());
}

public interface IRiskCheckService
{
    RiskCheckResult Check(Mailing mailing, UserAccount? owner);
}

public interface IMailingReviewService
{
    MailingReviewResult GetState(string userEmail, Guid mailingId);

    MailingReviewResult StartChecks(string userEmail, Guid mailingId, RequestMetadata request);
}

public interface IModerationAdminService
{
    IReadOnlyCollection<ModerationQueueItem> ListOpen();

    AdminModerationResult Get(Guid reviewId);

    AdminModerationResult Approve(Guid reviewId, string moderatorEmail, string? comment, RequestMetadata request);

    AdminModerationResult Reject(Guid reviewId, string moderatorEmail, string? comment, RequestMetadata request);
}

public sealed class RiskCheckService : IRiskCheckService
{
    private static readonly Regex LinkRegex = new(@"\bhttps?://[^\s<>'""\)]+|\bwww\.[^\s<>'""\)]+", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> SuspiciousDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "bit.ly",
        "tinyurl.com",
        "t.co",
        "goo.gl",
        "is.gd",
        "clck.ru"
    };

    private static readonly string[] RestrictedTopicPhrases =
    {
        "казино",
        "ставки на спорт",
        "быстрый займ",
        "микрозайм",
        "наркотик",
        "обналич",
        "купить базу email"
    };

    private static readonly string[] AggressiveAdPhrases =
    {
        "только сегодня",
        "срочно купите",
        "нажмите немедленно",
        "100% гарантия",
        "без риска",
        "последний шанс"
    };

    public RiskCheckResult Check(Mailing mailing, UserAccount? owner)
    {
        var hits = new List<RiskRuleHit>();
        var draft = mailing.MessageDraft;
        var text = string.Join("\n", mailing.Subject, draft?.Subject, draft?.SenderName, draft?.Body);

        if (draft is null)
        {
            hits.Add(RiskRuleHit.Review("message_missing", 100, "Текст письма ещё не сохранён."));
            return RiskCheckResult.Create(mailing.Id, hits);
        }

        if (string.IsNullOrWhiteSpace(draft.SenderName) || draft.SenderName.Trim().Length < 3)
        {
            hits.Add(RiskRuleHit.Review("sender_unclear", 30, "Не указан понятный отправитель."));
        }

        if (mailing.Declaration is null)
        {
            hits.Add(RiskRuleHit.Review("reason_missing", 30, "Не подтверждена причина получения письма."));
        }
        else if (mailing.Declaration.BaseSource == BaseSource.Other)
        {
            hits.Add(RiskRuleHit.Info("reason_generic", 10, "Источник базы указан как «Другое»."));
        }

        var links = LinkRegex.Matches(text).Select(match => match.Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (links.Length > 3)
        {
            hits.Add(RiskRuleHit.Review("too_many_links", 25, "В письме слишком много ссылок для автоматического одобрения."));
        }

        foreach (var link in links)
        {
            var normalizedLink = link.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? $"http://{link}" : link;
            if (Uri.TryCreate(normalizedLink, UriKind.Absolute, out var uri))
            {
                if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    hits.Add(RiskRuleHit.Review("non_https_link", 25, "В письме есть ссылка без защищённого HTTPS."));
                    break;
                }

                if (SuspiciousDomains.Contains(uri.Host.TrimStart('.')))
                {
                    hits.Add(RiskRuleHit.Review("short_link_domain", 35, "В письме есть сокращённая или подозрительная ссылка."));
                    break;
                }
            }
        }

        if (ContainsAny(text, RestrictedTopicPhrases))
        {
            hits.Add(RiskRuleHit.Review("restricted_topic", 50, "Тематика письма требует ручной проверки."));
        }

        if (ContainsAny(text, AggressiveAdPhrases))
        {
            hits.Add(RiskRuleHit.Review("aggressive_ad", 25, "Формулировки похожи на агрессивную рекламу."));
        }

        if (owner?.Profile.PremoderationRequired == true)
        {
            hits.Add(RiskRuleHit.Review("forced_premoderation", 50, "Новый клиент или клиент с обязательной премодерацией."));
        }

        return RiskCheckResult.Create(mailing.Id, hits);
    }

    private static bool ContainsAny(string text, IEnumerable<string> phrases) => phrases.Any(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase));
}

public sealed class MailingReviewService(
    IMailingRepository mailings,
    IPaymentRepository payments,
    IRiskCheckRepository riskChecks,
    IModerationReviewRepository reviews,
    IUserRepository users,
    IRiskCheckService riskCheckService,
    IEmailNormalizer emailNormalizer,
    IAuditLogger auditLogger,
    IAdminNotificationService? adminNotifications = null) : IMailingReviewService
{
    public MailingReviewResult GetState(string userEmail, Guid mailingId)
    {
        var mailing = GetOwnedMailing(userEmail, mailingId);
        if (mailing is null)
        {
            return MailingReviewResult.Failure("Рассылка не найдена.");
        }

        return BuildState(mailing);
    }

    public MailingReviewResult StartChecks(string userEmail, Guid mailingId, RequestMetadata request)
    {
        var mailing = GetOwnedMailing(userEmail, mailingId);
        if (mailing is null)
        {
            return MailingReviewResult.Failure("Рассылка не найдена.");
        }

        var payment = payments.GetByMailingId(mailing.Id);
        if (payment?.Status != PaymentStatus.Paid)
        {
            return MailingReviewResult.Failure("Сначала оплатите рассылку.");
        }

        var existing = riskChecks.GetByMailingId(mailing.Id);
        if (existing is not null)
        {
            return BuildState(mailing);
        }

        mailing = mailing.WithStatus(MailingStatus.PendingChecks);
        mailings.Update(mailing);
        auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "mailing_checks_started", request.Ip, request.UserAgent, $"mailingId={mailing.Id}"));

        var owner = users.GetByEmail(mailing.OwnerEmail);
        var riskResult = riskCheckService.Check(mailing, owner);
        riskChecks.Save(riskResult);

        ModerationReview? review = null;
        switch (riskResult.Decision)
        {
            case RiskDecision.Approved:
                mailing = mailing.WithStatus(MailingStatus.Approved);
                auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "risk_check_approved", request.Ip, request.UserAgent, $"mailingId={mailing.Id};score={riskResult.Score}"));
                break;
            case RiskDecision.Rejected:
                mailing = mailing.WithStatus(MailingStatus.Rejected);
                auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "risk_check_rejected", request.Ip, request.UserAgent, $"mailingId={mailing.Id};score={riskResult.Score}"));
                break;
            default:
                review = reviews.GetOpenByMailingId(mailing.Id) ?? ModerationReview.Create(mailing.Id, BuildReviewReason(riskResult));
                reviews.Save(review);
                mailing = mailing.WithStatus(MailingStatus.ReviewRequired);
                auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, mailing.OwnerEmail, "moderation_review_created", request.Ip, request.UserAgent, $"mailingId={mailing.Id};reviewId={review.Id};score={riskResult.Score}"));
                adminNotifications?.NotifyMailingSubmittedToModeration(mailing, review);
                break;
        }

        mailings.Update(mailing);
        return MailingReviewResult.Success(new MailingReviewState(mailing, payment, riskResult, review));
    }

    private MailingReviewResult BuildState(Mailing mailing)
    {
        var risk = riskChecks.GetByMailingId(mailing.Id);
        var review = reviews.GetOpenByMailingId(mailing.Id);
        if (review is null && risk?.Decision == RiskDecision.ReviewRequired)
        {
            review = reviews.ListOpen().FirstOrDefault(item => item.MailingId == mailing.Id);
        }

        return MailingReviewResult.Success(new MailingReviewState(mailing, payments.GetByMailingId(mailing.Id), risk, review));
    }

    private Mailing? GetOwnedMailing(string userEmail, Guid mailingId)
    {
        var normalized = emailNormalizer.Normalize(userEmail);
        return string.IsNullOrWhiteSpace(normalized) ? null : mailings.GetForOwner(mailingId, normalized);
    }

    private static string BuildReviewReason(RiskCheckResult riskResult)
    {
        var reasons = riskResult.TriggeredRules.Select(rule => rule.PublicReason).Where(reason => !string.IsNullOrWhiteSpace(reason)).Distinct().ToArray();
        return reasons.Length == 0 ? riskResult.PublicExplanation : string.Join("; ", reasons);
    }
}

public sealed class ModerationAdminService(
    IMailingRepository mailings,
    IRiskCheckRepository riskChecks,
    IModerationReviewRepository reviews,
    IModerationActionLogRepository actionLogs,
    IEmailNormalizer emailNormalizer,
    IAuditLogger auditLogger) : IModerationAdminService
{
    public IReadOnlyCollection<ModerationQueueItem> ListOpen() => reviews.ListOpen()
        .Select(review => new ModerationQueueItem(review, mailings.Get(review.MailingId), riskChecks.GetByMailingId(review.MailingId)))
        .OrderBy(item => item.Review.CreatedAt)
        .ToArray();

    public AdminModerationResult Get(Guid reviewId)
    {
        var review = reviews.Get(reviewId);
        if (review is null)
        {
            return AdminModerationResult.Failure("Проверка не найдена.");
        }

        return AdminModerationResult.Success(review, mailings.Get(review.MailingId), riskChecks.GetByMailingId(review.MailingId), actionLogs.ListForReview(review.Id));
    }

    public AdminModerationResult Approve(Guid reviewId, string moderatorEmail, string? comment, RequestMetadata request) => Resolve(reviewId, moderatorEmail, comment, request, approve: true);

    public AdminModerationResult Reject(Guid reviewId, string moderatorEmail, string? comment, RequestMetadata request) => Resolve(reviewId, moderatorEmail, comment, request, approve: false);

    private AdminModerationResult Resolve(Guid reviewId, string moderatorEmail, string? comment, RequestMetadata request, bool approve)
    {
        var normalizedModerator = emailNormalizer.Normalize(moderatorEmail);
        if (string.IsNullOrWhiteSpace(normalizedModerator))
        {
            return AdminModerationResult.Failure("Администратор не определён.");
        }

        var review = reviews.Get(reviewId);
        if (review is null)
        {
            return AdminModerationResult.Failure("Проверка не найдена.");
        }

        var mailing = mailings.Get(review.MailingId);
        if (mailing is null)
        {
            return AdminModerationResult.Failure("Рассылка не найдена.");
        }

        var previous = review.Status.ToString();
        if (review.Status == ModerationReviewStatus.Open)
        {
            review = approve ? review.Approve(normalizedModerator, comment) : review.Reject(normalizedModerator, comment);
            reviews.Save(review);

            mailing = mailing.WithStatus(approve ? MailingStatus.Approved : MailingStatus.Rejected);
            mailings.Update(mailing);

            var action = approve ? "moderation_approved" : "moderation_rejected";
            actionLogs.Add(ModerationActionLog.Create(review.Id, mailing.Id, normalizedModerator, action, comment, previous, review.Status.ToString()));
            auditLogger.Write(new AuditRecord(DateTimeOffset.UtcNow, normalizedModerator, action, request.Ip, request.UserAgent, $"reviewId={review.Id};mailingId={mailing.Id};previous={previous};new={review.Status}"));
        }
        else
        {
            var action = approve ? "moderation_approve_ignored" : "moderation_reject_ignored";
            actionLogs.Add(ModerationActionLog.Create(review.Id, mailing.Id, normalizedModerator, action, comment, previous, review.Status.ToString()));
        }

        return AdminModerationResult.Success(review, mailing, riskChecks.GetByMailingId(mailing.Id), actionLogs.ListForReview(review.Id));
    }
}
