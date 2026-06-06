using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RetryService.Models;
using RetryService.Services;

namespace RetryService.Tests;

public sealed class RetryMessageProcessorTests
{
    [Fact]
    public async Task ProcessAsync_PublishesRetryCommand_WithIncrementedRetryCount()
    {
        var producer = new FakeProducer();
        var delay = new FakeDelay();
        var processor = CreateProcessor(producer, delay);

        var processed = await processor.ProcessAsync(
            Serialize(CreateFailedMessage(retryCount: 1, retryable: true)),
            default);

        Assert.True(processed);
        Assert.Equal(TimeSpan.FromSeconds(2), delay.Delays.Single());
        var command = Assert.Single(producer.RetryCommands);
        Assert.Equal("cmd-1", command.CommandId);
        Assert.Equal(2, command.RetryCount);
        Assert.Empty(producer.DeadLetters);

        var json = JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var backendCommand = JsonSerializer.Deserialize<BackendAPI.Models.FacebookCommand>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(backendCommand);
        Assert.Equal(command.RetryCount, backendCommand!.RetryCount);
    }

    [Fact]
    public async Task ProcessAsync_PublishesDeadLetter_ForNonRetryableErrors()
    {
        var producer = new FakeProducer();
        var delay = new FakeDelay();
        var processor = CreateProcessor(producer, delay);

        await processor.ProcessAsync(
            Serialize(CreateFailedMessage(retryCount: 0, retryable: false)),
            default);

        Assert.Empty(delay.Delays);
        Assert.Empty(producer.RetryCommands);
        var deadLetter = Assert.Single(producer.DeadLetters);
        Assert.Equal("non_retryable", deadLetter.Reason);
        Assert.Equal("cmd-1", deadLetter.Command.CommandId);
    }

    [Fact]
    public async Task ProcessAsync_PublishesDeadLetter_WhenMaxAttemptsReached()
    {
        var producer = new FakeProducer();
        var delay = new FakeDelay();
        var processor = CreateProcessor(producer, delay);

        await processor.ProcessAsync(
            Serialize(CreateFailedMessage(retryCount: 5, retryable: true)),
            default);

        var deadLetter = Assert.Single(producer.DeadLetters);
        Assert.Equal("max_attempts_exceeded", deadLetter.Reason);
        Assert.Equal(5, deadLetter.RetryCount);
    }

    private static RetryMessageProcessor CreateProcessor(
        FakeProducer producer,
        FakeDelay delay) =>
        new(
            new RetryLogicService(),
            producer,
            delay,
            Options.Create(new RetryPolicy()),
            NullLogger<RetryMessageProcessor>.Instance);

    private static string Serialize(SendFailedMessage message) =>
        JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static SendFailedMessage CreateFailedMessage(
        int retryCount,
        bool retryable) =>
        new()
        {
            Command = new FacebookCommand
            {
                CommandId = "cmd-1",
                Action = "reply_comment",
                TargetId = "comment-1",
                PageId = "page-1",
                Message = "hello",
                RetryCount = retryCount,
                EventId = "event-1"
            },
            RetryCount = retryCount,
            Retryable = retryable,
            Error = "timeout",
            FailedAt = DateTimeOffset.UtcNow
        };

    private sealed class FakeProducer : IKafkaProducerService
    {
        public List<FacebookCommand> RetryCommands { get; } = [];
        public List<DeadLetterMessage> DeadLetters { get; } = [];

        public Task PublishRetryCommandAsync(
            FacebookCommand command,
            CancellationToken cancellationToken)
        {
            RetryCommands.Add(command);
            return Task.CompletedTask;
        }

        public Task PublishDeadLetterAsync(
            DeadLetterMessage message,
            CancellationToken cancellationToken)
        {
            DeadLetters.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDelay : IBackoffDelay
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }
}
