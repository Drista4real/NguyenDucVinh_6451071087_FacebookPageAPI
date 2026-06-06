using System.Text.Json.Serialization;

namespace RetryService.Models;

public class SendFailedMessage
{
    [JsonPropertyName("commandId")]
    public string CommandId { get; set; } = string.Empty;

    [JsonPropertyName("eventId")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("replyText")]
    public string ReplyText { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; } = 0;

    [JsonPropertyName("lastError")]
    public string LastError { get; set; } = string.Empty;
}

public class SendRetryMessage
{
    [JsonPropertyName("commandId")]
    public string CommandId { get; set; } = string.Empty;

    [JsonPropertyName("eventId")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("replyText")]
    public string ReplyText { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; } = 0;

    [JsonPropertyName("nextRetryTime")]
    public DateTime NextRetryTime { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("lastError")]
    public string LastError { get; set; } = string.Empty;
}

public class DeadLetterMessage
{
    [JsonPropertyName("commandId")]
    public string CommandId { get; set; } = string.Empty;

    [JsonPropertyName("eventId")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("replyText")]
    public string ReplyText { get; set; } = string.Empty;

    [JsonPropertyName("originalTimestamp")]
    public DateTime OriginalTimestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("failedAt")]
    public DateTime FailedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("totalRetries")]
    public int TotalRetries { get; set; } = 0;

    [JsonPropertyName("lastError")]
    public string LastError { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

public class RetryPolicy
{
    public int MaxRetryAttempts { get; set; } = 5;
    public int InitialBackoffSeconds { get; set; } = 1;
    public double BackoffMultiplier { get; set; } = 2.0;
    public int MaxBackoffSeconds { get; set; } = 300; // 5 minutes max

    public int CalculateBackoffSeconds(int retryCount)
    {
        if (retryCount == 0)
            return 0;

        var backoffSeconds = (int)(InitialBackoffSeconds * Math.Pow(BackoffMultiplier, retryCount - 1));
        return Math.Min(backoffSeconds, MaxBackoffSeconds);
    }

    public bool ShouldRetry(int currentRetryCount)
    {
        return currentRetryCount < MaxRetryAttempts;
    }
}
