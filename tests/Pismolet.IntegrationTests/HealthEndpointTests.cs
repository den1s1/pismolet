using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Pismolet.IntegrationTests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ok", body);
    }
}
