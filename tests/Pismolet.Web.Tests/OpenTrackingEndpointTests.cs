using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Tests;

public sealed class OpenTrackingEndpointTests
{
    [Fact]
    public async Task Open_tracking_returns_pixel_for_unknown_token()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/t/open/unknown-token.gif");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/gif", response.Content.Headers.ContentType?.MediaType);
        Assert.True((await response.Content.ReadAsByteArrayAsync()).Length > 0);
    }

    [Fact]
    public async Task Open_tracking_updates_first_last_and_count()
    {
        using var factory = CreateFactory();
        var mailingId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        const string token = "open-tracking-token-1";
        SeedSendEvent(factory, mailingId, "opened@example.test", token);
        using var client = factory.CreateClient();

        var first = await client.GetAsync($"/t/open/{token}.gif");
        var second = await client.GetAsync($"/t/open/{token}.gif");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var scope = factory.Services.CreateScope();
        var sendEvents = scope.ServiceProvider.GetRequiredService<ISendEventRepository>();
        var updated = sendEvents.Get(mailingId, "opened@example.test");
        Assert.NotNull(updated);
        Assert.NotNull(updated!.FirstOpenedAt);
        Assert.NotNull(updated.LastOpenedAt);
        Assert.Equal(2, updated.OpenCount);
        Assert.True(updated.LastOpenedAt >= updated.FirstOpenedAt);
    }

    [Fact]
    public void Pending_send_event_gets_tracking_token()
    {
        var item = SendEvent.Pending(Guid.NewGuid(), "owner@example.test", "recipient@example.test");

        Assert.False(string.IsNullOrWhiteSpace(item.TrackingToken));
    }

    [Fact]
    public void Mark_opened_preserves_first_opened_at_and_increments_count()
    {
        var firstOpen = DateTimeOffset.Parse("2026-06-22T10:00:00+00:00");
        var secondOpen = DateTimeOffset.Parse("2026-06-22T10:05:00+00:00");
        var item = SendEvent.Pending(Guid.NewGuid(), "owner@example.test", "recipient@example.test")
            .MarkOpened(firstOpen)
            .MarkOpened(secondOpen);

        Assert.Equal(firstOpen, item.FirstOpenedAt);
        Assert.Equal(secondOpen, item.LastOpenedAt);
        Assert.Equal(2, item.OpenCount);
    }

    private static void SeedSendEvent(WebApplicationFactory<Program> factory, Guid mailingId, string email, string trackingToken)
    {
        using var scope = factory.Services.CreateScope();
        var sendEvents = scope.ServiceProvider.GetRequiredService<ISendEventRepository>();
        sendEvents.Save(SendEvent.Pending(mailingId, "owner@example.test", email).MarkAccepted("fake-open-1") with { TrackingToken = trackingToken });
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
                    ["Unsubscribe:Secret"] = "integration-test-unsubscribe-secret"
                });
            });
        });
}
