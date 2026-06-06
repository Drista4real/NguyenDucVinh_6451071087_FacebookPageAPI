using Confluent.Kafka;
using System.Text.Json;
using RetryService.Models;

namespace RetryService.Services;

public interface IKafkaConsumerService
{
    Task StartConsumingAsync(CancellationToken cancellationToken);
}

public interface IKafkaProducerService
{
    Task PublishSendRetryAsync(SendRetryMessage message);
    Task PublishDeadLetterAsync(DeadLetterMessage message);
}

public class KafkaConsumerWorkerService : BackgroundService
{
    private readonly ILogger<KafkaConsumerWorkerService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IRetryLogicService _retryLogicService;
    private readonly IKafkaProducerService _producerService;

    public KafkaConsumerWorkerService(
        ILogger<KafkaConsumerWorkerService> logger,
        IConfiguration configuration,
        IRetryLogicService retryLogicService,
        IKafkaProducerService producerService)
    {
        _logger = logger;
        _configuration = configuration;
        _retryLogicService = retryLogicService;
        _producerService = producerService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Retry Service Kafka Consumer Worker starting...");

        var kafkaConfig = _configuration.GetSection("Kafka");
        string bootstrapServers = kafkaConfig.GetValue<string>("BootstrapServers") ?? "localhost:9092";
        string consumerGroupId = kafkaConfig.GetValue<string>("ConsumerGroupId") ?? "retry-service-group";
        string sendFailedTopic = kafkaConfig.GetSection("Topics").GetValue<string>("SendFailed") ?? "send_failed";

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = consumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
            StatisticsIntervalMs = 5000,
            SessionTimeoutMs = 30000
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig)
            .SetErrorHandler((_, e) => _logger.LogError("Kafka error: {Error}", e.Reason))
            .Build();

        consumer.Subscribe(sendFailedTopic);
        _logger.LogInformation("Subscribed to topic: {Topic}", sendFailedTopic);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cr = consumer.Consume(stoppingToken);
                if (cr == null || string.IsNullOrEmpty(cr.Message.Value))
                    continue;

                _logger.LogInformation(
                    "Received message from {Topic}@{Partition}[{Offset}]",
                    cr.Topic, cr.Partition, cr.Offset);

                await ProcessFailedMessageAsync(cr.Message.Value, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error consuming message from Kafka");
                await Task.Delay(5000, stoppingToken);
            }
        }

        consumer.Close();
        _logger.LogInformation("Retry Service Kafka Consumer Worker stopped");
    }

    private async Task ProcessFailedMessageAsync(string messageJson, CancellationToken cancellationToken)
    {
        try
        {
            var message = JsonSerializer.Deserialize<SendFailedMessage>(messageJson);
            if (message == null)
            {
                _logger.LogWarning("Failed to deserialize message: {MessageJson}", messageJson);
                return;
            }

            var policy = new RetryPolicy();
            var (shouldRetry, nextRetryTime, backoffSeconds) = 
                _retryLogicService.DetermineRetryAction(message, policy);

            if (shouldRetry)
            {
                var retryMessage = new SendRetryMessage
                {
                    CommandId = message.CommandId,
                    EventId = message.EventId,
                    Action = message.Action,
                    ReplyText = message.ReplyText,
                    Timestamp = message.Timestamp,
                    RetryCount = message.RetryCount + 1,
                    NextRetryTime = nextRetryTime,
                    LastError = message.LastError
                };

                _logger.LogInformation(
                    "Publishing retry message for command {CommandId} to send_retry topic",
                    message.CommandId);
                await _producerService.PublishSendRetryAsync(retryMessage);
            }
            else
            {
                var dlqMessage = new DeadLetterMessage
                {
                    CommandId = message.CommandId,
                    EventId = message.EventId,
                    Action = message.Action,
                    ReplyText = message.ReplyText,
                    OriginalTimestamp = message.Timestamp,
                    FailedAt = DateTime.UtcNow,
                    TotalRetries = message.RetryCount,
                    LastError = message.LastError,
                    Reason = $"Max retry attempts ({new RetryPolicy().MaxRetryAttempts}) exceeded"
                };

                _logger.LogError(
                    "Moving command {CommandId} to Dead Letter Queue after {TotalRetries} retries",
                    message.CommandId, message.RetryCount);
                await _producerService.PublishDeadLetterAsync(dlqMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing failed message");
        }
    }
}

public class KafkaProducerService : IKafkaProducerService
{
    private readonly ILogger<KafkaProducerService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IProducer<string, string> _producer;

    public KafkaProducerService(ILogger<KafkaProducerService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        var kafkaConfig = _configuration.GetSection("Kafka");
        string bootstrapServers = kafkaConfig.GetValue<string>("BootstrapServers") ?? "localhost:9092";

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.Leader,
            EnableIdempotence = true,
            RetryBackoffMs = 100,
            MessageTimeoutMs = 30000
        };

        _producer = new ProducerBuilder<string, string>(producerConfig)
            .SetErrorHandler((_, e) => _logger.LogError("Producer error: {Error}", e.Reason))
            .Build();
    }

    public async Task PublishSendRetryAsync(SendRetryMessage message)
    {
        try
        {
            var kafkaConfig = _configuration.GetSection("Kafka");
            string sendRetryTopic = kafkaConfig.GetSection("Topics").GetValue<string>("SendRetry") ?? "send_retry";
            string messageJson = JsonSerializer.Serialize(message);

            var result = await _producer.ProduceAsync(
                sendRetryTopic,
                new Message<string, string>
                {
                    Key = message.CommandId,
                    Value = messageJson
                });

            _logger.LogInformation(
                "Published send_retry message for command {CommandId} to topic {Topic} " +
                "at offset {Offset}",
                message.CommandId, sendRetryTopic, result.Offset);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing send_retry message for command {CommandId}", message.CommandId);
            throw;
        }
    }

    public async Task PublishDeadLetterAsync(DeadLetterMessage message)
    {
        try
        {
            var kafkaConfig = _configuration.GetSection("Kafka");
            string deadLetterTopic = kafkaConfig.GetSection("Topics").GetValue<string>("DeadLetter") ?? "dead_letter";
            string messageJson = JsonSerializer.Serialize(message);

            var result = await _producer.ProduceAsync(
                deadLetterTopic,
                new Message<string, string>
                {
                    Key = message.CommandId,
                    Value = messageJson
                });

            _logger.LogError(
                "Published dead letter message for command {CommandId} to topic {Topic} " +
                "at offset {Offset}. Reason: {Reason}",
                message.CommandId, deadLetterTopic, result.Offset, message.Reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing dead letter message for command {CommandId}", message.CommandId);
            throw;
        }
    }
}
