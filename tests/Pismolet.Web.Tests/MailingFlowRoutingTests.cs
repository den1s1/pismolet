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

        AssertPreferredRouteOrder(endpoints, "/mailings/{id:guid}/message", "GET", -2000);
        AssertPreferredRouteOrder(endpoints, "/mailings/{id:guid}/message", "POST", -2000);
        AssertPreferredRouteOrder(endpoints, "/mailings/{id:guid}/message/preview", "GET", -2000);
        AssertPreferredRouteOrder(endpoints, "/mailings/{id:guid}/recipients", "GET", -3000);
        AssertPreferredRouteOrder(endpoints, "/mailings/{id:guid}/recipients", "POST", -3000);
        AssertPreferredRouteOrder(endpoints, "/mailings/{id:guid}/confirmation", "GET", -3000);
        AssertPreferredRouteOrder(endpoints, "/mailings/{id:guid}/confirmation", "POST", -3000);
    }

    [Fact]
    public void Mailing_flow_does_not_register_legacy_fallback_routes_for_later_steps()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var scope = factory.Services.CreateScope();
        var endpoints = scope.ServiceProvider
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        AssertNoLegacyFallbackRoute(endpoints, "/mailings/{id:guid}/message", "GET");
        AssertNoLegacyFallbackRoute(endpoints, "/mailings/{id:guid}/message", "POST");
        AssertNoLegacyFallbackRoute(endpoints, "/mailings/{id:guid}/recipients", "GET");
        AssertNoLegacyFallbackRoute(endpoints, "/mailings/{id:guid}/recipients", "POST");
        AssertNoLegacyFallbackRoute(endpoints, "/mailings/{id:guid}/confirmation", "GET");
        AssertNoLegacyFallbackRoute(endpoints, "/mailings/{id:guid}/confirmation", "POST");
    }

    private static void AssertPreferredRouteOrder(IReadOnlyCollection<RouteEndpoint> endpoints, string pattern, string method, int expectedOrder)
    {
        var matches = MatchingRoutes(endpoints, pattern, method);

        Assert.NotEmpty(matches);
        Assert.Equal(expectedOrder, matches.Min(endpoint => endpoint.Order));
        Assert.All(matches, endpoint => Assert.True(endpoint.Order >= expectedOrder, $"Route {method} {pattern} has unexpected higher priority order {endpoint.Order}."));
    }

    private static void AssertNoLegacyFallbackRoute(IReadOnlyCollection<RouteEndpoint> endpoints, string pattern, string method)
    {
        var matches = MatchingRoutes(endpoints, pattern, method);
        Assert.DoesNotContain(matches, endpoint => endpoint.Order is -1000 or -200);
    }

    private static RouteEndpoint[] MatchingRoutes(IReadOnlyCollection<RouteEndpoint> endpoints, string pattern, string method) => endpoints
        .Where(endpoint => endpoint.RoutePattern.RawText == pattern && SupportsMethod(endpoint, method))
        .ToArray();

    private static bool SupportsMethod(RouteEndpoint endpoint, string method)
    {
        var metadata = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
        return metadata?.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase) == true;
    }
}
