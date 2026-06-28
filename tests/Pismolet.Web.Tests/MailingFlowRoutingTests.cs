using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailingFlowRoutingTests
{
    [Fact]
    public void Mailing_flow_routes_prefer_current_message_and_recipient_steps()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var scope = factory.Services.CreateScope();
        var endpoints = scope.ServiceProvider
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        AssertPreferredRouteOrder(endpoints, "/mailings/{id:guid}/message", -2000);
        AssertPreferredRouteOrder(endpoints, "/mailings/{id:guid}/message/preview", -2000);
        AssertPreferredRouteOrder(endpoints, "/mailings/{id:guid}/recipients", -3000);
        AssertPreferredRouteOrder(endpoints, "/mailings/{id:guid}/confirmation", -3000);
    }

    private static void AssertPreferredRouteOrder(IReadOnlyCollection<RouteEndpoint> endpoints, string pattern, int expectedOrder)
    {
        var matches = endpoints.Where(endpoint => endpoint.RoutePattern.RawText == pattern).ToArray();

        Assert.NotEmpty(matches);
        Assert.Equal(expectedOrder, matches.Min(endpoint => endpoint.Order));
        Assert.All(matches, endpoint => Assert.True(endpoint.Order >= expectedOrder, $"Route {pattern} has unexpected higher priority order {endpoint.Order}."));
    }
}
