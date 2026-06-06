using System.Text.Json.Serialization;

namespace CoreService.Models;

/// <summary>
/// Event thô từ Webhook Service qua topic raw_events
/// </summary>
public class RawEvent
{
    [JsonPropertyName("event_id")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("comment_id")]
    public string CommentId { get; set; } = string.Empty;

    [JsonPropertyName("post_id")]
    public string PostId { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("user_name")]
    public string UserName { get; set; } = string.Empty;
}

/// <summary>
/// Command để gửi phản hồi tới Backend API qua topic reply_commands
/// </summary>
public class ReplyCommand
{
    [JsonPropertyName("command_id")]
    public string CommandId { get; set; } = string.Empty;

    [JsonPropertyName("event_id")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("comment_id")]
    public string CommentId { get; set; } = string.Empty;

    [JsonPropertyName("post_id")]
    public string PostId { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty; // "reply", "hide", "pending_review", "block_user"

    [JsonPropertyName("reply_text")]
    public string ReplyText { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty; // "spam", "negative_sentiment", "pending_review"

    [JsonPropertyName("retry_count")]
    public int RetryCount { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}
