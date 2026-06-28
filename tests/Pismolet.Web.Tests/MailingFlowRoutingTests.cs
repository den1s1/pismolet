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

        Assert.Contains(endpoints, endpoint => endpoint.RoutePattern.RawText == "/mailings/{id:guid}/message" && endpoint.Order == -2000);
        Assert.Contains(endpoints, endpoint => endpoint.RoutePattern.RawText == "/mailings/{id:guid}/message/preview" && endpoint.Order == -2000);
        Assert.Contains(endpoints, endpoint => endpoint.RoutePattern.RawText == "/mailings/{id:guid}/recipients" && endpoint.Order == -3000);
        Assert.Contains(endpoints, endpoint => endpoint.RoutePattern.RawText == "/mailings/{id:guid}/confirmation" && endpoint.Order == -3000);
    }
}
