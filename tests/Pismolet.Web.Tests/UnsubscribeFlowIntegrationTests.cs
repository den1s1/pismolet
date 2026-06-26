using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pismolet.Web.Application.Mailings;
using Pismolet.Web.Application.Persistence;

namespace Pismolet.Web.Tests;

public sealed class UnsubscribeFlowIntegrationTests
{
    [Fact]
    public async Task Get_unsubscribe_page_with_valid_token_works_without_login()
    {
        using var factory = CreateFactory();
        var token = GenerateToken(factory, "User@Example.Test");
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync($"/unsubscribe/{token}");

        Assert.Contains("Отписка от рассылок", html);
        Assert.Contains("u***@example.test", html);
        Assert.Contains("Отписаться", html);
        Assert.Contains("/legal/unsubscribe", html);
        Assert.Contains("returnUrl=", html);
    }

    [Fact]
    public async Task Post_unsubscribe_adds_global_suppression_without_login()
    {
        using var factory = CreateFactory();
        var token = GenerateToken(factory, "User@Example.Test");
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/u/{token}", new FormUrlEncodedContent(new Dictionary<string, string>()));
        var html = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("Вы отписаны", html);
        Assert.Contains("/legal/unsubscribe", html);

        using var scope = factory.Services.CreateScope();
        var suppressions = scope.ServiceProvider.GetRequiredService<IGlobalSuppressionRepository>();
        Assert.True(suppressions.IsSuppressed("user@example.test"));
    }

    [Fact]
    public async Task Repeated_post_unsubscribe_is_idempotent()
    {
        using var factory = CreateFactory();
        var token = GenerateToken(factory, "repeat@example.test");
        using var client = factory.CreateClient();

        var first = await client.PostAsync($"/unsubscribe/{token}", new FormUrlEncodedContent(new Dictionary<string, string>()));
        var second = await client.PostAsync($"/unsubscribe/{token}", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var scope = factory.Services.CreateScope();
        var suppressions = scope.ServiceProvider.GetRequiredService<IGlobalSuppressionRepository>();
        Assert.True(suppressions.IsSuppressed("repeat@example.test"));
    }

    [Fact]
    public async Task Invalid_token_does_not_disclose_email_or_mailing()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/unsubscribe/not-a-valid-token", new FormUrlEncodedContent(new Dictionary<string, string>()));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Отписка не выполнена", html);
        Assert.Contains("Ссылка отписки недействительна или устарела", html);
        Assert.DoesNotContain("@", html);
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
                    ["Unsubscribe:Secret"] = "integration-test-unsubscribe-secret",
                    ["Unsubscribe:TokenLifetimeDays"] = "30"
                });
            });
        });

    private static string GenerateToken(WebApplicationFactory<Program> factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IUnsubscribeTokenService>();
        return tokens.Generate(Guid.Parse("33333333-3333-3333-3333-333333333333"), email);
    }
}
