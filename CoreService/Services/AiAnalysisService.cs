using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CoreService.Models;
using CoreService.Options;
using Microsoft.Extensions.Options;

namespace CoreService.Services;

public interface IAiAnalysisService
{
    Task<AiAnalysisResult> AnalyzeCommentAsync(
        string message,
        CancellationToken cancellationToken = default);
}

public sealed class FallbackAiAnalysisService : IAiAnalysisService
{
    private readonly OpenAiAnalysisService _openAi;
    private readonly RuleBasedAiAnalysisService _fallback;
    private readonly AiApiOptions _options;
    private readonly ILogger<FallbackAiAnalysisService> _logger;

    public FallbackAiAnalysisService(
        OpenAiAnalysisService openAi,
        RuleBasedAiAnalysisService fallback,
        IOptions<AiApiOptions> options,
        ILogger<FallbackAiAnalysisService> logger)
    {
        _openAi = openAi;
        _fallback = fallback;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiAnalysisResult> AnalyzeCommentAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(_options.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return await _fallback.AnalyzeCommentAsync(message, cancellationToken);
        }

        try
        {
            return await _openAi.AnalyzeCommentAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   or TaskCanceledException
                                   or InvalidOperationException)
        {
            _logger.LogWarning(
                ex,
                "OpenAI analysis failed; using rule-based fallback.");
            return await _fallback.AnalyzeCommentAsync(message, cancellationToken);
        }
    }
}

public sealed class RuleBasedAiAnalysisService : IAiAnalysisService
{
    private static readonly string[] LinkSpamKeywords =
    [
        "http://", "https://", "bit.ly", "tinyurl", "click", "link"
    ];

    private static readonly string[] AdvertisingSpamKeywords =
    [
        "khuyen mai", "sale", "discount", "mua ngay", "inbox de nhan gia"
    ];

    public Task<AiAnalysisResult> AnalyzeCommentAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        var text = (message ?? string.Empty).Trim();
        var lower = RemoveVietnameseDiacritics(text).ToLowerInvariant();

        if (ContainsAny(lower, LinkSpamKeywords))
        {
            return Task.FromResult(new AiAnalysisResult
            {
                Sentiment = "neutral",
                Intent = "spam",
                IsSpam = true,
                SpamType = "link",
                Confidence = 0.92,
                Source = "rule_based"
            });
        }

        if (ContainsAny(lower, AdvertisingSpamKeywords) ||
            (text.Length > 120 && text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 5))
        {
            return Task.FromResult(new AiAnalysisResult
            {
                Sentiment = "neutral",
                Intent = "spam",
                IsSpam = true,
                SpamType = "advertising",
                Confidence = 0.86,
                Source = "rule_based"
            });
        }

        if (lower.Contains("gia") || lower.Contains("bao nhieu") || lower.Contains("bao tien"))
        {
            return Task.FromResult(new AiAnalysisResult
            {
                Sentiment = "neutral",
                Intent = "ask_price",
                Confidence = 0.82,
                Source = "rule_based"
            });
        }

        if ((lower.Contains("chua nhan") || lower.Contains("khong nhan") ||
             lower.Contains("loi") || lower.Contains("hong")) &&
            (lower.Contains("hang") || lower.Contains("san pham") || lower.Contains("don")))
        {
            return Task.FromResult(new AiAnalysisResult
            {
                Sentiment = "negative",
                Intent = "complaint_support",
                Confidence = 0.84,
                Source = "rule_based"
            });
        }

        if (lower.Contains("te") || lower.Contains("xau") || lower.Contains("that vong"))
        {
            return Task.FromResult(new AiAnalysisResult
            {
                Sentiment = "negative",
                Intent = "complaint",
                Confidence = 0.82,
                Source = "rule_based"
            });
        }

        if (lower.Contains("tot") || lower.Contains("hay") ||
            lower.Contains("cam on") || lower.Contains("dep"))
        {
            return Task.FromResult(new AiAnalysisResult
            {
                Sentiment = "positive",
                Intent = "positive_feedback",
                Confidence = 0.78,
                Source = "rule_based"
            });
        }

        return Task.FromResult(new AiAnalysisResult
        {
            Sentiment = "neutral",
            Intent = "unknown",
            Confidence = 0.55,
            Source = "rule_based"
        });
    }

    private static bool ContainsAny(string value, IEnumerable<string> keywords) =>
        keywords.Any(value.Contains);

    private static string RemoveVietnameseDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder
            .ToString()
            .Replace('đ', 'd')
            .Replace('Đ', 'D')
            .Normalize(NormalizationForm.FormC);
    }
}

public sealed class OpenAiAnalysisService : IAiAnalysisService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;

    public OpenAiAnalysisService(HttpClient httpClient, IOptions<AiApiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value.OpenAi;
    }

    public async Task<AiAnalysisResult> AnalyzeCommentAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("OpenAI API key is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "responses");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(CreatePayload(message)),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var text = ExtractOutputText(document.RootElement);
        var result = JsonSerializer.Deserialize<AiAnalysisResult>(text, JsonOptions)
                     ?? throw new JsonException("OpenAI response did not contain analysis JSON.");

        return Normalize(result) with { Source = "openai" };
    }

    private object CreatePayload(string message) => new
    {
        model = _options.Model,
        input = new object[]
        {
            new
            {
                role = "developer",
                content = "Classify a Facebook Page comment. Return only JSON matching the schema."
            },
            new
            {
                role = "user",
                content = message
            }
        },
        max_output_tokens = _options.MaxOutputTokens,
        store = false,
        text = new
        {
            format = new
            {
                type = "json_schema",
                name = "comment_analysis",
                strict = true,
                schema = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[]
                    {
                        "sentiment", "intent", "confidence", "is_spam", "spam_type"
                    },
                    properties = new
                    {
                        sentiment = new
                        {
                            type = "string",
                            @enum = new[] { "positive", "neutral", "negative" }
                        },
                        intent = new
                        {
                            type = "string",
                            @enum = new[]
                            {
                                "ask_price", "complaint", "complaint_support",
                                "spam", "positive_feedback", "unknown"
                            }
                        },
                        confidence = new
                        {
                            type = "number",
                            minimum = 0,
                            maximum = 1
                        },
                        is_spam = new { type = "boolean" },
                        spam_type = new
                        {
                            type = "string",
                            @enum = new[]
                            {
                                "", "link", "repetitive", "bot", "advertising", "scam"
                            }
                        }
                    }
                }
            }
        }
    };

    private static string ExtractOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("OpenAI response has no output array.");
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                var type = contentItem.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString()
                    : null;

                if (string.Equals(type, "refusal", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("OpenAI refused the analysis request.");
                }

                if (string.Equals(type, "output_text", StringComparison.OrdinalIgnoreCase) &&
                    contentItem.TryGetProperty("text", out var textElement))
                {
                    return textElement.GetString()
                           ?? throw new JsonException("OpenAI output text is null.");
                }
            }
        }

        throw new JsonException("OpenAI response has no output_text content.");
    }

    private static AiAnalysisResult Normalize(AiAnalysisResult result)
    {
        var sentiment = IsOneOf(result.Sentiment, "positive", "neutral", "negative")
            ? result.Sentiment
            : "neutral";
        var intent = IsOneOf(
            result.Intent,
            "ask_price",
            "complaint",
            "complaint_support",
            "spam",
            "positive_feedback",
            "unknown")
            ? result.Intent
            : "unknown";
        var spamType = IsOneOf(
            result.SpamType,
            "",
            "link",
            "repetitive",
            "bot",
            "advertising",
            "scam")
            ? result.SpamType
            : string.Empty;

        return result with
        {
            Sentiment = sentiment,
            Intent = intent,
            Confidence = Math.Clamp(result.Confidence, 0, 1),
            SpamType = spamType
        };
    }

    private static bool IsOneOf(string value, params string[] allowed) =>
        allowed.Contains(value, StringComparer.OrdinalIgnoreCase);
}
