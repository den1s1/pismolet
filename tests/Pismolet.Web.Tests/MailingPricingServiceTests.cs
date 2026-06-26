using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailingPricingServiceTests
{
    [Theory]
    [InlineData(299, 1, 299)]
    [InlineData(300, 0.90, 270)]
    [InlineData(500, 0.80, 400)]
    [InlineData(1000, 0.70, 700)]
    public void Calculate_uses_public_tariff_for_payment_review(int acceptedRecipients, decimal expectedUnitPrice, decimal expectedTotal)
    {
        var service = new MailingPricingService(new InMemoryPriceSettingsRepository());
        var mailing = ReadyMailing(acceptedRecipients);

        var review = service.Calculate(mailing);

        Assert.Equal(expectedUnitPrice, review.PricePerRecipient);
        Assert.Equal(expectedTotal, review.TotalAmount);
        Assert.Equal("RUB", review.Currency);
    }

    private static Mailing ReadyMailing(int acceptedRecipients)
    {
        var mailing = Mailing.Draft("owner@example.test", "Pricing test");
        var recipients = Enumerable.Range(1, acceptedRecipients)
            .Select(i => Recipient.Accepted($"lead{i}@example.test", $"lead{i}@example.test", rowNumber: i))
            .ToArray();

        return mailing
            .WithImportResult(new ImportStats(acceptedRecipients, acceptedRecipients, 0, 0, 0), recipients)
            .WithDeclaration(new MailingDeclaration(
                mailing.Id,
                mailing.OwnerEmail,
                BaseSource.Customers,
                true,
                false,
                BaseDeclarationText.CurrentVersion,
                DateTimeOffset.UtcNow,
                "127.0.0.1",
                "pricing-test"))
            .WithMessageDraft(MailingMessageDraft.Create("Sender", "Subject", "Body", MessageType.Transactional, DateTimeOffset.UtcNow));
    }
}
