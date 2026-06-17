namespace Pismolet.Web.Domain.Mailings;

public enum RiskDecision
{
    Approved,
    ReviewRequired,
    Rejected,
    Blocked
}

public static class RiskDecisionLabels
{
    public static string ToRu(this RiskDecision decision) => decision switch
    {
        RiskDecision.Approved => "Одобрено",
        RiskDecision.ReviewRequired => "На ручной проверке",
        RiskDecision.Rejected => "Отклонено",
        RiskDecision.Blocked => "Заблокировано",
        _ => "Проверка перед отправкой"
    };
}

public enum RiskRuleDecision
{
    Informational,
    ReviewRequired,
    Reject
}

public sealed record RiskRuleHit(
    string Code,
    int Score,
    RiskRuleDecision Decision,
    string PublicReason)
{
    public static RiskRuleHit Info(string code, int score, string publicReason) => new(code, score, RiskRuleDecision.Informational, publicReason);

    public static RiskRuleHit Review(string code, int score, string publicReason) => new(code, score, RiskRuleDecision.ReviewRequired, publicReason);

    public static RiskRuleHit Reject(string code, int score, string publicReason) => new(code, score, RiskRuleDecision.Reject, publicReason);
}

public sealed record RiskCheckResult(
    Guid Id,
    Guid MailingId,
    int Score,
    RiskDecision Decision,
    IReadOnlyCollection<RiskRuleHit> TriggeredRules,
    DateTimeOffset CheckedAt,
    string CheckedBy,
    string PublicExplanation)
{
    public const string SystemChecker = "System";

    public static RiskCheckResult Create(Guid mailingId, IReadOnlyCollection<RiskRuleHit> rules, int reviewThreshold = 30)
    {
        var score = rules.Sum(rule => Math.Max(0, rule.Score));
        var decision = rules.Any(rule => rule.Decision == RiskRuleDecision.Reject)
            ? RiskDecision.Rejected
            : rules.Any(rule => rule.Decision == RiskRuleDecision.ReviewRequired) || score >= reviewThreshold
                ? RiskDecision.ReviewRequired
                : RiskDecision.Approved;

        var explanation = decision switch
        {
            RiskDecision.Approved => "Формальная проверка не выявила причин для ручной модерации.",
            RiskDecision.Rejected => "Рассылка отклонена по результатам формальной проверки.",
            _ => "Рассылка отправлена на ручную проверку. Мы покажем результат здесь."
        };

        return new RiskCheckResult(
            Guid.NewGuid(),
            mailingId,
            score,
            decision,
            rules.ToArray(),
            DateTimeOffset.UtcNow,
            SystemChecker,
            explanation);
    }
}

public enum ModerationReviewStatus
{
    Open,
    Approved,
    Rejected
}

public static class ModerationReviewStatusLabels
{
    public static string ToRu(this ModerationReviewStatus status) => status switch
    {
        ModerationReviewStatus.Approved => "Одобрено",
        ModerationReviewStatus.Rejected => "Отклонено",
        _ => "На ручной проверке"
    };
}

public sealed record ModerationReview(
    Guid Id,
    Guid MailingId,
    ModerationReviewStatus Status,
    string Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolvedBy,
    string? ModeratorComment)
{
    public static ModerationReview Create(Guid mailingId, string reason) => new(
        Guid.NewGuid(),
        mailingId,
        ModerationReviewStatus.Open,
        string.IsNullOrWhiteSpace(reason) ? "Требуется ручная проверка." : reason.Trim(),
        DateTimeOffset.UtcNow,
        null,
        null,
        null);

    public ModerationReview Approve(string moderatorEmail, string? comment) => Status == ModerationReviewStatus.Approved
        ? this
        : this with
        {
            Status = ModerationReviewStatus.Approved,
            ResolvedAt = ResolvedAt ?? DateTimeOffset.UtcNow,
            ResolvedBy = string.IsNullOrWhiteSpace(ResolvedBy) ? moderatorEmail : ResolvedBy,
            ModeratorComment = string.IsNullOrWhiteSpace(ModeratorComment) ? NormalizeComment(comment) : ModeratorComment
        };

    public ModerationReview Reject(string moderatorEmail, string? comment) => Status == ModerationReviewStatus.Rejected
        ? this
        : this with
        {
            Status = ModerationReviewStatus.Rejected,
            ResolvedAt = ResolvedAt ?? DateTimeOffset.UtcNow,
            ResolvedBy = string.IsNullOrWhiteSpace(ResolvedBy) ? moderatorEmail : ResolvedBy,
            ModeratorComment = string.IsNullOrWhiteSpace(ModeratorComment) ? NormalizeComment(comment) : ModeratorComment
        };

    private static string NormalizeComment(string? comment) => string.IsNullOrWhiteSpace(comment)
        ? string.Empty
        : comment.Trim();
}

public sealed record ModerationActionLog(
    Guid Id,
    Guid ReviewId,
    Guid MailingId,
    string ActorEmail,
    string Action,
    string? Comment,
    string PreviousState,
    string NewState,
    DateTimeOffset CreatedAt)
{
    public static ModerationActionLog Create(Guid reviewId, Guid mailingId, string actorEmail, string action, string? comment, string previousState, string newState) => new(
        Guid.NewGuid(),
        reviewId,
        mailingId,
        actorEmail.Trim().ToLowerInvariant(),
        action,
        string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
        previousState,
        newState,
        DateTimeOffset.UtcNow);
}
