using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackendAPI.Models;

public sealed class CreatePostRequest
{
    [Required, StringLength(63206, MinimumLength = 1)]
    public string Message { get; set; } = string.Empty;

    [Url]
    public string? Link { get; set; }

    public bool Published { get; set; } = true;

    public DateTimeOffset? ScheduledPublishTime { get; set; }
}

public sealed class ReplyCommentRequest
{
    [Required, StringLength(8000, MinimumLength = 1)]
    public string Message { get; set; } = string.Empty;
}

public sealed class SetCommentVisibilityRequest
{
    public bool IsHidden { get; set; } = true;
}

public sealed class ErrorResponse
{
    public bool Success { get; init; }
    public string Error { get; init; } = string.Empty;
    public string? Code { get; init; }
    public string? TraceId { get; init; }
}

public sealed class SuccessResponse
{
    public bool Success { get; init; } = true;
    public string Message { get; init; } = string.Empty;
    public JsonElement? Data { get; init; }
}

public sealed class FacebookErrorEnvelope
{
    [JsonPropertyName("error")]
    public FacebookError? Error { get; init; }
}

public sealed class FacebookError
{
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("error_subcode")]
    public int? ErrorSubcode { get; init; }

    [JsonPropertyName("fbtrace_id")]
    public string? FbtraceId { get; init; }
}
