using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BackendAPI.Models;

public sealed class FacebookCommand
{
    [Required]
    [JsonPropertyName("command_id")]
    public string CommandId { get; init; } = string.Empty;

    [Required]
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

public sealed class SendFailedMessage
{
    [JsonPropertyName("command")]
    public required FacebookCommand Command { get; init; }

    [JsonPropertyName("retry_count")]
    public int RetryCount { get; init; }

    [JsonPropertyName("retryable")]
    public bool Retryable { get; init; }

    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;

    [JsonPropertyName("failed_at")]
    public DateTimeOffset FailedAt { get; init; } = DateTimeOffset.UtcNow;
}
