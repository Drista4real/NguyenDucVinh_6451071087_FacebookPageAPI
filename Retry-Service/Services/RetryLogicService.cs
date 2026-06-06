using RetryService.Models;

namespace RetryService.Services;

public interface IRetryLogicService
{
    /// <summary>
    /// Determines retry action based on current retry count and policy
    /// </summary>
    /// <returns>Tuple containing (ShouldRetry, NextRetryTime, BackoffSeconds)</returns>
    (bool ShouldRetry, DateTime NextRetryTime, int BackoffSeconds) DetermineRetryAction(SendFailedMessage message, RetryPolicy policy);
}

public class RetryLogicService : IRetryLogicService
{
    private readonly ILogger<RetryLogicService> _logger;

    public RetryLogicService(ILogger<RetryLogicService> logger)
    {
        _logger = logger;
    }

    public (bool ShouldRetry, DateTime NextRetryTime, int BackoffSeconds) DetermineRetryAction(
        SendFailedMessage message, 
        RetryPolicy policy)
    {
        try
        {
            int nextRetryCount = message.RetryCount + 1;
            bool shouldRetry = policy.ShouldRetry(nextRetryCount);

            if (!shouldRetry)
            {
                _logger.LogWarning(
                    "Max retry attempts reached for command {CommandId}. " +
                    "Total retries: {TotalRetries}. Will move to Dead Letter Queue.",
                    message.CommandId, message.RetryCount);
                return (false, DateTime.UtcNow, 0);
            }

            int backoffSeconds = policy.CalculateBackoffSeconds(nextRetryCount);
            DateTime nextRetryTime = DateTime.UtcNow.AddSeconds(backoffSeconds);

            _logger.LogInformation(
                "Will retry command {CommandId}. Attempt {CurrentAttempt}/{MaxAttempts}. " +
                "Backoff: {BackoffSeconds} seconds. Next retry at {NextRetryTime}",
                message.CommandId, nextRetryCount, policy.MaxRetryAttempts,
                backoffSeconds, nextRetryTime);

            return (true, nextRetryTime, backoffSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error determining retry action for command {CommandId}", message.CommandId);
            throw;
        }
    }
}
