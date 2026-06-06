using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoreService.Models;

public sealed class RawEvent
{
    [JsonPropertyName("event_id")]
    public string EventId { get; init; } = string.Empty;

    [JsonPropertyName("event_type")]
    public string EventType { get; init; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; init; } = "facebook";

    [JsonPropertyName("page_id")]
    public string? PageId { get; init; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("user_name")]
    public string? UserName { get; init; }

    [JsonPropertyName("target_id")]
    public string? TargetId { get; init; }

    [JsonPropertyName("post_id")]
    public string? PostId { get; init; }

    [JsonPropertyName("parent_id")]
    public string? ParentId { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("occurred_at")]
    public DateTimeOffset OccurredAt { get; init; }

    [JsonPropertyName("received_at")]
    public DateTimeOffset ReceivedAt { get; init; }

    [JsonPropertyName("raw_event")]
    public JsonElement RawPayload { get; init; }
}

public sealed class FacebookCommand
{
    [JsonPropertyName("command_id")]
    public string CommandId { get; init; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    [JsonPropertyName("target_id")]
    public string? TargetId { get; init; }

    [JsonPropertyName("page_id")]
    public string? PageId { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("retry_count")]
    public int RetryCount { get; init; }

    [JsonPropertyName("event_id")]
    public string? EventId { get; init; }
}
