using System.Text.Json;
using System.Text.Json.Serialization;

namespace Webhook_Service.Models;

public sealed class NormalizedFacebookEvent
{
    [JsonPropertyName("event_id")]
    public required string EventId { get; init; }

    [JsonPropertyName("event_type")]
    public required string EventType { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }

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
    public required DateTimeOffset OccurredAt { get; init; }

    [JsonPropertyName("received_at")]
    public required DateTimeOffset ReceivedAt { get; init; }

    [JsonPropertyName("raw_event")]
    public JsonElement RawEvent { get; init; }
}

public sealed class WebhookAcceptedResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("published_events")]
    public int PublishedEvents { get; init; }
}

public sealed class WebhookErrorResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("error")]
    public required string Error { get; init; }

    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; init; }
}
