using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Webhook_Service.Models;
using Webhook_Service.Options;

namespace Webhook_Service.Services;

public interface IKafkaEventPublisher
{
    Task PublishAsync(
        NormalizedFacebookEvent facebookEvent,
        CancellationToken cancellationToken);
}

public sealed class KafkaEventPublisher : IKafkaEventPublisher, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly KafkaOptions _options;
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaEventPublisher> _logger;

    public KafkaEventPublisher(
        IOptions<KafkaOptions> options,
        ILogger<KafkaEventPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;

        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageSendMaxRetries = 5,
            RetryBackoffMs = 200,
            LingerMs = 0
        }).Build();
    }

    public async Task PublishAsync(
        NormalizedFacebookEvent facebookEvent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BootstrapServers) ||
            string.IsNullOrWhiteSpace(_options.RawEventsTopic))
        {
            throw new InvalidOperationException("Kafka chưa được cấu hình đầy đủ.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(
            Math.Max(1, _options.PublishTimeoutSeconds)));

        var result = await _producer.ProduceAsync(
            _options.RawEventsTopic,
            new Message<string, string>
            {
                Key = facebookEvent.EventId,
                Value = JsonSerializer.Serialize(facebookEvent, JsonOptions),
                Headers =
                [
                    new Header(
                        "event_type",
                        System.Text.Encoding.UTF8.GetBytes(facebookEvent.EventType))
                ]
            },
            timeout.Token);

        _logger.LogInformation(
            "Published event {EventId} to {Topic}[{Partition}] at offset {Offset}",
            facebookEvent.EventId,
            result.Topic,
            result.Partition.Value,
            result.Offset.Value);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
