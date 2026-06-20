using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Tests;

public sealed class WebhookEndpointIntegrationTests
{
    [Fact]
    public async Task Fake_webhook_endpoint_requires_secret()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/webhooks/email/fake", new { providerEventId = "evt-no-secret", eventType = "delivered" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Fake_delivered_webhook_updates_delivery_summary_once()
    {
        using var factory = CreateFactory();
        var mailingId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        SeedAcceptedSendEvent(factory, mailingId, "delivered@example.test", "fake-delivered-1");
        using var client = factory.CreateClient();
        var payload = new
        {
            providerEventId = "evt-delivered-1",
            providerMessageId = "fake-delivered-1",
            mailingId,
            recipientEmail = "delivered@example.test",
            eventType = "delivered",
            occurredAt = DateTimeOffset.UtcNow
        };

        var first = await PostWebhook(client, payload);
        var second = await PostWebhook(client, payload);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var scope = factory.Services.CreateScope();
        var sendEvents = scope.ServiceProvider.GetRequiredService<ISendEventRepository>();
        var webhookEvents = scope.ServiceProvider.GetRequiredService<IProviderWebhookEventRepository>();
        Assert.Equal(DeliveryStatus.Delivered, sendEvents.Get(mailingId, "delivered@example.test")!.DeliveryStatus);
        Assert.Single(webhookEvents.ListByMailingId(mailingId));
        Assert.Equal(1, sendEvents.GetSummary(mailingId, 1).Delivered);
    }

    [Fact]
    public async Task Fake_hard_bounce_webhook_creates_client_suppression()
    {
        using var factory = CreateFactory();
        var mailingId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        SeedAcceptedSendEvent(factory, mailingId, "bounce@example.test", "fake-bounce-1");
        using var client = factory.CreateClient();

        var response = await PostWebhook(client, new
        {
            providerEventId = "evt-hard-bounce-1",
            providerMessageId = "fake-bounce-1",
            mailingId,
            recipientEmail = "bounce@example.test",
            eventType = "hard_bounce",
            reasonCode = "hard",
            reasonMessage = "Mailbox not found",
            occurredAt = DateTimeOffset.UtcNow
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var clientSuppressions = scope.ServiceProvider.GetRequiredService<IClientSuppressionRepository>();
        Assert.True(clientSuppressions.IsSuppressed("owner@example.test", "bounce@example.test"));
    }

    [Fact]
    public async Task Fake_complaint_webhook_creates_global_suppression()
    {
        using var factory = CreateFactory();
        var mailingId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        SeedAcceptedSendEvent(factory, mailingId, "complaint@example.test", "fake-complaint-1");
        using var client = factory.CreateClient();

        var response = await PostWebhook(client, new
        {
            providerEventId = "evt-complaint-1",
            providerMessageId = "fake-complaint-1",
            mailingId,
            recipientEmail = "complaint@example.test",
            eventType = "complaint",
            occurredAt = DateTimeOffset.UtcNow
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var suppressions = scope.ServiceProvider.GetRequiredService<IGlobalSuppressionRepository>();
        Assert.True(suppressions.IsSuppressed("complaint@example.test"));
    }

    private static async Task<HttpResponseMessage> PostWebhook(HttpClient client, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/email/fake")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-Pismolet-Webhook-Secret", "integration-test-webhook-secret");
        return await client.SendAsync(request);
    }

    private static void SeedAcceptedSendEvent(WebApplicationFactory<Program> factory, Guid mailingId, string email, string providerMessageId)
    {
        using var scope = factory.Services.CreateScope();
        var sendEvents = scope.ServiceProvider.GetRequiredService<ISendEventRepository>();
        sendEvents.Save(SendEvent.Pending(mailingId, "owner@example.test", email).MarkAccepted(providerMessageId));
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "InMemory",
                    ["Webhooks:FakeProviderSecret"] = "integration-test-webhook-secret",
                    ["Unsubscribe:Secret"] = "integration-test-unsubscribe-secret"
                });
            });
        });
}