using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Resilience
{
    /// <summary>
    /// Implements the Circuit Breaker pattern to prevent cascading failures
    /// and provide graceful degradation when AI providers are experiencing issues.
    /// </summary>
    /// <remarks>
    /// The circuit breaker has three states:
    /// - Closed: Normal operation, requests pass through
    /// - Open: Provider is failing, requests are blocked
    /// - Half-Open: Testing if provider has recovered
    ///
    /// This implementation provides:
    /// - Automatic failure detection based on configurable thresholds
    /// - Cool-down periods to allow providers to recover
    /// - Gradual recovery testing with half-open state
    /// - Metrics collection for monitoring and alerting
    /// </remarks>
    public class CircuitBreaker : ICircuitBreaker
    {
        private readonly Logger _logger;
        private readonly object _lock = new object();

        // Circuit breaker state
        private CircuitState _state = CircuitState.Closed;
        private DateTime _lastStateChange = DateTime.UtcNow;
        private DateTime _nextHalfOpenAttempt = DateTime.MinValue;

        // Failure tracking
        private int _consecutiveFailures = 0;
        private int _successCount = 0;
        private int _failureCount = 0;
        private readonly CircularBuffer<OperationResult> _recentOperations;

        // Configuration
        private readonly CircuitBreakerOptions _options;

        public CircuitBreaker(string resourceName, CircuitBreakerOptions options, Logger logger)
        {
            ResourceName = resourceName;
            _options = options ?? CircuitBreakerOptions.Default;
            _logger = logger;
            _recentOperations = new CircularBuffer<OperationResult>(_options.SamplingWindowSize);
        }

        public string ResourceName { get; }
        public CircuitState State => _state;
        public DateTime LastStateChange => _lastStateChange;
        public int ConsecutiveFailures => _consecutiveFailures;
        public double FailureRate => CalculateFailureRate();

        /// <summary>
        /// Executes an operation through the circuit breaker with protection.
        /// </summary>
        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
        {
            if (!CanExecute())
            {
                throw new CircuitBreakerOpenException(
                    $"Circuit breaker is open for {ResourceName}. Will retry at {_nextHalfOpenAttempt:HH:mm:ss}");
            }

            var startTime = DateTime.UtcNow;
            try
            {
                var result = await operation().ConfigureAwait(false);
                RecordSuccess(DateTime.UtcNow - startTime);
                return result;
            }
            catch (Exception ex) when (ShouldHandleException(ex))
            {
                RecordFailure(ex, DateTime.UtcNow - startTime);
                throw;
            }
        }

        /// <summary>
        /// Executes an operation with a fallback value if the circuit is open.
        /// </summary>
        public async Task<T> ExecuteWithFallbackAsync<T>(
            Func<Task<T>> operation,
            T fallbackValue,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await ExecuteAsync(operation, cancellationToken).ConfigureAwait(false);
            }
            catch (CircuitBreakerOpenException)
            {
                _logger.Warn($"Circuit breaker open for {ResourceName}, using fallback value");
                return fallbackValue;
            }
        }

        private bool CanExecute()
        {
            lock (_lock)
            {
                switch (_state)
                {
                    case CircuitState.Closed:
                        return true;

                    case CircuitState.Open:
                        if (DateTime.UtcNow >= _nextHalfOpenAttempt)
                        {
                            TransitionToHalfOpen();
                            return true;
                        }
                        return false;

                    case CircuitState.HalfOpen:
                        // Allow limited requests in half-open state
                        return _successCount < _options.HalfOpenSuccessThreshold;

                    default:
                        return false;
                }
            }
        }

        private void RecordSuccess(TimeSpan duration)
        {
            lock (_lock)
            {
                _recentOperations.Add(new OperationResult
                {
                    Success = true,
                    Timestamp = DateTime.UtcNow,
                    Duration = duration
                });

                _consecutiveFailures = 0;

                if (_state == CircuitState.HalfOpen)
                {
                    _successCount++;
                    if (_successCount >= _options.HalfOpenSuccessThreshold)
                    {
                        TransitionToClosed();
                    }
                }

                // Emit metrics
                EmitMetrics(true, duration);
            }
        }

        private void RecordFailure(Exception exception, TimeSpan duration)
        {
            lock (_lock)
            {
                _recentOperations.Add(new OperationResult
                {
                    Success = false,
                    Timestamp = DateTime.UtcNow,
                    Duration = duration,
                    Exception = exception
                });

                _consecutiveFailures++;
                _failureCount++;

                switch (_state)
                {
                    case CircuitState.Closed:
                        if (ShouldOpenCircuit())
                        {
                            TransitionToOpen();
                        }
                        break;

                    case CircuitState.HalfOpen:
                        // Any failure in half-open immediately opens the circuit
                        TransitionToOpen();
                        break;
                }

                // Emit metrics
                EmitMetrics(false, duration);

                _logger.Warn($"Circuit breaker recorded failure for {ResourceName}: {exception.Message}");
            }
        }

        private bool ShouldOpenCircuit()
        {
            // Check consecutive failures threshold
            if (_consecutiveFailures >= _options.FailureThreshold)
            {
                return true;
            }

            // Check failure rate threshold
            var failureRate = CalculateFailureRate();
            if (failureRate >= _options.FailureRateThreshold &&
                _recentOperations.Count >= _options.MinimumThroughput)
            {
                return true;
            }

            return false;
        }

        private double CalculateFailureRate()
        {
            if (_recentOperations.Count == 0)
                return 0;

            var failures = _recentOperations.CountWhere(r => !r.Success);
            return (double)failures / _recentOperations.Count;
        }

        private void TransitionToOpen()
        {
            _state = CircuitState.Open;
            _lastStateChange = DateTime.UtcNow;
            _nextHalfOpenAttempt = DateTime.UtcNow.Add(_options.BreakDuration);

            _logger.Error($"Circuit breaker OPENED for {ResourceName}. " +
                         $"Failures: {_consecutiveFailures}, Rate: {FailureRate:P}. " +
                         $"Will retry at {_nextHalfOpenAttempt:HH:mm:ss}");

            // Raise event for monitoring/alerting
            OnCircuitOpened();
        }

        private void TransitionToHalfOpen()
        {
            _state = CircuitState.HalfOpen;
            _lastStateChange = DateTime.UtcNow;
            _successCount = 0;

            _logger.Info($"Circuit breaker transitioned to HALF-OPEN for {ResourceName}");
        }

        private void TransitionToClosed()
        {
            _state = CircuitState.Closed;
            _lastStateChange = DateTime.UtcNow;
            _consecutiveFailures = 0;
            _successCount = 0;

            _logger.Info($"Circuit breaker CLOSED for {ResourceName}. Provider recovered successfully.");

            // Raise event for monitoring
            OnCircuitClosed();
        }

        private bool ShouldHandleException(Exception ex)
        {
            // Don't trip circuit for client errors (4xx)
            if (ex.Message.Contains("4") && ex.Message.Contains("Bad Request"))
            {
                return false;
            }

            // Handle timeout and network errors
            return ex is TaskCanceledException ||
                   ex is TimeoutException ||
                   ex is System.Net.Http.HttpRequestException;
        }

        private void EmitMetrics(bool success, TimeSpan duration)
        {
            // This would integrate with your metrics system
            MetricsCollector.Record(new CircuitBreakerMetric
            {
                ResourceName = ResourceName,
                State = _state,
                Success = success,
                Duration = duration,
                ConsecutiveFailures = _consecutiveFailures,
                FailureRate = FailureRate,
                Timestamp = DateTime.UtcNow
            });
        }

        public event EventHandler<CircuitBreakerEventArgs> CircuitOpened;
        public event EventHandler<CircuitBreakerEventArgs> CircuitClosed;

        protected virtual void OnCircuitOpened()
        {
            CircuitOpened?.Invoke(this, new CircuitBreakerEventArgs
            {
                ResourceName = ResourceName,
                State = _state,
                Timestamp = DateTime.UtcNow
            });
        }

        protected virtual void OnCircuitClosed()
        {
            CircuitClosed?.Invoke(this, new CircuitBreakerEventArgs
            {
                ResourceName = ResourceName,
                State = _state,
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Gets current statistics for monitoring and diagnostics.
        /// </summary>
        public CircuitBreakerStatistics GetStatistics()
        {
            lock (_lock)
            {
                return new CircuitBreakerStatistics
                {
                    ResourceName = ResourceName,
                    State = _state,
                    ConsecutiveFailures = _consecutiveFailures,
                    FailureRate = FailureRate,
                    TotalOperations = _recentOperations.Count,
                    LastStateChange = _lastStateChange,
                    NextHalfOpenAttempt = _state == CircuitState.Open ? _nextHalfOpenAttempt : (DateTime?)null,
                    RecentOperations = _recentOperations.ToList()
                };
            }
        }

        /// <summary>
        /// Manually resets the circuit breaker (for admin operations).
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _state = CircuitState.Closed;
                _lastStateChange = DateTime.UtcNow;
                _consecutiveFailures = 0;
                _successCount = 0;
                _failureCount = 0;
                _recentOperations.Clear();

                _logger.Info($"Circuit breaker manually RESET for {ResourceName}");
            }
        }

        private class OperationResult
        {
            public bool Success { get; set; }
            public DateTime Timestamp { get; set; }
            public TimeSpan Duration { get; set; }
            public Exception Exception { get; set; }
        }
    }

    public enum CircuitState
    {
        Closed,    // Normal operation
        Open,      // Blocking requests
        HalfOpen   // Testing recovery
    }

    public class CircuitBreakerOptions
    {
        public int FailureThreshold { get; set; } = 5;
        public double FailureRateThreshold { get; set; } = 0.5; // 50%
        public TimeSpan BreakDuration { get; set; } = TimeSpan.FromMinutes(1);
        public int HalfOpenSuccessThreshold { get; set; } = 3;
        public int SamplingWindowSize { get; set; } = 20;
        public int MinimumThroughput { get; set; } = 10;

        public static CircuitBreakerOptions Default => new CircuitBreakerOptions();

        public static CircuitBreakerOptions Aggressive => new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            FailureRateThreshold = 0.3,
            BreakDuration = TimeSpan.FromMinutes(5)
        };

        public static CircuitBreakerOptions Lenient => new CircuitBreakerOptions
        {
            FailureThreshold = 10,
            FailureRateThreshold = 0.75,
            BreakDuration = TimeSpan.FromSeconds(30)
        };
    }
}
