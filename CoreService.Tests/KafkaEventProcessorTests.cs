using System.Text.Json;
using CoreService.Models;
using CoreService.Options;
using CoreService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CoreService.Tests;

public sealed class KafkaEventProcessorTests
{
    [Fact]
    public async Task ProcessRawEventAsync_PublishesReplyCommand_ForPriceInquiry()
    {
        var store = new FakeCoreEventStore();
        var producer = new FakeProducer();
        var processor = CreateProcessor(
            store,
            producer,
            new AiAnalysisResult
            {
                Intent = "ask_price",
                Sentiment = "neutral",
                Confidence = 0.9
            });

        await processor.ProcessRawEventAsync(CreateComment("Shop oi gia bao nhieu?"), default);

        var command = Assert.Single(producer.Commands);
        Assert.Equal("reply_comment", command.Action);
        Assert.Equal("comment-1", command.TargetId);
        Assert.Equal("page-1", command.PageId);
        Assert.Equal(0, command.RetryCount);
        Assert.False(string.IsNullOrWhiteSpace(command.Message));
        Assert.Equal("command_published", store.StatusByEvent["event-1"]);
    }

    [Fact]
    public async Task ProcessRawEventAsync_SkipsUnsupportedEvents()
    {
        var store = new FakeCoreEventStore();
        var producer = new FakeProducer();
        var processor = CreateProcessor(store, producer, new AiAnalysisResult());

        await processor.ProcessRawEventAsync(
            new RawEvent
            {
                EventId = "event-1",
                EventType = "message",
                Action = "received",
                PageId = "page-1",
                UserId = "user-1",
                TargetId = "message-1",
                Message = "hello",
                OccurredAt = DateTimeOffset.UtcNow,
                ReceivedAt = DateTimeOffset.UtcNow
            },
            default);

        Assert.Empty(producer.Commands);
        Assert.Equal("skipped", store.StatusByEvent["event-1"]);
    }

    [Fact]
    public async Task ProcessRawEventAsync_DoesNotReprocessDuplicateEvent()
    {
        var store = new FakeCoreEventStore { StartResult = false };
        var producer = new FakeProducer();
        var ai = new FakeAi(new AiAnalysisResult { Intent = "ask_price" });
        var processor = CreateProcessor(store, producer, ai);

        await processor.ProcessRawEventAsync(CreateComment("price?"), default);

        Assert.Empty(producer.Commands);
        Assert.Equal(0, ai.CallCount);
    }

    [Fact]
    public async Task ProcessRawEventAsync_RateLimitsTwentiethComment()
    {
        var store = new FakeCoreEventStore { RecentUserEventCount = 20 };
        var producer = new FakeProducer();
        var ai = new FakeAi(new AiAnalysisResult { Intent = "ask_price" });
        var processor = CreateProcessor(store, producer, ai);

        await processor.ProcessRawEventAsync(CreateComment("price?"), default);

        Assert.Empty(producer.Commands);
        Assert.Equal(0, ai.CallCount);
        Assert.Equal("pending_review", store.StatusByEvent["event-1"]);
        Assert.Equal("rate_limit", store.ReasonByEvent["event-1"]);
    }

    [Fact]
    public async Task ProcessRawEventAsync_BlacklistsUser_OnThirdSpamInWindow()
    {
        var store = new FakeCoreEventStore { RecentUserSpamCount = 2 };
        var producer = new FakeProducer();
        var processor = CreateProcessor(
            store,
            producer,
            new AiAnalysisResult
            {
                Intent = "spam",
                Sentiment = "neutral",
                IsSpam = true,
                SpamType = "link",
                Confidence = 0.95
            });

        await processor.ProcessRawEventAsync(CreateComment("http://spam.example"), default);

        Assert.Contains("user-1", store.BlacklistedUsers);
        var command = Assert.Single(producer.Commands);
        Assert.Equal("hide_comment", command.Action);
        Assert.Null(command.Message);
    }

    [Fact]
    public void CommandJson_DeserializesAsBackendCommand()
    {
        var command = new FacebookCommand
        {
            CommandId = "cmd-1",
            Action = "reply_comment",
            TargetId = "comment-1",
            PageId = "page-1",
            Message = "hello",
            RetryCount = 0,
            EventId = "event-1"
        };

        var json = JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var backendCommand = JsonSerializer.Deserialize<BackendAPI.Models.FacebookCommand>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(backendCommand);
        Assert.Equal(command.CommandId, backendCommand!.CommandId);
        Assert.Equal(command.Action, backendCommand.Action);
        Assert.Equal(command.TargetId, backendCommand.TargetId);
    }

    private static KafkaEventProcessor CreateProcessor(
        FakeCoreEventStore store,
        FakeProducer producer,
        AiAnalysisResult analysis) =>
        CreateProcessor(store, producer, new FakeAi(analysis));

    private static KafkaEventProcessor CreateProcessor(
        FakeCoreEventStore store,
        FakeProducer producer,
        IAiAnalysisService ai) =>
        new(
            ai,
            new AutomationLogicService(),
            producer,
            store,
            Microsoft.Extensions.Options.Options.Create(new ModerationOptions()),
            NullLogger<KafkaEventProcessor>.Instance);

    private static RawEvent CreateComment(string message) => new()
    {
        EventId = "event-1",
        EventType = "comment",
        Action = "add",
        PageId = "page-1",
        UserId = "user-1",
        TargetId = "comment-1",
        PostId = "post-1",
        Message = message,
        OccurredAt = DateTimeOffset.UtcNow,
        ReceivedAt = DateTimeOffset.UtcNow
    };

    private sealed class FakeAi : IAiAnalysisService
    {
        private readonly AiAnalysisResult _analysis;

        public FakeAi(AiAnalysisResult analysis)
        {
            _analysis = analysis;
        }

        public int CallCount { get; private set; }

        public Task<AiAnalysisResult> AnalyzeCommentAsync(
            string message,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_analysis);
        }
    }

    private sealed class FakeProducer : IKafkaProducerService
    {
        public List<FacebookCommand> Commands { get; } = [];

        public Task PublishFacebookCommandAsync(
            FacebookCommand command,
            CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCoreEventStore : ICoreEventStore
    {
        public bool StartResult { get; init; } = true;
        public int RecentUserEventCount { get; init; } = 1;
        public int RecentUserSpamCount { get; init; }
        public HashSet<string> BlacklistedUsers { get; } = [];
        public Dictionary<string, string> StatusByEvent { get; } = [];
        public Dictionary<string, string> ReasonByEvent { get; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> TryStartAsync(
            RawEvent rawEvent,
            TimeSpan processingLease,
            CancellationToken cancellationToken)
        {
            if (StartResult)
            {
                StatusByEvent[rawEvent.EventId] = "processing";
            }

            return Task.FromResult(StartResult);
        }

        public Task<int> CountRecentUserEventsAsync(
            string userId,
            TimeSpan window,
            CancellationToken cancellationToken) =>
            Task.FromResult(RecentUserEventCount);

        public Task<int> CountRecentUserSpamAsync(
            string userId,
            TimeSpan window,
            CancellationToken cancellationToken) =>
            Task.FromResult(RecentUserSpamCount);

        public Task<bool> IsBlacklistedAsync(
            string userId,
            CancellationToken cancellationToken) =>
            Task.FromResult(BlacklistedUsers.Contains(userId));

        public Task MarkBlacklistedAsync(
            string userId,
            string reason,
            CancellationToken cancellationToken)
        {
            BlacklistedUsers.Add(userId);
            return Task.CompletedTask;
        }

        public Task MarkCommandPublishedAsync(
            string eventId,
            AiAnalysisResult analysis,
            AutomationAction action,
            CancellationToken cancellationToken)
        {
            StatusByEvent[eventId] = "command_published";
            ReasonByEvent[eventId] = action.Reason;
            return Task.CompletedTask;
        }

        public Task MarkPendingReviewAsync(
            string eventId,
            AiAnalysisResult? analysis,
            string reason,
            CancellationToken cancellationToken)
        {
            StatusByEvent[eventId] = "pending_review";
            ReasonByEvent[eventId] = reason;
            return Task.CompletedTask;
        }

        public Task MarkSkippedAsync(
            string eventId,
            string reason,
            CancellationToken cancellationToken)
        {
            StatusByEvent[eventId] = "skipped";
            ReasonByEvent[eventId] = reason;
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            string eventId,
            string reason,
            CancellationToken cancellationToken)
        {
            StatusByEvent[eventId] = "failed";
            ReasonByEvent[eventId] = reason;
            return Task.CompletedTask;
        }
    }
}
