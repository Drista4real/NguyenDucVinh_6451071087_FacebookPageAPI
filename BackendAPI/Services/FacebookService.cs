using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BackendAPI.Infrastructure;
using BackendAPI.Models;
using BackendAPI.Options;
using Microsoft.Extensions.Options;

namespace BackendAPI.Services;

public interface IFacebookService
{
    Task<JsonElement> GetPageInfoAsync(string pageId, CancellationToken cancellationToken = default);
    Task<JsonElement> GetPostsAsync(string pageId, int limit = 25, string? after = null, CancellationToken cancellationToken = default);
    Task<JsonElement> CreatePostAsync(string pageId, CreatePostRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeletePostAsync(string postId, CancellationToken cancellationToken = default);
    Task<JsonElement> GetCommentsAsync(string postId, int limit = 25, string? after = null, CancellationToken cancellationToken = default);
    Task<JsonElement> GetLikesAsync(string postId, int limit = 25, string? after = null, CancellationToken cancellationToken = default);
    Task<JsonElement> GetInsightsAsync(string pageId, CancellationToken cancellationToken = default);
    Task<JsonElement> ReplyToCommentAsync(string commentId, string message, CancellationToken cancellationToken = default);
    Task<JsonElement> SetCommentHiddenAsync(string commentId, bool isHidden, CancellationToken cancellationToken = default);
}

public sealed class FacebookService : IFacebookService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly FacebookOptions _options;
    private readonly CircuitBreakerOptions _circuitOptions;
    private readonly FacebookCircuitBreaker _circuitBreaker;
    private readonly ILogger<FacebookService> _logger;

    public FacebookService(
        HttpClient httpClient,
        IOptions<FacebookOptions> options,
        IOptions<CircuitBreakerOptions> circuitOptions,
        FacebookCircuitBreaker circuitBreaker,
        ILogger<FacebookService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _circuitOptions = circuitOptions.Value;
        _circuitBreaker = circuitBreaker;
        _logger = logger;
    }

    public Task<JsonElement> GetPageInfoAsync(
        string pageId,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Get,
            BuildPath(pageId, new Dictionary<string, string?>
            {
                ["fields"] = "id,name,about,fan_count,followers_count,picture,link"
            }),
            retryReads: true,
            cancellationToken: cancellationToken);

    public Task<JsonElement> GetPostsAsync(
        string pageId,
        int limit = 25,
        string? after = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Get,
            BuildPath($"{pageId}/feed", PaginationQuery(
                limit,
                after,
                "id,message,created_time,permalink_url,full_picture,shares," +
                "comments.limit(0).summary(true),reactions.limit(0).summary(true)")),
            retryReads: true,
            cancellationToken: cancellationToken);

    public Task<JsonElement> CreatePostAsync(
        string pageId,
        CreatePostRequest request,
        CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["message"] = request.Message,
            ["published"] = request.Published ? "true" : "false"
        };

        if (!string.IsNullOrWhiteSpace(request.Link))
        {
            form["link"] = request.Link;
        }

        if (request.ScheduledPublishTime is not null)
        {
            form["scheduled_publish_time"] =
                request.ScheduledPublishTime.Value.ToUnixTimeSeconds().ToString();
            form["published"] = "false";
        }

        return SendAsync(
            HttpMethod.Post,
            BuildPath($"{pageId}/feed"),
            form,
            cancellationToken: cancellationToken);
    }

    public async Task<bool> DeletePostAsync(
        string postId,
        CancellationToken cancellationToken = default)
    {
        var result = await SendAsync(
            HttpMethod.Delete,
            BuildPath(postId),
            cancellationToken: cancellationToken);

        return result.TryGetProperty("success", out var success) &&
               success.ValueKind == JsonValueKind.True;
    }

    public Task<JsonElement> GetCommentsAsync(
        string postId,
        int limit = 25,
        string? after = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Get,
            BuildPath($"{postId}/comments", PaginationQuery(
                limit,
                after,
                "id,message,from,created_time,comment_count,like_count,is_hidden,can_hide")),
            retryReads: true,
            cancellationToken: cancellationToken);

    public Task<JsonElement> GetLikesAsync(
        string postId,
        int limit = 25,
        string? after = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Get,
            BuildPath($"{postId}/reactions", PaginationQuery(
                limit,
                after,
                "id,name,type")),
            retryReads: true,
            cancellationToken: cancellationToken);

    public Task<JsonElement> GetInsightsAsync(
        string pageId,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Get,
            BuildPath($"{pageId}/insights", new Dictionary<string, string?>
            {
                ["metric"] = "page_impressions,page_post_engagements,page_fans",
                ["period"] = "day"
            }),
            retryReads: true,
            cancellationToken: cancellationToken);

    public Task<JsonElement> ReplyToCommentAsync(
        string commentId,
        string message,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Post,
            BuildPath($"{commentId}/comments"),
            new Dictionary<string, string> { ["message"] = message },
            cancellationToken: cancellationToken);

    public Task<JsonElement> SetCommentHiddenAsync(
        string commentId,
        bool isHidden,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Post,
            BuildPath(commentId),
            new Dictionary<string, string>
            {
                ["is_hidden"] = isHidden ? "true" : "false"
            },
            cancellationToken: cancellationToken);

    private async Task<JsonElement> SendAsync(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string>? form = null,
        bool retryReads = false,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var maxAttempts = retryReads
            ? Math.Max(1, _circuitOptions.ReadRetryCount)
            : 1;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            _circuitBreaker.EnsureRequestAllowed();
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.PageAccessToken);
            if (form is not null)
            {
                request.Content = new FormUrlEncodedContent(form);
            }

            try
            {
                _logger.LogInformation(
                    "Calling Facebook Graph API {Method} {Path}, attempt {Attempt}",
                    method,
                    path.Split('?')[0],
                    attempt);

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _circuitBreaker.RecordSuccess();
                    _logger.LogInformation(
                        "Facebook Graph API returned {StatusCode} for {Method} {Path}",
                        (int)response.StatusCode,
                        method,
                        path.Split('?')[0]);
                    return ParseJson(body);
                }

                var exception = CreateFacebookException(response.StatusCode, body);
                if (exception.IsTransient)
                {
                    _circuitBreaker.RecordTransientFailure();
                }
                else
                {
                    // A 4xx response proves the downstream is reachable.
                    _circuitBreaker.RecordSuccess();
                }

                if (!exception.IsTransient || attempt == maxAttempts)
                {
                    throw exception;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _circuitBreaker.RecordTransientFailure();
                if (attempt == maxAttempts)
                {
                    throw new FacebookApiException(
                        "Facebook Graph API timeout.",
                        HttpStatusCode.GatewayTimeout,
                        null,
                        isTransient: true);
                }
            }
            catch (HttpRequestException ex)
            {
                _circuitBreaker.RecordTransientFailure();
                if (attempt == maxAttempts)
                {
                    throw new FacebookApiException(
                        $"Không thể kết nối Facebook Graph API: {ex.Message}",
                        HttpStatusCode.BadGateway,
                        null,
                        isTransient: true,
                        ex);
                }
            }
            catch (OperationCanceledException)
            {
                _circuitBreaker.RecordCancelledRequest();
                throw;
            }

            await Task.Delay(
                TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
                cancellationToken);
        }

        throw new InvalidOperationException("Facebook request loop ended unexpectedly.");
    }

    private string BuildPath(
        string resource,
        IReadOnlyDictionary<string, string?>? query = null)
    {
        var path = $"{_options.ApiVersion.Trim('/')}/{resource.TrimStart('/')}";
        if (query is null || query.Count == 0)
        {
            return path;
        }

        var queryString = string.Join(
            "&",
            query
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair =>
                    $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
        return $"{path}?{queryString}";
    }

    private static Dictionary<string, string?> PaginationQuery(
        int limit,
        string? after,
        string fields) =>
        new()
        {
            ["fields"] = fields,
            ["limit"] = Math.Clamp(limit, 1, 100).ToString(),
            ["after"] = after
        };

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.PageAccessToken))
        {
            throw new FacebookConfigurationException(
                "Thiếu Facebook:PageAccessToken. Hãy cấu hình bằng biến môi trường " +
                "Facebook__PageAccessToken.");
        }
    }

    private static JsonElement ParseJson(string body)
    {
        using var document = JsonDocument.Parse(
            string.IsNullOrWhiteSpace(body) ? "{}" : body);
        return document.RootElement.Clone();
    }

    private static FacebookApiException CreateFacebookException(
        HttpStatusCode statusCode,
        string body)
    {
        FacebookError? facebookError = null;
        try
        {
            facebookError = JsonSerializer
                .Deserialize<FacebookErrorEnvelope>(body, JsonOptions)?
                .Error;
        }
        catch (JsonException)
        {
            // Preserve the upstream body below when Facebook returns non-JSON.
        }

        var message = facebookError?.Message ??
                      (string.IsNullOrWhiteSpace(body)
                          ? $"Facebook returned HTTP {(int)statusCode}."
                          : body);
        var transient = statusCode == HttpStatusCode.RequestTimeout ||
                        statusCode == HttpStatusCode.TooManyRequests ||
                        (int)statusCode >= 500;
        return new FacebookApiException(
            message,
            statusCode,
            facebookError,
            transient);
    }
}

public sealed class FacebookApiException : Exception
{
    public FacebookApiException(
        string message,
        HttpStatusCode upstreamStatusCode,
        FacebookError? facebookError,
        bool isTransient,
        Exception? innerException = null)
        : base(message, innerException)
    {
        UpstreamStatusCode = upstreamStatusCode;
        FacebookError = facebookError;
        IsTransient = isTransient;
    }

    public HttpStatusCode UpstreamStatusCode { get; }
    public FacebookError? FacebookError { get; }
    public bool IsTransient { get; }
}

public sealed class FacebookConfigurationException : Exception
{
    public FacebookConfigurationException(string message) : base(message)
    {
    }
}
