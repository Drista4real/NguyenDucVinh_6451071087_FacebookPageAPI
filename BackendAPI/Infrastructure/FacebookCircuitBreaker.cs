using BackendAPI.Options;
using Microsoft.Extensions.Options;

namespace BackendAPI.Infrastructure;

public sealed class FacebookCircuitBreaker
{
    private readonly object _sync = new();
    private readonly CircuitBreakerOptions _options;
    private int _consecutiveFailures;
    private DateTimeOffset? _openUntil;
    private bool _halfOpenRequestInProgress;

    public FacebookCircuitBreaker(IOptions<CircuitBreakerOptions> options)
    {
        _options = options.Value;
    }

    public void EnsureRequestAllowed()
    {
        lock (_sync)
        {
            if (_openUntil is null)
            {
                return;
            }

            if (DateTimeOffset.UtcNow < _openUntil)
            {
                throw new CircuitBreakerOpenException(_openUntil.Value);
            }

            if (_halfOpenRequestInProgress)
            {
                throw new CircuitBreakerOpenException(_openUntil.Value);
            }

            _halfOpenRequestInProgress = true;
        }
    }

    public void RecordSuccess()
    {
        lock (_sync)
        {
            _consecutiveFailures = 0;
            _openUntil = null;
            _halfOpenRequestInProgress = false;
        }
    }

    public void RecordTransientFailure()
    {
        lock (_sync)
        {
            _halfOpenRequestInProgress = false;
            _consecutiveFailures++;
            if (_consecutiveFailures >= Math.Max(1, _options.FailureThreshold))
            {
                _openUntil = DateTimeOffset.UtcNow.AddSeconds(
                    Math.Max(1, _options.OpenSeconds));
            }
        }
    }

    public void RecordCancelledRequest()
    {
        lock (_sync)
        {
            _halfOpenRequestInProgress = false;
        }
    }
}

public sealed class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(DateTimeOffset retryAt)
        : base($"Facebook circuit breaker đang mở. Thử lại sau {retryAt:O}.")
    {
        RetryAt = retryAt;
    }

    public DateTimeOffset RetryAt { get; }
}
