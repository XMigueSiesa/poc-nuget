namespace Pos.Host.LocalPOS.Sync;

/// <summary>
/// Simple thread-safe circuit breaker for the cloud sync HTTP calls.
/// States: Closed (normal) → Open (failing) → HalfOpen (testing recovery) → Closed.
/// </summary>
public sealed class SyncCircuitBreaker
{
    private enum State { Closed, Open, HalfOpen }

    private readonly int _failureThreshold;
    private readonly TimeSpan _resetTimeout;
    private readonly ILogger _logger;

    private State _state = State.Closed;
    private int _consecutiveFailures;
    private DateTimeOffset _openedAt = DateTimeOffset.MinValue;
    private readonly object _lock = new();

    public SyncCircuitBreaker(
        int failureThreshold,
        int resetTimeoutSeconds,
        ILogger logger)
    {
        _failureThreshold = failureThreshold;
        _resetTimeout = TimeSpan.FromSeconds(resetTimeoutSeconds);
        _logger = logger;
    }

    /// <summary>Returns true when the circuit is open and calls should be skipped.</summary>
    public bool IsOpen()
    {
        lock (_lock)
        {
            if (_state == State.Closed)
                return false;

            if (_state == State.Open
                && DateTimeOffset.UtcNow - _openedAt >= _resetTimeout)
            {
                _state = State.HalfOpen;
                _logger.LogInformation("Circuit breaker → HalfOpen, testing recovery");
                return false;
            }

            return _state == State.Open;
        }
    }

    /// <summary>Report a successful call. Resets the failure counter.</summary>
    public void RecordSuccess()
    {
        lock (_lock)
        {
            if (_state == State.HalfOpen)
            {
                _logger.LogInformation("Circuit breaker → Closed, recovery confirmed");
            }

            _state = State.Closed;
            _consecutiveFailures = 0;
        }
    }

    /// <summary>Report a failed call. Opens the circuit when the threshold is reached.</summary>
    public void RecordFailure()
    {
        lock (_lock)
        {
            _consecutiveFailures++;

            if (_state == State.HalfOpen || _consecutiveFailures >= _failureThreshold)
            {
                _state = State.Open;
                _openedAt = DateTimeOffset.UtcNow;
                _logger.LogWarning(
                    "Circuit breaker → Open after {Failures} consecutive failures. "
                    + "Will retry in {Timeout}s",
                    _consecutiveFailures,
                    _resetTimeout.TotalSeconds);
            }
        }
    }
}
