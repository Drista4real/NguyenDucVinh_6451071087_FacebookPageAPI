using RetryService.Models;

namespace RetryService.Services;

public interface IRetryLogicService
{
    RetryDecision DetermineRetryAction(SendFailedMessage message, RetryPolicy policy);
}

public sealed class RetryLogicService : IRetryLogicService
{
    public RetryDecision DetermineRetryAction(SendFailedMessage message, RetryPolicy policy)
    {
        policy.Validate();

        if (!message.Retryable)
        {
            return new RetryDecision(
                ShouldRetry: false,
                Backoff: TimeSpan.Zero,
                Reason: "non_retryable");
        }

        if (message.RetryCount >= policy.MaxRetryAttempts)
        {
            return new RetryDecision(
                ShouldRetry: false,
                Backoff: TimeSpan.Zero,
                Reason: "max_attempts_exceeded");
        }

        return new RetryDecision(
            ShouldRetry: true,
            Backoff: policy.CalculateBackoff(message.RetryCount),
            Reason: "retry_scheduled");
    }
}
