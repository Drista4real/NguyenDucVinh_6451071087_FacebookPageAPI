using Confluent.Kafka;
using CoreService.Models;
using System.Text.Json;

namespace CoreService.Services;

public interface IKafkaEventProcessor
{
    Task ProcessRawEventAsync(RawEvent rawEvent);
}

public interface IKafkaProducerService
{
    Task PublishReplyCommandAsync(ReplyCommand command);
}

/// <summary>
/// Kafka Consumer Worker Service
/// Consume từ topic raw_events và xử lý events
/// </summary>
public class KafkaConsumerWorkerService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KafkaConsumerWorkerService> _logger;
    private IConsumer<string, string>? _consumer;

    public KafkaConsumerWorkerService(
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<KafkaConsumerWorkerService> logger)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var kafkaConfig = _configuration.GetSection("Kafka");
        var bootstrapServers = kafkaConfig["BootstrapServers"] ?? "localhost:9092";
        var groupId = kafkaConfig["ConsumerGroupId"] ?? "core-service-group";
        var rawEventsTopic = kafkaConfig["Topics:RawEvents"] ?? "raw_events";

        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
            StatisticsIntervalMs = 5000,
            SessionTimeoutMs = 30000
        };

        _consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => _logger.LogError("Kafka error: {Reason}", e.Reason))
            .SetStatisticsHandler((_, stats) => _logger.LogDebug("Kafka statistics: {Stats}", stats))
            .Build();

        _consumer.Subscribe(rawEventsTopic);
        _logger.LogInformation("Kafka Consumer subscribed to topic: {Topic}", rawEventsTopic);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = _consumer.Consume(stoppingToken);
                    if (consumeResult == null || consumeResult.IsPartitionEOF)
                        continue;

                    _logger.LogInformation(
                        "Received message - Topic: {Topic}, Partition: {Partition}, Offset: {Offset}",
                        consumeResult.Topic, consumeResult.Partition, consumeResult.Offset);

                    try
                    {
                        var rawEvent = JsonSerializer.Deserialize<RawEvent>(consumeResult.Message.Value);
                        if (rawEvent != null)
                        {
                            using (var scope = _serviceProvider.CreateScope())
                            {
                                var processor = scope.ServiceProvider.GetRequiredService<IKafkaEventProcessor>();
                                await processor.ProcessRawEventAsync(rawEvent);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing message");
                    }
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Consume error: {Error}", ex.Error.Reason);
                }
            }
        }
        finally
        {
            _consumer?.Close();
            _consumer?.Dispose();
        }
    }
}

/// <summary>
/// Event Processing - Orchestrates AI analysis, automation logic, and publishing
/// </summary>
public class KafkaEventProcessor : IKafkaEventProcessor
{
    private readonly IAiAnalysisService _aiService;
    private readonly IAutomationLogicService _automationService;
    private readonly IKafkaProducerService _kafkaProducer;
    private readonly ILogger<KafkaEventProcessor> _logger;

    public KafkaEventProcessor(
        IAiAnalysisService aiService,
        IAutomationLogicService automationService,
        IKafkaProducerService kafkaProducer,
        ILogger<KafkaEventProcessor> logger)
    {
        _aiService = aiService;
        _automationService = automationService;
        _kafkaProducer = kafkaProducer;
        _logger = logger;
    }

    public async Task ProcessRawEventAsync(RawEvent rawEvent)
    {
        try
        {
            _logger.LogInformation(
                "Processing event - EventId: {EventId}, CommentId: {CommentId}, UserId: {UserId}",
                rawEvent.EventId, rawEvent.CommentId, rawEvent.UserId);

            // Step 1: AI Analysis
            var aiAnalysis = await _aiService.AnalyzeCommentAsync(rawEvent.Message);
            _logger.LogInformation(
                "AI Analysis result - Intent: {Intent}, Sentiment: {Sentiment}, IsSpam: {IsSpam}",
                aiAnalysis.Intent, aiAnalysis.Sentiment, aiAnalysis.IsSpam);

            // Step 2: Determine automation action
            var action = _automationService.DetermineAction(aiAnalysis, rawEvent);
            _logger.LogInformation(
                "Determined action - ActionType: {ActionType}, Reason: {Reason}",
                action.ActionType, action.Reason);

            // Step 3: Publish reply command
            var replyCommand = new ReplyCommand
            {
                CommandId = Guid.NewGuid().ToString(),
                EventId = rawEvent.EventId,
                CommentId = rawEvent.CommentId,
                PostId = rawEvent.PostId,
                Action = action.ActionType,
                ReplyText = action.ReplyMessage,
                Reason = action.Reason,
                RetryCount = 0,
                Timestamp = DateTime.UtcNow
            };

            await _kafkaProducer.PublishReplyCommandAsync(replyCommand);
            _logger.LogInformation("Reply command published - CommandId: {CommandId}", replyCommand.CommandId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing raw event - EventId: {EventId}", rawEvent.EventId);
            throw;
        }
    }
}

/// <summary>
/// Kafka Producer for publishing reply commands
/// </summary>
public class KafkaProducerService : IKafkaProducerService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<KafkaProducerService> _logger;
    private readonly IProducer<string, string>? _producer;

    public KafkaProducerService(IConfiguration configuration, ILogger<KafkaProducerService> logger)
    {
        _configuration = configuration;
        _logger = logger;

        var kafkaConfig = configuration.GetSection("Kafka");
        var bootstrapServers = kafkaConfig["BootstrapServers"] ?? "localhost:9092";

        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All,
            RetryBackoffMs = 100,
            MessageSendMaxRetries = 3
        };

        _producer = new ProducerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => _logger.LogError("Kafka Producer error: {Reason}", e.Reason))
            .Build();
    }

    public async Task PublishReplyCommandAsync(ReplyCommand command)
    {
        if (_producer == null)
        {
            _logger.LogError("Kafka producer not initialized");
            throw new InvalidOperationException("Kafka producer not initialized");
        }

        try
        {
            var kafkaConfig = _configuration.GetSection("Kafka");
            var topic = kafkaConfig["Topics:ReplyCommands"] ?? "reply_commands";

            var messageJson = System.Text.Json.JsonSerializer.Serialize(command);
            var result = await _producer.ProduceAsync(topic, new Message<string, string>
            {
                Key = command.CommandId,
                Value = messageJson
            });

            _logger.LogInformation(
                "Message published - Topic: {Topic}, Partition: {Partition}, Offset: {Offset}",
                result.Topic, result.Partition, result.Offset);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing reply command - CommandId: {CommandId}", command.CommandId);
            throw;
        }
    }
}
