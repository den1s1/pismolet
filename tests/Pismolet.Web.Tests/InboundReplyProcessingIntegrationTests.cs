using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Common;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class InboundReplyProcessingIntegrationTests
{
    [Fact]
    public async Task Inbound_event_reaches_processing_and_creates_reply_event()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var scope = factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IInboundReplyProcessingService>();
        var replies = scope.ServiceProvider.GetRequiredService<IReplyEventRepository>();
        var inbound = new EmailProviderInboundEvent(
            Provider: "PostfixSpool",
            ProviderInboundEventId: Guid.NewGuid().ToString("N"),
            FromEmail: "client@example.test",
            ToAddress: $"reply+token{Convert.ToChar(64)}reply.pismolet.test",
            ReplyToken: "token",
            Subject: "Re: Проверка ответа",
            TextBody: "Тестовый ответ получателя.",
            HtmlBody: null,
            Headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            ReceivedAt: DateTimeOffset.UtcNow,
            RawPayload: "raw-payload-marker");

        var result = await processor.ProcessAsync(
            inbound,
            new RequestMetadata("test", "inbound-reply-processing-test"),
            CancellationToken.None);

        Assert.Equal("unmatched", result.Status);
        Assert.NotNull(result.ReplyEventId);
        var saved = replies.ListRecent(20).SingleOrDefault(x => x.Id == result.ReplyEventId);
        Assert.NotNull(saved);
        Assert.Equal("client@example.test", saved.FromEmailNormalized);
        Assert.Equal("Re: Проверка ответа", saved.SubjectPreview);
        Assert.Contains("Тестовый ответ", saved.BodyText);
    }
}
