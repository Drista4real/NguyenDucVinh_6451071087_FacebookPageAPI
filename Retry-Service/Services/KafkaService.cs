using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using RetryService.Models;

namespace RetryService.Services;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ConsumerGroupId { get; set; } = "retry-service-group";
    public KafkaTopicsOptions Topics { get; set; } = new();
}

public sealed class KafkaTopicsOptions
{
    public string SendFailed { get; set; } = "send_failed";
    public string SendRetry { get; set; } = "send_retry";
    public string DeadLetter { get; set; } = "dead_letter";
}

public interface IKafkaProducerService
{
    Task PublishRetryCommandAsync(
        FacebookCommand command,
        CancellationToken cancellationToken);
    Task PublishDeadLetterAsync(
        DeadLetterMessage message,
        CancellationToken cancellationToken);
}

public interface IBackoffDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class TaskBackoffDelay : IBackoffDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

public sealed class RetryMessageProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IRetryLogicService _retryLogic;
    private readonly IKafkaProducerService _producer;
    private readonly IBackoffDelay _delay;
    private readonly RetryPolicy _policy;
    private readonly ILogger<RetryMessageProcessor> _logger;

    public RetryMessageProcessor(
        IRetryLogicService retryLogic,
        IKafkaProducerService producer,
        IBackoffDelay delay,
        IOptions<RetryPolicy> policy,
        ILogger<RetryMessageProcessor> logger)
    {
        _retryLogic = retryLogic;
        _producer = producer;
        _delay = delay;
        _policy = policy.Value;
        _logger = logger;
    }

    public async Task<bool> ProcessAsync(
        string payload,
        CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Deserialize<SendFailedMessage>(
            payload.TrimStart('\uFEFF'),
            JsonOptions);
        if (message?.Command is null ||
            string.IsNullOrWhiteSpace(message.Command.CommandId))
        {
            _logger.LogError("Skipping invalid send_failed payload: {Payload}", payload);
            return false;
        }

        var decision = _retryLogic.DetermineRetryAction(message, _policy);
        if (!decision.ShouldRetry)
        {
            await _producer.PublishDeadLetterAsync(
                new DeadLetterMessage
                {
                    Command = message.Command,
                    RetryCount = message.RetryCount,
                    Retryable = message.Retryable,
                    Error = message.Error,
                    FailedAt = message.FailedAt,
                    DeadLetteredAt = DateTimeOffset.UtcNow,
                    Reason = decision.Reason
                },
                cancellationToken);
            return true;
        }

        _logger.LogInformation(
            "Retrying command {CommandId} after {BackoffSeconds}s",
            message.Command.CommandId,
            decision.Backoff.TotalSeconds);

        await _delay.DelayAsync(decision.Backoff, cancellationToken);
        await _producer.PublishRetryCommandAsync(
            message.Command with { RetryCount = message.RetryCount + 1 },
            cancellationToken);
        return true;
    }
}

public sealed class KafkaConsumerWorkerService : BackgroundService
{
    private readonly KafkaOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KafkaConsumerWorkerService> _logger;

    public KafkaConsumerWorkerService(
        IOptions<KafkaOptions> options,
        IServiceProvider serviceProvider,
        ILogger<KafkaConsumerWorkerService> logger)
    {
        _options = options.Value;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AllowAutoCreateTopics = false
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig)
            .SetErrorHandler((_, error) => _logger.LogError("Kafka error: {Reason}", error.Reason))
            .Build();

        consumer.Subscribe(_options.Topics.SendFailed);
        _logger.LogInformation("Retry Service subscribed to {Topic}", _options.Topics.SendFailed);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = null;
            try
            {
                result = consumer.Consume(stoppingToken);
                if (result is null || result.IsPartitionEOF)
                {
                    continue;
                }

                using var scope = _serviceProvider.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<RetryMessageProcessor>();
                await processor.ProcessAsync(result.Message.Value, stoppingToken);

                consumer.StoreOffset(result);
                consumer.Commit(result);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Skipping malformed send_failed payload");
                if (result is not null)
                {
                    consumer.Commit(result);
                }
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Kafka consume failed");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retry processing failed; seeking message for retry");
                if (result is not null)
                {
                    consumer.Seek(result.TopicPartitionOffset);
                }

                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        consumer.Close();
    }
}

public sealed class KafkaProducerService : IKafkaProducerService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly KafkaOptions _options;
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaProducerService> _logger;

    public KafkaProducerService(
        IOptions<KafkaOptions> options,
        ILogger<KafkaProducerService> logger)
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
            MessageTimeoutMs = 30000
        })
        .SetErrorHandler((_, error) => _logger.LogError("Kafka producer error: {Reason}", error.Reason))
        .Build();
    }

    public Task PublishRetryCommandAsync(
        FacebookCommand command,
        CancellationToken cancellationToken) =>
        ProduceAsync(_options.Topics.SendRetry, command.CommandId, command, cancellationToken);

    public Task PublishDeadLetterAsync(
        DeadLetterMessage message,
        CancellationToken cancellationToken) =>
        ProduceAsync(_options.Topics.DeadLetter, message.Command.CommandId, message, cancellationToken);

    private async Task ProduceAsync<T>(
        string topic,
        string key,
        T message,
        CancellationToken cancellationToken)
    {
        var result = await _producer.ProduceAsync(
            topic,
            new Message<string, string>
            {
                Key = key,
                Value = JsonSerializer.Serialize(message, JsonOptions)
            },
            cancellationToken);

        _logger.LogInformation(
            "Published message with key {Key} to {Topic}@{Partition}[{Offset}]",
            key,
            result.Topic,
            result.Partition,
            result.Offset);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
