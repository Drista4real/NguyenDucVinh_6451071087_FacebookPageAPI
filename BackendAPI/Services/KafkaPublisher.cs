using System.Text.Json;
using BackendAPI.Options;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace BackendAPI.Services;

public interface IKafkaPublisher
{
    Task PublishAsync<T>(
        string topic,
        string key,
        T value,
        CancellationToken cancellationToken);
}

public sealed class KafkaPublisher : IKafkaPublisher, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly KafkaOptions _options;
    private readonly Lazy<IProducer<string, string>> _producer;

    public KafkaPublisher(IOptions<KafkaOptions> options)
    {
        _options = options.Value;
        _producer = new Lazy<IProducer<string, string>>(() =>
            new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = _options.BootstrapServers,
                Acks = Acks.All,
                EnableIdempotence = true,
                MessageSendMaxRetries = 5
            }).Build());
    }

    public async Task PublishAsync<T>(
        string topic,
        string key,
        T value,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("Kafka is disabled.");
        }

        await _producer.Value.ProduceAsync(
            topic,
            new Message<string, string>
            {
                Key = key,
                Value = JsonSerializer.Serialize(value, JsonOptions)
            },
            cancellationToken);
    }

    public void Dispose()
    {
        if (_producer.IsValueCreated)
        {
            _producer.Value.Flush(TimeSpan.FromSeconds(5));
            _producer.Value.Dispose();
        }
    }
}
