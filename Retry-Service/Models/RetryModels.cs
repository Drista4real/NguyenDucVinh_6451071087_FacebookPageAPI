using System.Text.Json.Serialization;

namespace RetryService.Models;

public sealed record FacebookCommand
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

public sealed record SendFailedMessage
{
    [JsonPropertyName("command")]
    public FacebookCommand? Command { get; init; }

    [JsonPropertyName("retry_count")]
    public int RetryCount { get; init; }

    [JsonPropertyName("retryable")]
    public bool Retryable { get; init; }

    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;

    [JsonPropertyName("failed_at")]
    public DateTimeOffset FailedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record DeadLetterMessage
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
    public DateTimeOffset FailedAt { get; init; }

    [JsonPropertyName("dead_lettered_at")]
    public DateTimeOffset DeadLetteredAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}

public sealed class RetryPolicy
{
    public int MaxRetryAttempts { get; set; } = 5;
    public int InitialBackoffSeconds { get; set; } = 1;
    public double BackoffMultiplier { get; set; } = 2.0;
    public int MaxBackoffSeconds { get; set; } = 300;

    public TimeSpan CalculateBackoff(int currentRetryCount)
    {
        var seconds = InitialBackoffSeconds *
                      Math.Pow(BackoffMultiplier, Math.Max(0, currentRetryCount));
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoffSeconds));
    }

    public void Validate()
    {
        if (MaxRetryAttempts < 1)
        {
            throw new InvalidOperationException("RetryPolicy:MaxRetryAttempts must be >= 1.");
        }

        if (InitialBackoffSeconds < 0)
        {
            throw new InvalidOperationException("RetryPolicy:InitialBackoffSeconds must be >= 0.");
        }

        if (BackoffMultiplier < 1)
        {
            throw new InvalidOperationException("RetryPolicy:BackoffMultiplier must be >= 1.");
        }

        if (MaxBackoffSeconds < InitialBackoffSeconds)
        {
            throw new InvalidOperationException(
                "RetryPolicy:MaxBackoffSeconds must be >= InitialBackoffSeconds.");
        }
    }
}

public sealed record RetryDecision(
    bool ShouldRetry,
    TimeSpan Backoff,
    string Reason);
