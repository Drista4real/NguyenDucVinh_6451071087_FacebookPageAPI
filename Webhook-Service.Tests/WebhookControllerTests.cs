using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Webhook_Service.Controllers;
using Webhook_Service.Models;
using Webhook_Service.Options;
using Webhook_Service.Services;

namespace Webhook_Service.Tests;

public sealed class WebhookControllerTests
{
    private const string VerifyToken = "verify-token";
    private const string AppSecret = "test-app-secret";

    [Fact]
    public void Verify_ReturnsChallengeForMatchingToken()
    {
        var controller = CreateController(new CapturingPublisher());

        var result = Assert.IsType<ContentResult>(
            controller.Verify("subscribe", VerifyToken, "challenge-123"));

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal("challenge-123", result.Content);
        Assert.Equal("text/plain", result.ContentType);
    }

    [Fact]
    public void Verify_ReturnsForbiddenForWrongToken()
    {
        var controller = CreateController(new CapturingPublisher());

        var result = Assert.IsType<ObjectResult>(
            controller.Verify("subscribe", "wrong-token", "challenge-123"));

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task Receive_PublishesNormalizedEventForValidSignature()
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
                    "sender_id": "user-101",
                    "message": "Hello"
                  }
                }]
              }]
            }
            """;
        var publisher = new CapturingPublisher();
        var controller = CreateController(publisher);
        SetRequest(controller, payload, CreateSignature(payload));

        var result = Assert.IsType<OkObjectResult>(
            await controller.Receive(CancellationToken.None));

        var response = Assert.IsType<WebhookAcceptedResponse>(result.Value);
        Assert.Equal(1, response.PublishedEvents);
        Assert.Single(publisher.Events);
        Assert.Equal(
            "facebook:comment:comment-456:add",
            publisher.Events[0].EventId);
    }

    [Fact]
    public async Task Receive_ReturnsUnauthorizedForInvalidSignature()
    {
        const string payload = """{"object":"page","entry":[]}""";
        var controller = CreateController(new CapturingPublisher());
        SetRequest(controller, payload, $"sha256={new string('0', 64)}");

        var result = Assert.IsType<ObjectResult>(
            await controller.Receive(CancellationToken.None));

        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Task Receive_ReturnsBadRequestForMalformedJson()
    {
        const string payload = """{"object":"page","entry":[""";
        var controller = CreateController(new CapturingPublisher());
        SetRequest(controller, payload, CreateSignature(payload));

        var result = Assert.IsType<ObjectResult>(
            await controller.Receive(CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Receive_ReturnsPayloadTooLargeBeforeReadingBody()
    {
        var controller = CreateController(new CapturingPublisher());
        controller.Request.ContentLength = 2 * 1024 * 1024;

        var result = Assert.IsType<ObjectResult>(
            await controller.Receive(CancellationToken.None));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, result.StatusCode);
    }

    private static WebhookController CreateController(
        IKafkaEventPublisher publisher)
    {
        var options = Microsoft.Extensions.Options.Options.Create(
            new FacebookWebhookOptions
            {
                VerifyToken = VerifyToken,
                AppSecret = AppSecret,
                MaxPayloadBytes = 1024 * 1024
            });

        return new WebhookController(
            options,
            new FacebookSignatureVerifier(options),
            new FacebookWebhookNormalizer(),
            publisher,
            NullLogger<WebhookController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static void SetRequest(
        ControllerBase controller,
        string payload,
        string signature)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        controller.Request.Body = new MemoryStream(bytes);
        controller.Request.ContentLength = bytes.Length;
        controller.Request.ContentType = "application/json";
        controller.Request.Headers["X-Hub-Signature-256"] = signature;
    }

    private static string CreateSignature(string payload)
    {
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(AppSecret),
            Encoding.UTF8.GetBytes(payload));
        return $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private sealed class CapturingPublisher : IKafkaEventPublisher
    {
        public List<NormalizedFacebookEvent> Events { get; } = [];

        public Task PublishAsync(
            NormalizedFacebookEvent facebookEvent,
            CancellationToken cancellationToken)
        {
            Events.Add(facebookEvent);
            return Task.CompletedTask;
        }
    }
}
