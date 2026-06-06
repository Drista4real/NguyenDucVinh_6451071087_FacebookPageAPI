using System.Text;
using Webhook_Service.Services;

namespace Webhook_Service.Tests;

public sealed class FacebookWebhookNormalizerTests
{
    private readonly FacebookWebhookNormalizer _normalizer = new();
    private readonly DateTimeOffset _receivedAt =
        new(2026, 6, 6, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Normalize_ConvertsPageCommentChange()
    {
        const string payload = """
            {
              "object": "page",
              "entry": [{
                "id": "page-123",
                "time": 1780710000,
                "changes": [{
                  "field": "feed",
                  "value": {
                    "item": "comment",
                    "verb": "add",
                    "comment_id": "comment-456",
                    "post_id": "post-789",
                    "parent_id": "post-789",
                    "sender_id": "user-101",
                    "sender_name": "Nguyen Van A",
                    "message": "Shop oi gia bao nhieu?",
                    "created_time": 1780710001
                  }
                }]
              }]
            }
            """;

        var result = _normalizer.Normalize(
            Encoding.UTF8.GetBytes(payload),
            _receivedAt);

        var facebookEvent = Assert.Single(result);
        Assert.Equal("facebook:comment:comment-456:add", facebookEvent.EventId);
        Assert.Equal("comment", facebookEvent.EventType);
        Assert.Equal("add", facebookEvent.Action);
        Assert.Equal("page-123", facebookEvent.PageId);
        Assert.Equal("user-101", facebookEvent.UserId);
        Assert.Equal("comment-456", facebookEvent.TargetId);
        Assert.Equal("post-789", facebookEvent.PostId);
        Assert.Equal("Shop oi gia bao nhieu?", facebookEvent.Message);
        Assert.Equal(_receivedAt, facebookEvent.ReceivedAt);
    }

    [Fact]
    public void Normalize_ConvertsMessengerMessage()
    {
        const string payload = """
            {
              "object": "page",
              "entry": [{
                "id": "page-123",
                "time": 1780710000,
                "messaging": [{
                  "sender": { "id": "user-101" },
                  "recipient": { "id": "page-123" },
                  "timestamp": 1780710000123,
                  "message": {
                    "mid": "message-456",
                    "text": "Hello shop"
                  }
                }]
              }]
            }
            """;

        var result = _normalizer.Normalize(
            Encoding.UTF8.GetBytes(payload),
            _receivedAt);

        var facebookEvent = Assert.Single(result);
        Assert.Equal("facebook:message:message-456", facebookEvent.EventId);
        Assert.Equal("message", facebookEvent.EventType);
        Assert.Equal("received", facebookEvent.Action);
        Assert.Equal("user-101", facebookEvent.UserId);
        Assert.Equal("Hello shop", facebookEvent.Message);
    }

    [Fact]
    public void Normalize_IgnoresEchoMessages()
    {
        const string payload = """
            {
              "object": "page",
              "entry": [{
                "id": "page-123",
                "messaging": [{
                  "sender": { "id": "page-123" },
                  "message": {
                    "mid": "echo-1",
                    "text": "Own reply",
                    "is_echo": true
                  }
                }]
              }]
            }
            """;

        var result = _normalizer.Normalize(
            Encoding.UTF8.GetBytes(payload),
            _receivedAt);

        Assert.Empty(result);
    }

    [Fact]
    public void Normalize_IgnoresUnsupportedObject()
    {
        var result = _normalizer.Normalize(
            """{"object":"instagram","entry":[]}"""u8.ToArray(),
            _receivedAt);

        Assert.Empty(result);
    }
}
