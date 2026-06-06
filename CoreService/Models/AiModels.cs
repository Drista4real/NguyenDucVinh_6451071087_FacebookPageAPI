using System.Text.Json.Serialization;

namespace CoreService.Models;

public sealed record AiAnalysisResult
{
    [JsonPropertyName("sentiment")]
    public string Sentiment { get; init; } = "neutral";

    [JsonPropertyName("intent")]
    public string Intent { get; init; } = "unknown";

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("is_spam")]
    public bool IsSpam { get; init; }

    [JsonPropertyName("spam_type")]
    public string SpamType { get; init; } = string.Empty;

    [JsonIgnore]
    public string Source { get; init; } = "rule_based";
}

public sealed record AutomationAction
{
    public string ActionType { get; init; } = "pending_review";
    public string ReplyMessage { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public double Confidence { get; init; }
}
