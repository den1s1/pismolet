using System.Text;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Domain.Mailings;
using Pismolet.Web.Infrastructure.Audit;
using Pismolet.Web.Infrastructure.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class RecipientImportServiceTests
{
    private static readonly RequestMetadata Request = new("127.0.0.1", "unit-test");

    [Fact]
    public void EmailNormalizer_trims_and_lowercases_email()
    {
        var normalizer = new EmailNormalizer();

        Assert.Equal("client@test.local", normalizer.Normalize(" CLIENT@TEST.LOCAL "));
        Assert.Equal(string.Empty, normalizer.Normalize("   "));
    }

    [Fact]
    public async Task ImportCsv_counts_valid_duplicates_and_invalid_rows()
    {
        var mailings = new InMemoryMailingRepository();
        var audit = new InMemoryAuditLogger();
        var mailingService = new MailingService(mailings, audit, new EmailNormalizer());
        var created = mailingService.CreateDraft(new CreateMailingCommand("client@test.local", "Новости"), Request);
        var service = new RecipientImportService(
            mailings,
            new InMemoryGlobalSuppressionRepository(),
            new InMemoryClientSuppressionRepository(),
            new InMemorySendEventRepository(),
            new EmailNormalizer(),
            new EmailSyntaxValidator(),
            audit);

        await using var stream = ToStream("email\nfirst@test.local\nFIRST@test.local\nwrong-email\nsecond@test.local\n");
        var result = await service.ImportAsync(new ImportRecipientsCommand("client@test.local", created.Mailing!.Id, "list.csv", stream, Request));

        Assert.True(result.Ok);
        Assert.Equal(4, result.Stats.TotalRows);
        Assert.Equal(2, result.Stats.Accepted);
        Assert.Equal(1, result.Stats.Duplicates);
        Assert.Equal(1, result.Stats.Invalid);
        var recipients = mailings.GetForOwner(created.Mailing.Id, "client@test.local")!.Recipients;
        Assert.Equal(4, recipients.Count);
        Assert.Equal(2, recipients.Count(x => x.Status == RecipientStatus.Accepted));
        Assert.Contains(recipients, x => x.Status == RecipientStatus.Duplicate && x.Email == "first@test.local");
        Assert.Contains(recipients, x => x.Status == RecipientStatus.Invalid && x.SourceEmail == "wrong-email");
        Assert.Contains(audit.GetRecords(), record => record.EventType == "recipients_import_completed");
    }

    [Fact]
    public async Task ImportCsv_warns_about_repeated_soft_bounces_without_excluding_address()
    {
        var mailings = new InMemoryMailingRepository();
        var audit = new InMemoryAuditLogger();
        var sendEvents = new InMemorySendEventRepository();
        var mailingService = new MailingService(mailings, audit, new EmailNormalizer());
        var created = mailingService.CreateDraft(new CreateMailingCommand("client@test.local", "Новости"), Request);
        var service = new RecipientImportService(
            mailings,
            new InMemoryGlobalSuppressionRepository(),
            new InMemoryClientSuppressionRepository(),
            sendEvents,
            new EmailNormalizer(),
            new EmailSyntaxValidator(),
            audit);
        sendEvents.Save(SendEvent.Pending(Guid.NewGuid(), "client@test.local", "soft@test.local")
            .ApplyDeliveryStatus(DeliveryStatus.SoftBounce, DateTimeOffset.UtcNow.AddDays(-2), "mailbox full"));
        sendEvents.Save(SendEvent.Pending(Guid.NewGuid(), "client@test.local", "soft@test.local")
            .ApplyDeliveryStatus(DeliveryStatus.SoftBounce, DateTimeOffset.UtcNow.AddDays(-1), "temporary unavailable"));

        await using var stream = ToStream("email\nsoft@test.local\n");
        var result = await service.ImportAsync(new ImportRecipientsCommand("client@test.local", created.Mailing!.Id, "list.csv", stream, Request));

        Assert.True(result.Ok);
        Assert.Equal(1, result.Stats.Accepted);
        Assert.Single(result.Mailing!.Recipients);
        Assert.Contains(result.Mailing.LastImportBatch!.Issues, issue =>
            issue.Email == "soft@test.local" &&
            issue.Message.Contains("Временные ошибки доставки ранее: 2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_requires_email_column()
    {
        var mailings = new InMemoryMailingRepository();
        var created = new MailingService(mailings, new InMemoryAuditLogger(), new EmailNormalizer())
            .CreateDraft(new CreateMailingCommand("client@test.local", "Новости"), Request);
        var service = new RecipientImportService(
            mailings,
            new InMemoryGlobalSuppressionRepository(),
            new InMemoryClientSuppressionRepository(),
            new InMemorySendEventRepository(),
            new EmailNormalizer(),
            new EmailSyntaxValidator(),
            new InMemoryAuditLogger());

        await using var stream = ToStream("name\nclient@test.local\n");
        var result = await service.ImportAsync(new ImportRecipientsCommand("client@test.local", created.Mailing!.Id, "list.csv", stream, Request));

        Assert.False(result.Ok);
        Assert.Equal("В файле должна быть колонка email.", result.Error);
    }

    private static MemoryStream ToStream(string content) => new(Encoding.UTF8.GetBytes(content));
}
