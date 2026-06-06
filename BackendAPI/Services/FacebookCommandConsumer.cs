using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using BackendAPI.Infrastructure;
using BackendAPI.Models;
using BackendAPI.Options;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace BackendAPI.Services;

public sealed class FacebookCommandConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly KafkaOptions _options;
    private readonly IFacebookService _facebookService;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly IKafkaPublisher _publisher;
    private readonly ILogger<FacebookCommandConsumer> _logger;

    public FacebookCommandConsumer(
        IOptions<KafkaOptions> options,
        IFacebookService facebookService,
        IIdempotencyStore idempotencyStore,
        IKafkaPublisher publisher,
        ILogger<FacebookCommandConsumer> logger)
    {
        _options = options.Value;
        _facebookService = facebookService;
        _idempotencyStore = idempotencyStore;
        _publisher = publisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Kafka consumer is disabled");
            return;
        }

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AllowAutoCreateTopics = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe([
            _options.ReplyCommandsTopic,
            _options.SendRetryTopic
        ]);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(stoppingToken);
                await ProcessAsync(result.Message.Value, stoppingToken);
                consumer.StoreOffset(result);
                consumer.Commit(result);
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
                _logger.LogError(ex, "Unexpected command consumer error");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        consumer.Close();
    }

    private async Task ProcessAsync(
        string payload,
        CancellationToken cancellationToken)
    {
        FacebookCommand command;
        try
        {
            command = JsonSerializer.Deserialize<FacebookCommand>(payload, JsonOptions)
                      ?? throw new JsonException("Command payload is null.");
            Validate(command);
        }
        catch (Exception ex) when (ex is JsonException or ValidationException)
        {
            _logger.LogError(ex, "Ignoring invalid Kafka command: {Payload}", payload);
            return;
        }

        if (!await _idempotencyStore.TryStartAsync(
                command.CommandId,
                cancellationToken))
        {
            _logger.LogInformation(
                "Skipping duplicate command {CommandId}",
                command.CommandId);
            return;
        }

        try
        {
            await ExecuteCommandAsync(command, cancellationToken);
            await _idempotencyStore.MarkCompletedAsync(
                command.CommandId,
                cancellationToken);
            _logger.LogInformation(
                "Completed Facebook command {CommandId} ({Action})",
                command.CommandId,
                command.Action);
        }
        catch (Exception ex)
        {
            await _idempotencyStore.RemoveAsync(command.CommandId, cancellationToken);

            var retryable = ex switch
            {
                FacebookApiException facebookException => facebookException.IsTransient,
                CircuitBreakerOpenException => true,
                HttpRequestException => true,
                _ => false
            };

            await _publisher.PublishAsync(
                _options.SendFailedTopic,
                command.CommandId,
                new SendFailedMessage
                {
                    Command = command,
                    RetryCount = command.RetryCount,
                    Retryable = retryable,
                    Error = ex.Message
                },
                cancellationToken);

            _logger.LogError(
                ex,
                "Command {CommandId} failed and was published to {Topic}",
                command.CommandId,
                _options.SendFailedTopic);
        }
    }

    private Task ExecuteCommandAsync(
        FacebookCommand command,
        CancellationToken cancellationToken) =>
        command.Action.Trim().ToLowerInvariant() switch
        {
            "reply_comment" => _facebookService.ReplyToCommentAsync(
                Require(command.TargetId, "target_id"),
                Require(command.Message, "message"),
                cancellationToken),
            "hide_comment" => _facebookService.SetCommentHiddenAsync(
                Require(command.TargetId, "target_id"),
                true,
                cancellationToken),
            "unhide_comment" => _facebookService.SetCommentHiddenAsync(
                Require(command.TargetId, "target_id"),
                false,
                cancellationToken),
            "create_post" => _facebookService.CreatePostAsync(
                Require(command.PageId, "page_id"),
                new CreatePostRequest
                {
                    Message = Require(command.Message, "message")
                },
                cancellationToken),
            "delete_post" => DeletePostAsync(command, cancellationToken),
            _ => throw new ValidationException(
                $"Unsupported command action '{command.Action}'.")
        };

    private async Task<System.Text.Json.JsonElement> DeletePostAsync(
        FacebookCommand command,
        CancellationToken cancellationToken)
    {
        var success = await _facebookService.DeletePostAsync(
            Require(command.TargetId, "target_id"),
            cancellationToken);
        return JsonSerializer.SerializeToElement(new { success });
    }

    private static void Validate(FacebookCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.CommandId) ||
            command.CommandId.Length > 100)
        {
            throw new ValidationException("command_id is required (max 100).");
        }

        if (string.IsNullOrWhiteSpace(command.Action))
        {
            throw new ValidationException("action is required.");
        }
    }

    private static string Require(string? value, string fieldName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ValidationException($"{fieldName} is required.")
            : value;
}
