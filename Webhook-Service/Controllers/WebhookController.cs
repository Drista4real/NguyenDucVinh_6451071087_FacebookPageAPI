using System.Text.Json;
using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Webhook_Service.Models;
using Webhook_Service.Options;
using Webhook_Service.Services;

namespace Webhook_Service.Controllers;

[ApiController]
[Route("webhook")]
[Produces("application/json")]
public sealed class WebhookController : ControllerBase
{
    private const string SignatureHeader = "X-Hub-Signature-256";

    private readonly FacebookWebhookOptions _facebookOptions;
    private readonly IFacebookSignatureVerifier _signatureVerifier;
    private readonly IFacebookWebhookNormalizer _normalizer;
    private readonly IKafkaEventPublisher _publisher;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IOptions<FacebookWebhookOptions> facebookOptions,
        IFacebookSignatureVerifier signatureVerifier,
        IFacebookWebhookNormalizer normalizer,
        IKafkaEventPublisher publisher,
        ILogger<WebhookController> logger)
    {
        _facebookOptions = facebookOptions.Value;
        _signatureVerifier = signatureVerifier;
        _normalizer = normalizer;
        _publisher = publisher;
        _logger = logger;
    }

    [HttpGet]
    [Produces("text/plain", "application/json")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(WebhookErrorResponse), StatusCodes.Status403Forbidden)]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        if (string.IsNullOrWhiteSpace(_facebookOptions.VerifyToken))
        {
            return Error(
                StatusCodes.Status503ServiceUnavailable,
                "Webhook verify token chưa được cấu hình.",
                "webhook_not_configured");
        }

        if (!string.Equals(mode, "subscribe", StringComparison.Ordinal) ||
            !string.Equals(
                verifyToken,
                _facebookOptions.VerifyToken,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(challenge))
        {
            _logger.LogWarning("Facebook webhook verification failed");
            return Error(
                StatusCodes.Status403Forbidden,
                "Webhook verification failed.",
                "verification_failed");
        }

        return new ContentResult
        {
            StatusCode = StatusCodes.Status200OK,
            ContentType = "text/plain",
            Content = challenge
        };
    }

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(WebhookAcceptedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(WebhookErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(WebhookErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(WebhookErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        if (Request.ContentLength > _facebookOptions.MaxPayloadBytes)
        {
            return Error(
                StatusCodes.Status413PayloadTooLarge,
                "Webhook payload vượt quá giới hạn cho phép.",
                "payload_too_large");
        }

        byte[] payload;
        try
        {
            payload = await ReadPayloadAsync(cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            return Error(
                StatusCodes.Status413PayloadTooLarge,
                ex.Message,
                "payload_too_large");
        }

        try
        {
            if (!_signatureVerifier.Verify(
                    payload,
                    Request.Headers[SignatureHeader].ToString()))
            {
                _logger.LogWarning(
                    "Rejected Facebook webhook with invalid signature");
                return Error(
                    StatusCodes.Status401Unauthorized,
                    "Facebook webhook signature không hợp lệ.",
                    "invalid_signature");
            }

            var events = _normalizer.Normalize(payload, DateTimeOffset.UtcNow);
            foreach (var facebookEvent in events)
            {
                await _publisher.PublishAsync(facebookEvent, cancellationToken);
            }

            _logger.LogInformation(
                "Accepted Facebook webhook and published {EventCount} event(s)",
                events.Count);
            return Ok(new WebhookAcceptedResponse
            {
                PublishedEvents = events.Count
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Rejected malformed Facebook webhook JSON");
            return Error(
                StatusCodes.Status400BadRequest,
                "Webhook payload không phải JSON hợp lệ.",
                "invalid_json");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Webhook service configuration error");
            return Error(
                StatusCodes.Status503ServiceUnavailable,
                ex.Message,
                "service_not_configured");
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "Failed to publish Facebook event to Kafka");
            return Error(
                StatusCodes.Status503ServiceUnavailable,
                "Không thể publish sự kiện vào Kafka. Facebook sẽ thử gửi lại.",
                "kafka_unavailable");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error(
                StatusCodes.Status503ServiceUnavailable,
                "Kafka publish timeout. Facebook sẽ thử gửi lại.",
                "kafka_timeout");
        }
    }

    private async Task<byte[]> ReadPayloadAsync(CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        var maxBytes = Math.Max(1, _facebookOptions.MaxPayloadBytes);
        var chunk = new byte[16 * 1024];
        long totalBytes = 0;

        while (true)
        {
            var read = await Request.Body.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalBytes += read;
            if (totalBytes > maxBytes)
            {
                throw new InvalidDataException(
                    "Webhook payload vượt quá giới hạn cho phép.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }

    private ObjectResult Error(int statusCode, string message, string code) =>
        StatusCode(statusCode, new WebhookErrorResponse
        {
            Error = message,
            Code = code,
            TraceId = HttpContext.TraceIdentifier
        });
}
