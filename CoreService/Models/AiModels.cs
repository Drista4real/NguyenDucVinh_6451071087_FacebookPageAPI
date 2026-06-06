using System.Text.Json.Serialization;

namespace CoreService.Models;

/// <summary>
/// Kết quả phân tích từ AI
/// </summary>
public class AiAnalysisResult
{
    [JsonPropertyName("sentiment")]
    public string Sentiment { get; set; } = string.Empty; // "positive", "neutral", "negative"

    [JsonPropertyName("intent")]
    public string Intent { get; set; } = string.Empty; // "ask_price", "complaint", "complaint_support", "spam", "positive_feedback"

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("is_spam")]
    public bool IsSpam { get; set; }

    [JsonPropertyName("spam_type")]
    public string SpamType { get; set; } = string.Empty; // "link", "repetitive", "bot", "advertising"
}

/// <summary>
/// Request tới AI API (OpenAI/Gemini/Claude)
/// </summary>
public class AiAnalysisRequest
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("context")]
    public Dictionary<string, string> Context { get; set; } = new();
}

/// <summary>
/// Tác vụ tự động hóa dựa trên kết quả phân tích
/// </summary>
public class AutomationAction
{
    [JsonPropertyName("action_type")]
    public string ActionType { get; set; } = string.Empty; // "auto_reply", "hide_comment", "pending_review", "block_user"

    [JsonPropertyName("reply_message")]
    public string ReplyMessage { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
}
