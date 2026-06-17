using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Domain.Users;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class RiskCheckServiceTests
{
    [Fact]
    public void NormalMessageWithoutForcedPremoderationIsApproved()
    {
        var service = new RiskCheckService();
        var mailing = BuildMailing("ООО Ромашка", "Здравствуйте! Приглашаем вас на обновление сервиса. Подробнее: https://example.com/news");
        var owner = BuildOwner(premoderationRequired: false);

        var result = service.Check(mailing, owner);

        Assert.Equal(RiskDecision.Approved, result.Decision);
    }

    [Fact]
    public void NonHttpsLinkRequiresManualReview()
    {
        var service = new RiskCheckService();
        var mailing = BuildMailing("ООО Ромашка", "Здравствуйте! Подробности доступны по ссылке http://example.com/promo");
        var owner = BuildOwner(premoderationRequired: false);

        var result = service.Check(mailing, owner);

        Assert.Equal(RiskDecision.ReviewRequired, result.Decision);
        Assert.Contains(result.TriggeredRules, rule => rule.PublicReason.Contains("HTTPS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShortSenderRequiresManualReview()
    {
        var service = new RiskCheckService();
        var mailing = BuildMailing("Я", "Здравствуйте! Нейтральное информационное письмо без ссылок.");
        var owner = BuildOwner(premoderationRequired: false);

        var result = service.Check(mailing, owner);

        Assert.Equal(RiskDecision.ReviewRequired, result.Decision);
    }

    [Fact]
    public void ForcedPremoderationAlwaysRequiresManualReview()
    {
        var service = new RiskCheckService();
        var mailing = BuildMailing("ООО Ромашка", "Здравствуйте! Нейтральное информационное письмо без подозрительных признаков.");
        var owner = BuildOwner(premoderationRequired: true);

        var result = service.Check(mailing, owner);

        Assert.Equal(RiskDecision.ReviewRequired, result.Decision);
        Assert.Contains(result.TriggeredRules, rule => rule.PublicReason.Contains("премодера", StringComparison.OrdinalIgnoreCase) || rule.PublicReason.Contains("Новый клиент", StringComparison.OrdinalIgnoreCase));
    }

    private static Mailing BuildMailing(string senderName, string body)
    {
        var mailing = Mailing.Draft("owner@example.test", "Тестовая рассылка");
        return mailing with
        {
            LastImportStats = new ImportStats(1, 1, 0, 0, 0),
            Recipients = new List<Recipient> { Recipient.Accepted("lead@example.test", "lead@example.test") },
            Declaration = new MailingDeclaration(
                mailing.Id,
                mailing.OwnerEmail,
                BaseSource.Customers,
                IsBaseLegalityConfirmed: true,
                IsAdvertisingConsentConfirmed: false,
                BaseDeclarationText.CurrentVersion,
                DateTimeOffset.UtcNow,
                "127.0.0.1",
                "test"),
            MessageDraft = MailingMessageDraft.Create(senderName, "Тема письма", body, MessageType.Transactional, DateTimeOffset.UtcNow)
        };
    }

    private static UserAccount BuildOwner(bool premoderationRequired) => new(
        Email: "owner@example.test",
        PasswordHash: "hash",
        DisplayName: "Owner",
        ConfirmationToken: "token",
        EmailConfirmed: true,
        Profile: new ClientProfile("active", 1000, 10000, premoderationRequired),
        Mailings: new List<Mailing>());
}
