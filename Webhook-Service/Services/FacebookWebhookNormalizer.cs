using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Webhook_Service.Models;

namespace Webhook_Service.Services;

public interface IFacebookWebhookNormalizer
{
    IReadOnlyList<NormalizedFacebookEvent> Normalize(
        ReadOnlyMemory<byte> payload,
        DateTimeOffset receivedAt);
}

public sealed class FacebookWebhookNormalizer : IFacebookWebhookNormalizer
{
    public IReadOnlyList<NormalizedFacebookEvent> Normalize(
        ReadOnlyMemory<byte> payload,
        DateTimeOffset receivedAt)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (!root.TryGetProperty("object", out var objectElement) ||
            !string.Equals(
                objectElement.GetString(),
                "page",
                StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        if (!root.TryGetProperty("entry", out var entries) ||
            entries.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var events = new List<NormalizedFacebookEvent>();
        foreach (var entry in entries.EnumerateArray())
        {
            var pageId = GetString(entry, "id");
            var entryTime = GetTimestamp(entry, "time", milliseconds: false) ?? receivedAt;

            NormalizeFeedChanges(entry, pageId, entryTime, receivedAt, events);
            NormalizeMessages(entry, pageId, entryTime, receivedAt, events);
        }

        return events;
    }

    private static void NormalizeFeedChanges(
        JsonElement entry,
        string? pageId,
        DateTimeOffset entryTime,
        DateTimeOffset receivedAt,
        ICollection<NormalizedFacebookEvent> events)
    {
        if (!entry.TryGetProperty("changes", out var changes) ||
            changes.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var change in changes.EnumerateArray())
        {
            if (!string.Equals(
                    GetString(change, "field"),
                    "feed",
                    StringComparison.OrdinalIgnoreCase) ||
                !change.TryGetProperty("value", out var value) ||
                !string.Equals(
                    GetString(value, "item"),
                    "comment",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var action = GetString(value, "verb") ?? "unknown";
            var commentId = GetString(value, "comment_id");
            var eventId = !string.IsNullOrWhiteSpace(commentId)
                ? $"facebook:comment:{commentId}:{action}"
                : CreateFallbackEventId("comment", change);

            events.Add(new NormalizedFacebookEvent
            {
                EventId = eventId,
                EventType = "comment",
                Action = action,
                PageId = pageId,
                UserId = GetString(value, "sender_id") ??
                         GetNestedString(value, "from", "id"),
                UserName = GetString(value, "sender_name") ??
                           GetNestedString(value, "from", "name"),
                TargetId = commentId,
                PostId = GetString(value, "post_id"),
                ParentId = GetString(value, "parent_id"),
                Message = GetString(value, "message"),
                OccurredAt =
                    GetTimestamp(value, "created_time", milliseconds: false) ??
                    entryTime,
                ReceivedAt = receivedAt,
                RawEvent = change.Clone()
            });
        }
    }

    private static void NormalizeMessages(
        JsonElement entry,
        string? pageId,
        DateTimeOffset entryTime,
        DateTimeOffset receivedAt,
        ICollection<NormalizedFacebookEvent> events)
    {
        if (!entry.TryGetProperty("messaging", out var messagingEvents) ||
            messagingEvents.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var messaging in messagingEvents.EnumerateArray())
        {
            if (!messaging.TryGetProperty("message", out var message) ||
                GetBoolean(message, "is_echo"))
            {
                continue;
            }

            var messageId = GetString(message, "mid");
            var eventId = !string.IsNullOrWhiteSpace(messageId)
                ? $"facebook:message:{messageId}"
                : CreateFallbackEventId("message", messaging);

            events.Add(new NormalizedFacebookEvent
            {
                EventId = eventId,
                EventType = "message",
                Action = "received",
                PageId = pageId ?? GetNestedString(messaging, "recipient", "id"),
                UserId = GetNestedString(messaging, "sender", "id"),
                TargetId = messageId,
                Message = GetString(message, "text"),
                OccurredAt =
                    GetTimestamp(messaging, "timestamp", milliseconds: true) ??
                    entryTime,
                ReceivedAt = receivedAt,
                RawEvent = messaging.Clone()
            });
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static string? GetNestedString(
        JsonElement element,
        string objectName,
        string propertyName) =>
        element.TryGetProperty(objectName, out var nested) &&
        nested.ValueKind == JsonValueKind.Object
            ? GetString(nested, propertyName)
            : null;

    private static bool GetBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? GetTimestamp(
        JsonElement element,
        string propertyName,
        bool milliseconds)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        if (!value.TryGetInt64(out var unixTime))
        {
            return null;
        }

        try
        {
            return milliseconds
                ? DateTimeOffset.FromUnixTimeMilliseconds(unixTime)
                : DateTimeOffset.FromUnixTimeSeconds(unixTime);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string CreateFallbackEventId(
        string eventType,
        JsonElement payload)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload.GetRawText()));
        return $"facebook:{eventType}:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
