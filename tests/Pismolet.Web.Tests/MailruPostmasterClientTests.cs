using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Pismolet.Web.Infrastructure.Postmaster;
using Xunit;

namespace Pismolet.Web.Tests;

public sealed class MailruPostmasterClientTests
{
    [Fact]
    public async Task Token_provider_refreshes_and_caches_access_token_without_repeating_request()
    {
        var handler = new QueueHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"expires_in\":3600,\"access_token\":\"secret-access-token\"}")
        });
        var factory = new StubHttpClientFactory(new Dictionary<string, HttpClient>
        {
            [MailruPostmasterOptions.OAuthHttpClientName] = new(handler) { BaseAddress = new Uri("https://o2.mail.ru/") }
        });
        var provider = new MailruPostmasterTokenProvider(factory, EnabledOptions(), NullLogger<MailruPostmasterTokenProvider>.Instance);

        var first = await provider.GetAccessTokenAsync();
        var second = await provider.GetAccessTokenAsync();

        Assert.True(first.IsSuccess);
        Assert.Equal("secret-access-token", first.AccessToken);
        Assert.Equal(first.AccessToken, second.AccessToken);
        Assert.Equal(1, handler.CallCount);
        Assert.Contains("client_id=postmaster_api_client", handler.RequestBodies.Single(), StringComparison.Ordinal);
        Assert.Contains("grant_type=refresh_token", handler.RequestBodies.Single(), StringComparison.Ordinal);
        Assert.Contains("refresh_token=secret-refresh-token", handler.RequestBodies.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_retries_once_after_403_with_forced_token_refresh()
    {
        var handler = new QueueHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"error\":\"forbidden\"}")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"domains\":[{\"domain\":\"pismolet.ru\"}]}")
            });
        var tokenProvider = new SequenceTokenProvider("old-token", "new-token");
        var client = CreateClient(handler, tokenProvider);

        var result = await client.GetRegisteredDomainsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("pismolet.ru", Assert.Single(result.Data!).Domain);
        Assert.Equal(new[] { false, true }, tokenProvider.ForceRefreshCalls);
        Assert.Equal(new[] { "old-token", "new-token" }, handler.BearerHeaders);
    }

    [Fact]
    public async Task Client_parses_troubles_and_detailed_statistics()
    {
        var handler = new QueueHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"data\":[{\"domain\":\"pismolet.ru\",\"errors\":[{\"msg\":\"DMARC check failed.\",\"code\":-1}]}]}")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"data\":[{\"domain\":\"pismolet.ru\",\"data\":[{\"date\":\"2026-07-09\",\"messages_sent\":152,\"delivered\":45,\"probably_spam\":107,\"spam\":0,\"complaints\":0,\"read\":12,\"deleted_read\":1,\"deleted_unread\":3,\"spam_percent\":0,\"probably_spam_percent\":70.4,\"reputation\":0,\"trend\":-4.2}]}]}")
            });
        var client = CreateClient(handler, new SequenceTokenProvider("token", "token"));

        var troubles = await client.GetTroublesAsync();
        var detailed = await client.GetDetailedStatisticsAsync(
            new DateOnly(2026, 7, 9),
            new DateOnly(2026, 7, 9),
            "pismolet.ru");

        var trouble = Assert.Single(troubles.Data!);
        Assert.Equal(-1, trouble.Code);
        Assert.Equal("DMARC check failed.", trouble.Message);

        var metric = Assert.Single(detailed.Data!);
        Assert.Equal(new DateOnly(2026, 7, 9), metric.Date);
        Assert.Equal(152, metric.MessagesSent);
        Assert.Equal(107, metric.ProbablySpam);
        Assert.Equal(70.4, metric.ProbablySpamPercent, 3);
        Assert.Equal(-4.2, metric.Trend, 3);
        Assert.Contains("date_from=2026-07-09", handler.RequestUris[1].Query, StringComparison.Ordinal);
        Assert.Contains("domain=pismolet.ru", handler.RequestUris[1].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_returns_rate_limit_details_without_waiting_for_long_delay()
    {
        var handler = new QueueHttpMessageHandler(new HttpResponseMessage((HttpStatusCode)429)
        {
            Content = new StringContent("{\"detail\":\"Запрос был проигнорирован. Expected available in 35 seconds.\"}")
        });
        var options = EnabledOptions() with { MaxRateLimitDelay = TimeSpan.FromSeconds(1) };
        var client = CreateClient(handler, new SequenceTokenProvider("token", "token"), options);

        var result = await client.GetRegisteredDomainsAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("rate_limited", result.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(35), result.RetryAfter);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Client_rejects_msgtype_without_domain_before_http_request()
    {
        var handler = new QueueHttpMessageHandler();
        var client = CreateClient(handler, new SequenceTokenProvider("token", "token"));

        var result = await client.GetStatisticsAsync(
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 9),
            messageType: "mailing123");

        Assert.False(result.IsSuccess);
        Assert.Equal("domain_required_for_msgtype", result.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    private static MailruPostmasterClient CreateClient(
        QueueHttpMessageHandler handler,
        IMailruPostmasterTokenProvider tokenProvider,
        MailruPostmasterOptions? options = null)
    {
        var factory = new StubHttpClientFactory(new Dictionary<string, HttpClient>
        {
            [MailruPostmasterOptions.ApiHttpClientName] = new(handler) { BaseAddress = new Uri("https://postmaster.mail.ru/") }
        });
        return new MailruPostmasterClient(
            factory,
            tokenProvider,
            options ?? EnabledOptions(),
            NullLogger<MailruPostmasterClient>.Instance);
    }

    private static MailruPostmasterOptions EnabledOptions() => new(
        Enabled: true,
        Domain: "pismolet.ru",
        RefreshToken: "secret-refresh-token",
        OAuthBaseUri: new Uri("https://o2.mail.ru/"),
        ApiBaseUri: new Uri("https://postmaster.mail.ru/"),
        RequestTimeout: TimeSpan.FromSeconds(15),
        MaxRateLimitDelay: TimeSpan.FromSeconds(10));

    private sealed class StubHttpClientFactory(IReadOnlyDictionary<string, HttpClient> clients) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => clients.TryGetValue(name, out var client)
            ? client
            : throw new InvalidOperationException($"Не зарегистрирован тестовый HTTP-клиент {name}.");
    }

    private sealed class SequenceTokenProvider(params string[] tokens) : IMailruPostmasterTokenProvider
    {
        private int index;
        public List<bool> ForceRefreshCalls { get; } = [];

        public Task<MailruPostmasterTokenResult> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            ForceRefreshCalls.Add(forceRefresh);
            var token = tokens[Math.Min(index, tokens.Length - 1)];
            index++;
            return Task.FromResult(MailruPostmasterTokenResult.Success(token, DateTimeOffset.UtcNow.AddHours(1)));
        }
    }

    private sealed class QueueHttpMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);

        public int CallCount { get; private set; }
        public List<string> RequestBodies { get; } = [];
        public List<string> BearerHeaders { get; } = [];
        public List<Uri> RequestUris { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUris.Add(request.RequestUri!);
            if (request.Headers.TryGetValues("Bearer", out var values))
            {
                BearerHeaders.Add(values.Single());
            }

            if (request.Content is not null)
            {
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return responses.Count > 0
                ? responses.Dequeue()
                : throw new InvalidOperationException("Для тестового HTTP-запроса не подготовлен ответ.");
        }
    }
}
