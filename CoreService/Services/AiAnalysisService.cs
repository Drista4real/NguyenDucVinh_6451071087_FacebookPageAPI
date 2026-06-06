using CoreService.Models;
using System.Text.Json;

namespace CoreService.Services;

public interface IAiAnalysisService
{
    Task<AiAnalysisResult> AnalyzeCommentAsync(string message);
}

/// <summary>
/// Dummy AI Service - cho phép thay thế bằng OpenAI/Gemini API
/// </summary>
public class DummyAiAnalysisService : IAiAnalysisService
{
    private readonly ILogger<DummyAiAnalysisService> _logger;

    public DummyAiAnalysisService(ILogger<DummyAiAnalysisService> logger)
    {
        _logger = logger;
    }

    public Task<AiAnalysisResult> AnalyzeCommentAsync(string message)
    {
        _logger.LogInformation("Analyzing comment: {Message}", message.Substring(0, Math.Min(50, message.Length)));

        var result = new AiAnalysisResult
        {
            Confidence = 0.85,
            IsSpam = false,
            SpamType = string.Empty
        };

        // Simple pattern matching for demo
        var lowerMessage = message.ToLower();

        // Check for spam indicators
        if (ContainsSpamIndicators(lowerMessage))
        {
            result.IsSpam = true;
            result.SpamType = DetermineSpamType(lowerMessage);
            result.Sentiment = "neutral";
            result.Intent = "spam";
            result.Confidence = 0.9;
        }
        else if (lowerMessage.Contains("giá") || lowerMessage.Contains("bao nhiêu") || lowerMessage.Contains("giá bao"))
        {
            result.Intent = "ask_price";
            result.Sentiment = "neutral";
        }
        else if (lowerMessage.Contains("không") && (lowerMessage.Contains("lỗi") || lowerMessage.Contains("hỏng")))
        {
            result.Intent = "complaint_support";
            result.Sentiment = "negative";
        }
        else if (lowerMessage.Contains("quá tệ") || lowerMessage.Contains("tệ") || lowerMessage.Contains("xấu"))
        {
            result.Intent = "complaint";
            result.Sentiment = "negative";
            result.Confidence = 0.88;
        }
        else if (lowerMessage.Contains("tốt") || lowerMessage.Contains("hay") || lowerMessage.Contains("cảm ơn"))
        {
            result.Intent = "positive_feedback";
            result.Sentiment = "positive";
        }
        else
        {
            result.Intent = "positive_feedback";
            result.Sentiment = "neutral";
        }

        return Task.FromResult(result);
    }

    private bool ContainsSpamIndicators(string message)
    {
        var spamKeywords = new[] { "http", "link", "click", "bit.ly", "tinyurl", "khuyến mại", "mua ngay", "sale", "discount" };
        return spamKeywords.Any(keyword => message.Contains(keyword));
    }

    private string DetermineSpamType(string message)
    {
        if (message.Contains("http") || message.Contains("link"))
            return "link";
        if (message.Length > 100 && message.Split(' ').Length < 5)
            return "bot";
        if (message.Contains("khuyến mại") || message.Contains("sale"))
            return "advertising";
        return "repetitive";
    }
}

/// <summary>
/// OpenAI Integration Service
/// </summary>
public class OpenAiAnalysisService : IAiAnalysisService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAiAnalysisService> _logger;

    public OpenAiAnalysisService(HttpClient httpClient, IConfiguration configuration, ILogger<OpenAiAnalysisService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AiAnalysisResult> AnalyzeCommentAsync(string message)
    {
        var apiKey = _configuration["AiApi:OpenAi:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("OpenAI API key not configured, falling back to dummy analysis");
            return new DummyAiAnalysisService(_logger).AnalyzeCommentAsync(message).Result;
        }

        try
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var prompt = $@"Analyze this Facebook comment and return JSON response:
Comment: {message}

Respond with JSON (valid JSON only, no markdown):
{{
  ""sentiment"": ""positive|neutral|negative"",
  ""intent"": ""ask_price|complaint|complaint_support|spam|positive_feedback"",
  ""is_spam"": true|false,
  ""spam_type"": ""link|repetitive|bot|advertising|"",
  ""confidence"": 0.0-1.0
}}";

            var payload = new
            {
                model = "gpt-3.5-turbo",
                messages = new[] { new { role = "user", content = prompt } },
                temperature = 0.3,
                max_tokens = 200
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("OpenAI API error: {StatusCode}", response.StatusCode);
                throw new HttpRequestException($"OpenAI API returned {response.StatusCode}");
            }

            var responseText = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(responseText);
            var messageContent = jsonDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            // Parse JSON response
            var analysisJson = JsonDocument.Parse(messageContent ?? "{}");
            var result = new AiAnalysisResult
            {
                Sentiment = analysisJson.RootElement.GetProperty("sentiment").GetString() ?? "neutral",
                Intent = analysisJson.RootElement.GetProperty("intent").GetString() ?? "positive_feedback",
                IsSpam = analysisJson.RootElement.GetProperty("is_spam").GetBoolean(),
                SpamType = analysisJson.RootElement.TryGetProperty("spam_type", out var spamType) ? spamType.GetString() ?? string.Empty : string.Empty,
                Confidence = analysisJson.RootElement.GetProperty("confidence").GetDouble()
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling OpenAI API, falling back to dummy analysis");
            return new DummyAiAnalysisService(_logger).AnalyzeCommentAsync(message).Result;
        }
    }
}
