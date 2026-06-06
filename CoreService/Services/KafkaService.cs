using System.Text.Json;
using Confluent.Kafka;
using CoreService.Models;
using CoreService.Options;
using Microsoft.Extensions.Options;

namespace CoreService.Services;

public interface IKafkaEventProcessor
{
    Task ProcessRawEventAsync(RawEvent rawEvent, CancellationToken cancellationToken);
}

public interface IKafkaProducerService
{
    Task PublishFacebookCommandAsync(
        FacebookCommand command,
        CancellationToken cancellationToken);
}

public sealed class KafkaConsumerWorkerService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

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
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AllowAutoCreateTopics = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, error) => _logger.LogError("Kafka error: {Reason}", error.Reason))
            .Build();

        consumer.Subscribe(_options.Topics.RawEvents);
        _logger.LogInformation("Core Service subscribed to {Topic}", _options.Topics.RawEvents);

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

                var rawEvent = JsonSerializer.Deserialize<RawEvent>(
                    result.Message.Value.TrimStart('\uFEFF'),
                    JsonOptions);

                if (rawEvent is null || string.IsNullOrWhiteSpace(rawEvent.EventId))
                {
                    _logger.LogWarning("Skipping invalid raw event payload: {Payload}", result.Message.Value);
                    consumer.Commit(result);
                    continue;
                }

                using var scope = _serviceProvider.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IKafkaEventProcessor>();
                await processor.ProcessRawEventAsync(rawEvent, stoppingToken);

                consumer.StoreOffset(result);
                consumer.Commit(result);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Skipping malformed raw event payload");
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
                _logger.LogError(ex, "Core event processing failed; seeking message for retry");
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

public sealed class KafkaEventProcessor : IKafkaEventProcessor
{
    private readonly IAiAnalysisService _aiService;
    private readonly IAutomationLogicService _automationService;
    private readonly IKafkaProducerService _producer;
    private readonly ICoreEventStore _store;
    private readonly ModerationOptions _moderation;
    private readonly ILogger<KafkaEventProcessor> _logger;

    public KafkaEventProcessor(
        IAiAnalysisService aiService,
        IAutomationLogicService automationService,
        IKafkaProducerService producer,
        ICoreEventStore store,
        IOptions<ModerationOptions> moderation,
        ILogger<KafkaEventProcessor> logger)
    {
        _aiService = aiService;
        _automationService = automationService;
        _producer = producer;
        _store = store;
        _moderation = moderation.Value;
        _logger = logger;
    }

    public async Task ProcessRawEventAsync(
        RawEvent rawEvent,
        CancellationToken cancellationToken)
    {
        var started = false;
        try
        {
            started = await _store.TryStartAsync(
                rawEvent,
                TimeSpan.FromMinutes(_moderation.ProcessingLeaseMinutes),
                cancellationToken);

            if (!started)
            {
                _logger.LogInformation("Skipping duplicate raw event {EventId}", rawEvent.EventId);
                return;
            }

            if (!IsSupportedCommentAdd(rawEvent))
            {
                await _store.MarkSkippedAsync(
                    rawEvent.EventId,
                    "unsupported_event",
                    cancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(rawEvent.TargetId))
            {
                await _store.MarkSkippedAsync(rawEvent.EventId, "missing_target_id", cancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(rawEvent.Message))
            {
                await _store.MarkSkippedAsync(rawEvent.EventId, "missing_message", cancellationToken);
                return;
            }

            if (!string.IsNullOrWhiteSpace(rawEvent.UserId) &&
                await _store.IsBlacklistedAsync(rawEvent.UserId, cancellationToken))
            {
                await _store.MarkPendingReviewAsync(
                    rawEvent.EventId,
                    null,
                    "blacklisted_user",
                    cancellationToken);
                return;
            }

            if (!string.IsNullOrWhiteSpace(rawEvent.UserId))
            {
                var recentCount = await _store.CountRecentUserEventsAsync(
                    rawEvent.UserId,
                    TimeSpan.FromSeconds(_moderation.RateLimitWindowSeconds),
                    cancellationToken);

                if (recentCount >= _moderation.RateLimitMaxComments)
                {
                    await _store.MarkPendingReviewAsync(
                        rawEvent.EventId,
                        null,
                        "rate_limit",
                        cancellationToken);
                    return;
                }
            }

            var analysis = await _aiService.AnalyzeCommentAsync(
                rawEvent.Message,
                cancellationToken);
            var action = _automationService.DetermineAction(analysis, rawEvent);

            if (analysis.IsSpam &&
                !string.IsNullOrWhiteSpace(rawEvent.UserId))
            {
                var previousSpamCount = await _store.CountRecentUserSpamAsync(
                    rawEvent.UserId,
                    TimeSpan.FromHours(_moderation.BlacklistWindowHours),
                    cancellationToken);

                if (previousSpamCount + 1 >= _moderation.BlacklistSpamThreshold)
                {
                    await _store.MarkBlacklistedAsync(
                        rawEvent.UserId,
                        "spam_threshold",
                        cancellationToken);
                }
            }

            if (string.Equals(action.ActionType, "pending_review", StringComparison.OrdinalIgnoreCase))
            {
                await _store.MarkPendingReviewAsync(
                    rawEvent.EventId,
                    analysis,
                    action.Reason,
                    cancellationToken);
                return;
            }

            var command = BuildCommand(rawEvent, action);
            await _producer.PublishFacebookCommandAsync(command, cancellationToken);
            await _store.MarkCommandPublishedAsync(
                rawEvent.EventId,
                analysis,
                action,
                cancellationToken);

            _logger.LogInformation(
                "Published {Action} command {CommandId} for event {EventId}",
                command.Action,
                command.CommandId,
                rawEvent.EventId);
        }
        catch (Exception ex)
        {
            if (started)
            {
                try
                {
                    await _store.MarkFailedAsync(rawEvent.EventId, ex.Message, cancellationToken);
                }
                catch (Exception markFailedException)
                {
                    _logger.LogError(
                        markFailedException,
                        "Could not mark event {EventId} as failed",
                        rawEvent.EventId);
                }
            }

            throw;
        }
    }

    private static bool IsSupportedCommentAdd(RawEvent rawEvent) =>
        string.Equals(rawEvent.EventType, "comment", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(rawEvent.Action, "add", StringComparison.OrdinalIgnoreCase);

    private static FacebookCommand BuildCommand(RawEvent rawEvent, AutomationAction action) =>
        new()
        {
            CommandId = CommandIdFactory.Create(
                rawEvent.EventId,
                action.ActionType,
                rawEvent.TargetId),
            EventId = rawEvent.EventId,
            Action = action.ActionType,
            TargetId = rawEvent.TargetId,
            PageId = rawEvent.PageId,
            Message = string.Equals(action.ActionType, "reply_comment", StringComparison.OrdinalIgnoreCase)
                ? action.ReplyMessage
                : null,
            RetryCount = 0
        };
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

    public async Task PublishFacebookCommandAsync(
        FacebookCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _producer.ProduceAsync(
            _options.Topics.ReplyCommands,
            new Message<string, string>
            {
                Key = command.CommandId,
                Value = JsonSerializer.Serialize(command, JsonOptions)
            },
            cancellationToken);

        _logger.LogInformation(
            "Published command {CommandId} to {Topic}@{Partition}[{Offset}]",
            command.CommandId,
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
