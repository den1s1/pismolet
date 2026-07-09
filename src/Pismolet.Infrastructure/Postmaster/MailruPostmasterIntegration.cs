using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Pismolet.Web.Infrastructure.Postmaster;

public sealed record MailruPostmasterOptions(
    bool Enabled,
    string Domain,
    string RefreshToken,
    Uri OAuthBaseUri,
    Uri ApiBaseUri,
    TimeSpan RequestTimeout,
    TimeSpan MaxRateLimitDelay)
{
    public const string OAuthHttpClientName = "MailruPostmaster.OAuth";
    public const string ApiHttpClientName = "MailruPostmaster.Api";

    public static MailruPostmasterOptions DisabledDefault => new(
        Enabled: false,
        Domain: "pismolet.ru",
        RefreshToken: string.Empty,
        OAuthBaseUri: new Uri("https://o2.mail.ru/", UriKind.Absolute),
        ApiBaseUri: new Uri("https://postmaster.mail.ru/", UriKind.Absolute),
        RequestTimeout: TimeSpan.FromSeconds(15),
        MaxRateLimitDelay: TimeSpan.FromSeconds(10));

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(Domain) && !string.IsNullOrWhiteSpace(RefreshToken);
}

public sealed record MailruPostmasterTokenResult(
    bool IsSuccess,
    string? AccessToken,
    DateTimeOffset? ExpiresAt,
    string? ErrorCode,
    string? ErrorMessage,
    int? HttpStatusCode)
{
    public static MailruPostmasterTokenResult Success(string accessToken, DateTimeOffset expiresAt) =>
        new(true, accessToken, expiresAt, null, null, null);

    public static MailruPostmasterTokenResult Failure(string errorCode, string errorMessage, int? httpStatusCode = null) =>
        new(false, null, null, errorCode, errorMessage, httpStatusCode);
}

public sealed record MailruPostmasterResult<T>(
    bool IsSuccess,
    T? Data,
    string? ErrorCode,
    string? ErrorMessage,
    int? HttpStatusCode,
    TimeSpan? RetryAfter)
{
    public static MailruPostmasterResult<T> Success(T data) =>
        new(true, data, null, null, null, null);

    public static MailruPostmasterResult<T> Failure(
        string errorCode,
        string errorMessage,
        int? httpStatusCode = null,
        TimeSpan? retryAfter = null) =>
        new(false, default, errorCode, errorMessage, httpStatusCode, retryAfter);
}

public sealed record MailruPostmasterRegisteredDomain(string Domain);

public sealed record MailruPostmasterTrouble(string Domain, int Code, string Message);

public sealed record MailruPostmasterStatistics(
    string Domain,
    long MessagesSent,
    long Delivered,
    long ProbablySpam,
    long Spam,
    long Complaints,
    long Read,
    long DeletedRead,
    long DeletedUnread,
    double SpamPercent,
    double ProbablySpamPercent);

public sealed record MailruPostmasterDailyStatistics(
    string Domain,
    DateOnly Date,
    long MessagesSent,
    long Delivered,
    long ProbablySpam,
    long Spam,
    long Complaints,
    long Read,
    long DeletedRead,
    long DeletedUnread,
    double SpamPercent,
    double ProbablySpamPercent,
    double Reputation,
    double Trend);

public interface IMailruPostmasterTokenProvider
{
    Task<MailruPostmasterTokenResult> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
}

public interface IMailruPostmasterClient
{
    Task<MailruPostmasterResult<IReadOnlyList<MailruPostmasterRegisteredDomain>>> GetRegisteredDomainsAsync(CancellationToken cancellationToken = default);

    Task<MailruPostmasterResult<IReadOnlyList<MailruPostmasterTrouble>>> GetTroublesAsync(CancellationToken cancellationToken = default);

    Task<MailruPostmasterResult<IReadOnlyList<MailruPostmasterStatistics>>> GetStatisticsAsync(
        DateOnly dateFrom,
        DateOnly? dateTo = null,
        string? domain = null,
        string? messageType = null,
        CancellationToken cancellationToken = default);

    Task<MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>> GetDetailedStatisticsAsync(
        DateOnly dateFrom,
        DateOnly? dateTo = null,
        string? domain = null,
        string? messageType = null,
        CancellationToken cancellationToken = default);
}

public sealed class MailruPostmasterTokenProvider(
    IHttpClientFactory httpClientFactory,
    MailruPostmasterOptions options,
    ILogger<MailruPostmasterTokenProvider> logger) : IMailruPostmasterTokenProvider
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private string? cachedAccessToken;
    private DateTimeOffset cachedExpiresAt;

    public async Task<MailruPostmasterTokenResult> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            return MailruPostmasterTokenResult.Failure("integration_disabled", "Интеграция Mail.ru Postmaster отключена.");
        }

        if (string.IsNullOrWhiteSpace(options.RefreshToken))
        {
            return MailruPostmasterTokenResult.Failure("refresh_token_missing", "Не задан refresh_token Mail.ru Postmaster.");
        }

        if (!forceRefresh && TryGetCachedToken(out var cached))
        {
            return cached;
        }

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && TryGetCachedToken(out cached))
            {
                return cached;
            }

            var client = httpClientFactory.CreateClient(MailruPostmasterOptions.OAuthHttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, "token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = "postmaster_api_client",
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = options.RefreshToken
                })
            };

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = ReadError(responseBody, "oauth_request_failed");
                logger.LogWarning(
                    "Mail.ru Postmaster token refresh failed. statusCode={StatusCode} errorCode={ErrorCode}",
                    (int)response.StatusCode,
                    error.Code);
                return MailruPostmasterTokenResult.Failure(error.Code, error.Message, (int)response.StatusCode);
            }

            try
            {
                using var document = JsonDocument.Parse(responseBody);
                var root = document.RootElement;
                var accessToken = GetString(root, "access_token");
                var expiresInSeconds = GetInt32(root, "expires_in", 3600);
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    return MailruPostmasterTokenResult.Failure("access_token_missing", "Mail.ru не вернул access_token.", (int)response.StatusCode);
                }

                cachedAccessToken = accessToken;
                cachedExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresInSeconds));
                logger.LogInformation(
                    "Mail.ru Postmaster access token refreshed. expiresInSeconds={ExpiresInSeconds}",
                    expiresInSeconds);
                return MailruPostmasterTokenResult.Success(cachedAccessToken, cachedExpiresAt);
            }
            catch (JsonException)
            {
                logger.LogWarning("Mail.ru Postmaster token refresh returned invalid JSON. statusCode={StatusCode}", (int)response.StatusCode);
                return MailruPostmasterTokenResult.Failure("invalid_oauth_json", "Mail.ru вернул некорректный ответ при обновлении токена.", (int)response.StatusCode);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return MailruPostmasterTokenResult.Failure("oauth_timeout", "Истекло время ожидания ответа OAuth Mail.ru.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Mail.ru Postmaster token refresh request failed.");
            return MailruPostmasterTokenResult.Failure("oauth_network_error", "Не удалось подключиться к OAuth Mail.ru.");
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private bool TryGetCachedToken(out MailruPostmasterTokenResult result)
    {
        if (!string.IsNullOrWhiteSpace(cachedAccessToken) && cachedExpiresAt - RefreshSkew > DateTimeOffset.UtcNow)
        {
            result = MailruPostmasterTokenResult.Success(cachedAccessToken, cachedExpiresAt);
            return true;
        }

        result = MailruPostmasterTokenResult.Failure("token_not_cached", "Access token отсутствует в кеше.");
        return false;
    }

    private static (string Code, string Message) ReadError(string body, string fallbackCode)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var code = GetString(root, "error") ?? GetString(root, "error_code") ?? fallbackCode;
            var message = GetString(root, "error_description") ?? GetString(root, "detail") ?? GetString(root, "error") ?? "Ошибка OAuth Mail.ru.";
            return (code, message);
        }
        catch (JsonException)
        {
            return (fallbackCode, "Ошибка OAuth Mail.ru.");
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private static int GetInt32(JsonElement element, string propertyName, int fallback)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return fallback;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }

        return property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : fallback;
    }
}

public sealed class MailruPostmasterClient(
    IHttpClientFactory httpClientFactory,
    IMailruPostmasterTokenProvider tokenProvider,
    MailruPostmasterOptions options,
    ILogger<MailruPostmasterClient> logger) : IMailruPostmasterClient
{
    private static readonly Regex RetryAfterRegex = new(@"Expected available in\s+(?<seconds>\d+)\s+seconds", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<MailruPostmasterResult<IReadOnlyList<MailruPostmasterRegisteredDomain>>> GetRegisteredDomainsAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendGetAsync("ext-api/reg-list/", cancellationToken);
        if (!response.IsSuccess)
        {
            return ConvertFailure<IReadOnlyList<MailruPostmasterRegisteredDomain>>(response);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Body!);
            if (!IsApiOk(document.RootElement, out var apiError))
            {
                return MailruPostmasterResult<IReadOnlyList<MailruPostmasterRegisteredDomain>>.Failure("api_error", apiError, response.StatusCode);
            }

            var domains = new List<MailruPostmasterRegisteredDomain>();
            if (document.RootElement.TryGetProperty("domains", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var domain = GetString(item, "domain");
                    if (!string.IsNullOrWhiteSpace(domain))
                    {
                        domains.Add(new MailruPostmasterRegisteredDomain(domain.Trim().ToLowerInvariant()));
                    }
                }
            }

            return MailruPostmasterResult<IReadOnlyList<MailruPostmasterRegisteredDomain>>.Success(domains);
        }
        catch (JsonException)
        {
            return InvalidJson<IReadOnlyList<MailruPostmasterRegisteredDomain>>(response.StatusCode);
        }
    }

    public async Task<MailruPostmasterResult<IReadOnlyList<MailruPostmasterTrouble>>> GetTroublesAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendGetAsync("ext-api/troubles-list/", cancellationToken);
        if (!response.IsSuccess)
        {
            return ConvertFailure<IReadOnlyList<MailruPostmasterTrouble>>(response);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Body!);
            if (!IsApiOk(document.RootElement, out var apiError))
            {
                return MailruPostmasterResult<IReadOnlyList<MailruPostmasterTrouble>>.Failure("api_error", apiError, response.StatusCode);
            }

            var troubles = new List<MailruPostmasterTrouble>();
            if (document.RootElement.TryGetProperty("data", out var domains) && domains.ValueKind == JsonValueKind.Array)
            {
                foreach (var domainItem in domains.EnumerateArray())
                {
                    var domain = GetString(domainItem, "domain")?.Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(domain) || !domainItem.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var error in errors.EnumerateArray())
                    {
                        troubles.Add(new MailruPostmasterTrouble(
                            domain,
                            GetInt32(error, "code"),
                            GetString(error, "msg") ?? "Неизвестная проблема Mail.ru Postmaster."));
                    }
                }
            }

            return MailruPostmasterResult<IReadOnlyList<MailruPostmasterTrouble>>.Success(troubles);
        }
        catch (JsonException)
        {
            return InvalidJson<IReadOnlyList<MailruPostmasterTrouble>>(response.StatusCode);
        }
    }

    public async Task<MailruPostmasterResult<IReadOnlyList<MailruPostmasterStatistics>>> GetStatisticsAsync(
        DateOnly dateFrom,
        DateOnly? dateTo = null,
        string? domain = null,
        string? messageType = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateStatisticsRequest(dateFrom, dateTo, domain, messageType);
        if (validation is not null)
        {
            return MailruPostmasterResult<IReadOnlyList<MailruPostmasterStatistics>>.Failure(validation.Value.Code, validation.Value.Message);
        }

        var response = await SendGetAsync(BuildStatisticsPath("ext-api/stat-list/", dateFrom, dateTo, domain, messageType), cancellationToken);
        if (!response.IsSuccess)
        {
            return ConvertFailure<IReadOnlyList<MailruPostmasterStatistics>>(response);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Body!);
            if (!IsApiOk(document.RootElement, out var apiError))
            {
                return MailruPostmasterResult<IReadOnlyList<MailruPostmasterStatistics>>.Failure("api_error", apiError, response.StatusCode);
            }

            var items = new List<MailruPostmasterStatistics>();
            if (document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    var parsed = ParseStatistics(item);
                    if (parsed is not null)
                    {
                        items.Add(parsed);
                    }
                }
            }

            return MailruPostmasterResult<IReadOnlyList<MailruPostmasterStatistics>>.Success(items);
        }
        catch (JsonException)
        {
            return InvalidJson<IReadOnlyList<MailruPostmasterStatistics>>(response.StatusCode);
        }
    }

    public async Task<MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>> GetDetailedStatisticsAsync(
        DateOnly dateFrom,
        DateOnly? dateTo = null,
        string? domain = null,
        string? messageType = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateStatisticsRequest(dateFrom, dateTo, domain, messageType);
        if (validation is not null)
        {
            return MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>.Failure(validation.Value.Code, validation.Value.Message);
        }

        var response = await SendGetAsync(BuildStatisticsPath("ext-api/stat-list-detailed/", dateFrom, dateTo, domain, messageType), cancellationToken);
        if (!response.IsSuccess)
        {
            return ConvertFailure<IReadOnlyList<MailruPostmasterDailyStatistics>>(response);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Body!);
            if (!IsApiOk(document.RootElement, out var apiError))
            {
                return MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>.Failure("api_error", apiError, response.StatusCode);
            }

            var items = new List<MailruPostmasterDailyStatistics>();
            if (document.RootElement.TryGetProperty("data", out var domains) && domains.ValueKind == JsonValueKind.Array)
            {
                foreach (var domainItem in domains.EnumerateArray())
                {
                    var domainName = GetString(domainItem, "domain")?.Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(domainName) || !domainItem.TryGetProperty("data", out var dailyItems) || dailyItems.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var dailyItem in dailyItems.EnumerateArray())
                    {
                        var dateText = GetString(dailyItem, "date");
                        if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        {
                            continue;
                        }

                        items.Add(new MailruPostmasterDailyStatistics(
                            domainName,
                            date,
                            GetInt64(dailyItem, "messages_sent"),
                            GetInt64(dailyItem, "delivered"),
                            GetInt64(dailyItem, "probably_spam"),
                            GetInt64(dailyItem, "spam"),
                            GetInt64(dailyItem, "complaints"),
                            GetInt64(dailyItem, "read"),
                            GetInt64(dailyItem, "deleted_read"),
                            GetInt64(dailyItem, "deleted_unread"),
                            GetDouble(dailyItem, "spam_percent"),
                            GetDouble(dailyItem, "probably_spam_percent"),
                            GetDouble(dailyItem, "reputation"),
                            GetDouble(dailyItem, "trend")));
                    }
                }
            }

            return MailruPostmasterResult<IReadOnlyList<MailruPostmasterDailyStatistics>>.Success(items);
        }
        catch (JsonException)
        {
            return InvalidJson<IReadOnlyList<MailruPostmasterDailyStatistics>>(response.StatusCode);
        }
    }

    private async Task<RawResponse> SendGetAsync(string relativePath, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return RawResponse.Failure("integration_disabled", "Интеграция Mail.ru Postmaster отключена.");
        }

        if (!options.IsConfigured)
        {
            return RawResponse.Failure("integration_not_configured", "Интеграция Mail.ru Postmaster включена, но не настроена.");
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = await tokenProvider.GetAccessTokenAsync(forceRefresh: attempt > 0, cancellationToken);
            if (!token.IsSuccess || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                return RawResponse.Failure(
                    token.ErrorCode ?? "token_error",
                    token.ErrorMessage ?? "Не удалось получить access_token Mail.ru.",
                    token.HttpStatusCode);
            }

            try
            {
                var client = httpClientFactory.CreateClient(MailruPostmasterOptions.ApiHttpClientName);
                using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
                request.Headers.TryAddWithoutValidation("Bearer", token.AccessToken);
                var startedAt = DateTimeOffset.UtcNow;
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var durationMs = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

                logger.LogInformation(
                    "Mail.ru Postmaster API request completed. path={Path} statusCode={StatusCode} durationMs={DurationMs}",
                    relativePath.Split('?', 2)[0],
                    (int)response.StatusCode,
                    Math.Round(durationMs));

                if (response.StatusCode == HttpStatusCode.Forbidden && attempt == 0)
                {
                    continue;
                }

                if ((int)response.StatusCode == 429)
                {
                    var retryAfter = ParseRetryAfter(response, body);
                    logger.LogWarning(
                        "Mail.ru Postmaster API rate limited. path={Path} retryAfterSeconds={RetryAfterSeconds}",
                        relativePath.Split('?', 2)[0],
                        retryAfter?.TotalSeconds);
                    if (attempt == 0 && retryAfter is { } delay && delay > TimeSpan.Zero && delay <= options.MaxRateLimitDelay)
                    {
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    return RawResponse.Failure("rate_limited", "Mail.ru Postmaster временно ограничил частоту запросов.", (int)response.StatusCode, retryAfter);
                }

                if ((int)response.StatusCode >= 500 && attempt == 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var error = ReadApiError(body, $"http_{(int)response.StatusCode}");
                    return RawResponse.Failure(error.Code, error.Message, (int)response.StatusCode);
                }

                return RawResponse.Success(body, (int)response.StatusCode);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return RawResponse.Failure("api_timeout", "Истекло время ожидания ответа Mail.ru Postmaster API.");
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Mail.ru Postmaster API network request failed. path={Path}", relativePath.Split('?', 2)[0]);
                return RawResponse.Failure("api_network_error", "Не удалось подключиться к Mail.ru Postmaster API.");
            }
        }

        return RawResponse.Failure("authorization_failed", "Mail.ru Postmaster отклонил обновлённый access_token.", 403);
    }

    private static string BuildStatisticsPath(string endpoint, DateOnly dateFrom, DateOnly? dateTo, string? domain, string? messageType)
    {
        var query = new List<string>
        {
            $"date_from={Uri.EscapeDataString(dateFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}"
        };

        if (dateTo is not null)
        {
            query.Add($"date_to={Uri.EscapeDataString(dateTo.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}");
        }

        if (!string.IsNullOrWhiteSpace(domain))
        {
            query.Add($"domain={Uri.EscapeDataString(domain.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(messageType))
        {
            query.Add($"msgtype={Uri.EscapeDataString(messageType.Trim())}");
        }

        return $"{endpoint}?{string.Join("&", query)}";
    }

    private static (string Code, string Message)? ValidateStatisticsRequest(DateOnly dateFrom, DateOnly? dateTo, string? domain, string? messageType)
    {
        if (dateTo is not null && dateFrom > dateTo.Value)
        {
            return ("invalid_date_range", "Дата начала периода не может быть позже даты окончания.");
        }

        if (!string.IsNullOrWhiteSpace(messageType) && string.IsNullOrWhiteSpace(domain))
        {
            return ("domain_required_for_msgtype", "Для запроса по msgtype необходимо указать домен.");
        }

        return null;
    }

    private static MailruPostmasterStatistics? ParseStatistics(JsonElement item)
    {
        var domain = GetString(item, "domain")?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(domain)
            ? null
            : new MailruPostmasterStatistics(
                domain,
                GetInt64(item, "messages_sent"),
                GetInt64(item, "delivered"),
                GetInt64(item, "probably_spam"),
                GetInt64(item, "spam"),
                GetInt64(item, "complaints"),
                GetInt64(item, "read"),
                GetInt64(item, "deleted_read"),
                GetInt64(item, "deleted_unread"),
                GetDouble(item, "spam_percent"),
                GetDouble(item, "probably_spam_percent"));
    }

    private static bool IsApiOk(JsonElement root, out string error)
    {
        if (!root.TryGetProperty("ok", out var ok))
        {
            error = ReadApiError(root.GetRawText(), "api_response_without_ok").Message;
            return false;
        }

        var success = ok.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => bool.TryParse(ok.GetString(), out var parsed) && parsed,
            _ => false
        };
        error = success ? string.Empty : ReadApiError(root.GetRawText(), "api_error").Message;
        return success;
    }

    private static (string Code, string Message) ReadApiError(string body, string fallbackCode)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var code = GetString(root, "error_code") ?? GetString(root, "error") ?? fallbackCode;
            var message = GetString(root, "error_description") ?? GetString(root, "detail") ?? GetString(root, "error") ?? "Ошибка Mail.ru Postmaster API.";
            return (code, message);
        }
        catch (JsonException)
        {
            return (fallbackCode, "Ошибка Mail.ru Postmaster API.");
        }
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response, string body)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        var match = RetryAfterRegex.Match(body);
        return match.Success && int.TryParse(match.Groups["seconds"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(Math.Max(0, seconds))
            : null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private static int GetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }

        return property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : 0;
    }

    private static long GetInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number))
        {
            return number;
        }

        return property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : 0;
    }

    private static double GetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number))
        {
            return number;
        }

        return property.ValueKind == JsonValueKind.String && double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number
            : 0;
    }

    private static MailruPostmasterResult<T> InvalidJson<T>(int? statusCode) =>
        MailruPostmasterResult<T>.Failure("invalid_api_json", "Mail.ru Postmaster вернул некорректный JSON.", statusCode);

    private static MailruPostmasterResult<T> ConvertFailure<T>(RawResponse response) =>
        MailruPostmasterResult<T>.Failure(
            response.ErrorCode ?? "api_error",
            response.ErrorMessage ?? "Ошибка Mail.ru Postmaster API.",
            response.StatusCode,
            response.RetryAfter);

    private sealed record RawResponse(
        bool IsSuccess,
        string? Body,
        int? StatusCode,
        string? ErrorCode,
        string? ErrorMessage,
        TimeSpan? RetryAfter)
    {
        public static RawResponse Success(string body, int statusCode) =>
            new(true, body, statusCode, null, null, null);

        public static RawResponse Failure(string errorCode, string errorMessage, int? statusCode = null, TimeSpan? retryAfter = null) =>
            new(false, null, statusCode, errorCode, errorMessage, retryAfter);
    }
}

public static class MailruPostmasterServiceCollectionExtensions
{
    public static IServiceCollection AddMailruPostmasterIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var options = ReadOptions(configuration);
        services.AddSingleton(options);
        services.AddHttpClient(MailruPostmasterOptions.OAuthHttpClientName, client =>
        {
            client.BaseAddress = options.OAuthBaseUri;
            client.Timeout = options.RequestTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Pismolet-MailruPostmaster/1.0");
        });
        services.AddHttpClient(MailruPostmasterOptions.ApiHttpClientName, client =>
        {
            client.BaseAddress = options.ApiBaseUri;
            client.Timeout = options.RequestTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Pismolet-MailruPostmaster/1.0");
        });
        services.AddSingleton<IMailruPostmasterTokenProvider, MailruPostmasterTokenProvider>();
        services.AddSingleton<IMailruPostmasterClient, MailruPostmasterClient>();
        return services;
    }

    internal static MailruPostmasterOptions ReadOptions(IConfiguration configuration)
    {
        var fallback = MailruPostmasterOptions.DisabledDefault;
        return new MailruPostmasterOptions(
            Enabled: ReadBool(configuration, "MailruPostmaster:Enabled", fallback.Enabled),
            Domain: ReadString(configuration, "MailruPostmaster:Domain", fallback.Domain).Trim().ToLowerInvariant(),
            RefreshToken: ReadString(configuration, "MailruPostmaster:RefreshToken", string.Empty),
            OAuthBaseUri: ReadHttpsBaseUri(configuration, "MailruPostmaster:OAuthBaseUrl", fallback.OAuthBaseUri),
            ApiBaseUri: ReadHttpsBaseUri(configuration, "MailruPostmaster:ApiBaseUrl", fallback.ApiBaseUri),
            RequestTimeout: TimeSpan.FromSeconds(ReadInt(configuration, "MailruPostmaster:RequestTimeoutSeconds", (int)fallback.RequestTimeout.TotalSeconds, 1, 120)),
            MaxRateLimitDelay: TimeSpan.FromSeconds(ReadInt(configuration, "MailruPostmaster:MaxRateLimitDelaySeconds", (int)fallback.MaxRateLimitDelay.TotalSeconds, 0, 60)));
    }

    private static string ReadString(IConfiguration configuration, string key, string fallback) =>
        configuration[key]
        ?? configuration[key.Replace(":", "__", StringComparison.Ordinal)]
        ?? Environment.GetEnvironmentVariable(key.Replace(":", "__", StringComparison.Ordinal))
        ?? fallback;

    private static int ReadInt(IConfiguration configuration, string key, int fallback, int min, int max)
    {
        var value = ReadString(configuration, key, string.Empty);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;
    }

    private static bool ReadBool(IConfiguration configuration, string key, bool fallback)
    {
        var value = ReadString(configuration, key, string.Empty);
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static Uri ReadHttpsBaseUri(IConfiguration configuration, string key, Uri fallback)
    {
        var value = ReadString(configuration, key, fallback.ToString()).Trim();
        if (!Uri.TryCreate(value.EndsWith('/') ? value : value + "/", UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return fallback;
        }

        return uri;
    }
}
