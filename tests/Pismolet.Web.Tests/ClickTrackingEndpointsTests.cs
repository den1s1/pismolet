using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Persistence;
using Pismolet.Web.Domain.Mailings;

namespace Pismolet.Web.Tests;

public sealed class ClickTrackingEndpointsTests
{
    [Fact]
    public async Task Click_tracking_redirects_to_original_url_and_records_click()
    {
        using var factory = CreateFactory();
        var mailingId = Guid.NewGuid();
        var trackedLink = SeedTrackedLink(factory, mailingId, "recipient@example.test", "https://example.org/page?utm=test");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync($"/t/click/{trackedLink.Token}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(trackedLink.OriginalUrl, response.Headers.Location?.ToString());

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IClickTrackingRepository>();
        var updatedLink = repository.GetByToken(trackedLink.Token);
        Assert.NotNull(updatedLink);
        Assert.NotNull(updatedLink.FirstClickedAt);
        Assert.NotNull(updatedLink.LastClickedAt);
        Assert.Equal(1, updatedLink.ClickCount);

        var events = repository.ListEventsByMailingId(mailingId, 10);
        var clickEvent = Assert.Single(events);
        Assert.Equal(trackedLink.Token, clickEvent.Token);
        Assert.Equal(trackedLink.OriginalUrl, clickEvent.OriginalUrl);
        Assert.Equal("recipient@example.test", clickEvent.RecipientEmail);
    }

    [Fact]
    public async Task Click_tracking_returns_not_found_for_unknown_token()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/t/click/unknown-token");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));

    private static TrackedLink SeedTrackedLink(WebApplicationFactory<Program> factory, Guid mailingId, string recipientEmail, string originalUrl)
    {
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IClickTrackingRepository>();
        return repository.AddOrGet(TrackedLink.Create(mailingId, recipientEmail, originalUrl));
    }
}
