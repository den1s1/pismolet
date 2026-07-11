using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Audit;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailingRecipientReasonTests
{
    private static readonly RequestMetadata Request = new("127.0.0.1", "unit-test");

    [Fact]
    public void Save_rejects_explicitly_empty_recipient_reason()
    {
        var repository = new InMemoryMailingRepository();
        var mailing = Mailing.Draft("client@example.com", "Новости");
        repository.TryAdd(mailing);
        var service = new MailingMessageService(repository, new EmailNormalizer(), new InMemoryAuditLogger());

        var result = service.Save(new SaveMailingMessageCommand(
            "client@example.com",
            mailing.Id,
            "Библиотека №5",
            "Новости",
            "Текст письма",
            MessageType.Transactional,
            Request,
            RecipientReason: "   "));

        Assert.False(result.Ok);
        Assert.Equal("Объясните, почему получатель получает это письмо.", result.Error);
    }

    [Fact]
    public void Save_trims_and_stores_recipient_reason_separately()
    {
        var repository = new InMemoryMailingRepository();
        var mailing = Mailing.Draft("client@example.com", "Новости");
        repository.TryAdd(mailing);
        var service = new MailingMessageService(repository, new EmailNormalizer(), new InMemoryAuditLogger());

        var result = service.Save(new SaveMailingMessageCommand(
            "client@example.com",
            mailing.Id,
            "Библиотека №5",
            "Новости",
            "Текст письма",
            MessageType.Transactional,
            Request,
            RecipientReason: "  Вы записались на встречу в библиотеке.  "));

        Assert.True(result.Ok);
        Assert.Equal("Вы записались на встречу в библиотеке.", result.Mailing?.RecipientReason);
        Assert.Equal("Текст письма", result.Mailing?.MessageDraft?.Body);
    }

    [Fact]
    public void Save_rejects_recipient_reason_over_limit()
    {
        var repository = new InMemoryMailingRepository();
        var mailing = Mailing.Draft("client@example.com", "Новости");
        repository.TryAdd(mailing);
        var service = new MailingMessageService(repository, new EmailNormalizer(), new InMemoryAuditLogger());

        var result = service.Save(new SaveMailingMessageCommand(
            "client@example.com",
            mailing.Id,
            "Библиотека №5",
            "Новости",
            "Текст письма",
            MessageType.Transactional,
            Request,
            RecipientReason: new string('а', Mailing.MaxRecipientReasonLength + 1)));

        Assert.False(result.Ok);
        Assert.Contains(Mailing.MaxRecipientReasonLength.ToString(), result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Footer_uses_user_reason_and_exact_unsubscribe_wording()
    {
        const string recipientReason = "Вы зарегистрировались на конференцию 5 июля 2026 года.";

        var reason = MailingServiceEmailFooter.Reason(recipientReason);
        var text = MailingServiceEmailFooter.PlainText("Основной текст", recipientReason, "/unsubscribe/token", "Служебный идентификатор рассылки: PL-123");

        Assert.Equal($"{recipientReason} {MailingServiceEmailFooter.UnsubscribeExplanation}", reason);
        Assert.Contains(recipientReason, text, StringComparison.Ordinal);
        Assert.Contains("Если вы не хотите получать письма через Письмолёт, вы можете отписаться от всех рассылок через сервис.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("потому что отправитель указал", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preview_contains_user_reason_and_fixed_unsubscribe_sentence()
    {
        var mailing = Mailing.Draft("client@example.com", "Новости") with
        {
            RecipientReason = "Вы оставили адрес при регистрации на встречу.",
            MessageDraft = MailingMessageDraft.Create("Библиотека №5", "Новости", "Основной текст", MessageType.Transactional, DateTimeOffset.UtcNow)
        };

        var preview = new MessageRenderingService().RenderPreview(mailing);

        Assert.Contains(mailing.RecipientReason, preview.ReasonBlock, StringComparison.Ordinal);
        Assert.Contains(MailingServiceEmailFooter.UnsubscribeExplanation, preview.ReasonBlock, StringComparison.Ordinal);
        Assert.Contains(mailing.RecipientReason, preview.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void Risk_check_requires_recipient_reason()
    {
        var mailing = Mailing.Draft("client@example.com", "Новости") with
        {
            MessageDraft = MailingMessageDraft.Create("Библиотека №5", "Новости", "Основной текст", MessageType.Transactional, DateTimeOffset.UtcNow)
        };

        var result = new RiskCheckService().Check(mailing, null);

        Assert.Contains(result.TriggeredRules, rule => rule.Code == "recipient_reason_missing");
    }

    [Theory]
    [InlineData("Вы подписались на новости на https://example.test")]
    [InlineData("<b>Вы зарегистрировались на встречу</b>")]
    public void Risk_check_marks_link_or_markup_in_recipient_reason(string recipientReason)
    {
        var mailing = Mailing.Draft("client@example.com", "Новости") with
        {
            RecipientReason = recipientReason,
            MessageDraft = MailingMessageDraft.Create("Библиотека №5", "Новости", "Основной текст", MessageType.Transactional, DateTimeOffset.UtcNow)
        };

        var result = new RiskCheckService().Check(mailing, null);

        Assert.Contains(result.TriggeredRules, rule => rule.Code == "recipient_reason_contains_link_or_markup");
    }
}
