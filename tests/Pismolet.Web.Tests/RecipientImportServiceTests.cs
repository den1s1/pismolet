using System.Text;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Imports;
using Pismolet.Web.Application.Mailings;
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

        Assert.Equal("client@example.com", normalizer.Normalize(" CLIENT@EXAMPLE.COM "));
        Assert.Equal(string.Empty, normalizer.Normalize("   "));
    }

    [Fact]
    public async Task ImportCsv_counts_valid_duplicates_invalid_and_global_opt_outs()
    {
        var mailings = new InMemoryMailingRepository();
        var audit = new InMemoryAuditLogger();
        var mailingService = new MailingService(mailings, audit, new EmailNormalizer());
        var created = mailingService.CreateDraft(new CreateMailingCommand("client@example.com", "Новости"), Request);
        var optOuts = new InMemoryGlobalSuppressionRepository();
        optOuts.Add("blocked@example.com");
        var service = new RecipientImportService(
            mailings,
            optOuts,
            new EmailNormalizer(),
            new EmailSyntaxValidator(),
            audit);

        await using var stream = ToStream("email\nfirst@example.com\nFIRST@example.com\nwrong-email\nblocked@example.com\nsecond@example.com\n");
        var result = await service.ImportAsync(new ImportRecipientsCommand("client@example.com", created.Mailing!.Id, "list.csv", stream, Request));

        Assert.True(result.Ok);
        Assert.Equal(5, result.Stats.TotalRows);
        Assert.Equal(2, result.Stats.Accepted);
        Assert.Equal(1, result.Stats.Duplicates);
        Assert.Equal(1, result.Stats.Invalid);
        Assert.Equal(1, result.Stats.GloballySuppressed);
        Assert.Equal(2, mailings.GetForOwner(created.Mailing.Id, "client@example.com")!.Recipients.Count);
        Assert.NotNull(mailings.GetForOwner(created.Mailing.Id, "client@example.com")!.LastImportBatch);
        Assert.Contains(audit.GetRecords(), record => record.EventType == "recipients_import_completed");
    }

    [Fact]
    public async Task Import_requires_email_column()
    {
        var mailings = new InMemoryMailingRepository();
        var created = new MailingService(mailings, new InMemoryAuditLogger(), new EmailNormalizer())
            .CreateDraft(new CreateMailingCommand("client@example.com", "Новости"), Request);
        var service = new RecipientImportService(
            mailings,
            new InMemoryGlobalSuppressionRepository(),
            new EmailNormalizer(),
            new EmailSyntaxValidator(),
            new InMemoryAuditLogger());

        await using var stream = ToStream("name\nclient@example.com\n");
        var result = await service.ImportAsync(new ImportRecipientsCommand("client@example.com", created.Mailing!.Id, "list.csv", stream, Request));

        Assert.False(result.Ok);
        Assert.Equal("В файле должна быть колонка email.", result.Error);
    }

    private static MemoryStream ToStream(string content) => new(Encoding.UTF8.GetBytes(content));
}
