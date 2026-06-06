using RetryService.Models;
using RetryService.Services;

namespace RetryService.Tests;

public sealed class RetryLogicServiceTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    public void DetermineRetryAction_UsesExponentialBackoff(
        int retryCount,
        int expectedSeconds)
    {
        var service = new RetryLogicService();
        var message = CreateFailedMessage(retryCount, retryable: true);

        var decision = service.DetermineRetryAction(message, new RetryPolicy());

        Assert.True(decision.ShouldRetry);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), decision.Backoff);
    }

    [Fact]
    public void DetermineRetryAction_DoesNotRetry_NonRetryableErrors()
    {
        var service = new RetryLogicService();

        var decision = service.DetermineRetryAction(
            CreateFailedMessage(0, retryable: false),
            new RetryPolicy());

        Assert.False(decision.ShouldRetry);
        Assert.Equal("non_retryable", decision.Reason);
    }

    [Fact]
    public void DetermineRetryAction_DoesNotRetry_WhenMaxAttemptsReached()
    {
        var service = new RetryLogicService();

        var decision = service.DetermineRetryAction(
            CreateFailedMessage(5, retryable: true),
            new RetryPolicy());

        Assert.False(decision.ShouldRetry);
        Assert.Equal("max_attempts_exceeded", decision.Reason);
    }

    [Fact]
    public void RetryPolicy_CapsBackoff()
    {
        var policy = new RetryPolicy
        {
            InitialBackoffSeconds = 10,
            BackoffMultiplier = 3,
            MaxBackoffSeconds = 20
        };

        Assert.Equal(TimeSpan.FromSeconds(20), policy.CalculateBackoff(3));
    }

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
                Message = "hello",
                RetryCount = retryCount,
                EventId = "event-1"
            },
            RetryCount = retryCount,
            Retryable = retryable,
            Error = "failure",
            FailedAt = DateTimeOffset.UtcNow
        };
}
