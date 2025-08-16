using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Support
{
    /// <summary>
    /// Circuit breaker pattern implementation to prevent cascading failures
    /// </summary>
    public interface ICircuitBreaker
    {
        Task<T> ExecuteAsync<T>(Func<Task<T>> action, string operationName = null);
        CircuitState State { get; }
        void Reset();
        CircuitBreakerStatistics GetStatistics();
    }

    public enum CircuitState
    {
        Closed,    // Normal operation
        Open,      // Failure threshold exceeded, rejecting calls
        HalfOpen   // Testing if service recovered
    }

    public class CircuitBreakerStatistics
    {
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public int RejectedCount { get; set; }
        public DateTime? LastFailureTime { get; set; }
        public DateTime? LastSuccessTime { get; set; }
        public TimeSpan? AverageResponseTime { get; set; }
    }

    public class CircuitBreaker : ICircuitBreaker
    {
        private readonly Logger _logger;
        private readonly int _failureThreshold;
        private readonly TimeSpan _openDuration;
        private readonly TimeSpan _halfOpenTestTimeout;
        
        private CircuitState _state = CircuitState.Closed;
        private int _failureCount = 0;
        private int _successCount = 0;
        private int _rejectedCount = 0;
        private DateTime? _lastFailureTime;
        private DateTime? _lastSuccessTime;
        private DateTime _openedAt;
        
        private readonly SemaphoreSlim _stateChangeLock = new SemaphoreSlim(1, 1);
        private readonly object _statsLock = new object();

        public CircuitState State => _state;

        public CircuitBreaker(
            Logger logger,
            int failureThreshold = 5,
            TimeSpan? openDuration = null,
            TimeSpan? halfOpenTestTimeout = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _failureThreshold = failureThreshold;
            _openDuration = openDuration ?? TimeSpan.FromMinutes(1);
            _halfOpenTestTimeout = halfOpenTestTimeout ?? TimeSpan.FromSeconds(10);
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, string operationName = null)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            operationName = operationName ?? "Operation";

            // Check if circuit should transition from Open to HalfOpen
            if (_state == CircuitState.Open)
            {
                await _stateChangeLock.WaitAsync();
                try
                {
                    if (_state == CircuitState.Open && 
                        DateTime.UtcNow - _openedAt >= _openDuration)
                    {
                        _logger.Info($"Circuit breaker transitioning to HalfOpen for {operationName}");
                        _state = CircuitState.HalfOpen;
                    }
                }
                finally
                {
                    _stateChangeLock.Release();
                }
            }

            // Reject if still open
            if (_state == CircuitState.Open)
            {
                lock (_statsLock)
                {
                    _rejectedCount++;
                }
                
                var timeRemaining = _openDuration - (DateTime.UtcNow - _openedAt);
                throw new CircuitBreakerOpenException(
                    $"Circuit breaker is open for {operationName}. Retry after {timeRemaining.TotalSeconds:F0} seconds");
            }

            // Execute with timeout for HalfOpen state
            var timeout = _state == CircuitState.HalfOpen ? _halfOpenTestTimeout : TimeSpan.FromMinutes(5);
            var cts = new CancellationTokenSource(timeout);

            try
            {
                var startTime = DateTime.UtcNow;
                T result;

                if (cts.Token.CanBeCanceled)
                {
                    var task = action();
                    var completedTask = await Task.WhenAny(task, Task.Delay(timeout, cts.Token));
                    
                    if (completedTask != task)
                    {
                        throw new TimeoutException($"{operationName} timed out after {timeout.TotalSeconds} seconds");
                    }
                    
                    result = await task;
                }
                else
                {
                    result = await action();
                }

                var responseTime = DateTime.UtcNow - startTime;
                
                // Success - update state
                await OnSuccessAsync(operationName, responseTime);
                
                return result;
            }
            catch (Exception ex)
            {
                await OnFailureAsync(operationName, ex);
                throw;
            }
            finally
            {
                cts?.Dispose();
            }
        }

        private async Task OnSuccessAsync(string operationName, TimeSpan responseTime)
        {
            lock (_statsLock)
            {
                _successCount++;
                _lastSuccessTime = DateTime.UtcNow;
            }

            if (_state == CircuitState.HalfOpen)
            {
                await _stateChangeLock.WaitAsync();
                try
                {
                    if (_state == CircuitState.HalfOpen)
                    {
                        _logger.Info($"Circuit breaker closing after successful test for {operationName}");
                        _state = CircuitState.Closed;
                        _failureCount = 0;
                    }
                }
                finally
                {
                    _stateChangeLock.Release();
                }
            }
            else if (_state == CircuitState.Closed)
            {
                // Reset failure count on success in closed state
                Interlocked.Exchange(ref _failureCount, 0);
            }
        }

        private async Task OnFailureAsync(string operationName, Exception exception)
        {
            lock (_statsLock)
            {
                _lastFailureTime = DateTime.UtcNow;
            }

            if (_state == CircuitState.HalfOpen)
            {
                await _stateChangeLock.WaitAsync();
                try
                {
                    if (_state == CircuitState.HalfOpen)
                    {
                        _logger.Warn($"Circuit breaker reopening after failed test for {operationName}: {exception.Message}");
                        _state = CircuitState.Open;
                        _openedAt = DateTime.UtcNow;
                        _failureCount = _failureThreshold; // Reset to threshold
                    }
                }
                finally
                {
                    _stateChangeLock.Release();
                }
            }
            else if (_state == CircuitState.Closed)
            {
                var newFailureCount = Interlocked.Increment(ref _failureCount);
                
                if (newFailureCount >= _failureThreshold)
                {
                    await _stateChangeLock.WaitAsync();
                    try
                    {
                        if (_state == CircuitState.Closed && _failureCount >= _failureThreshold)
                        {
                            _logger.Error($"Circuit breaker opening after {_failureCount} failures for {operationName}");
                            _state = CircuitState.Open;
                            _openedAt = DateTime.UtcNow;
                        }
                    }
                    finally
                    {
                        _stateChangeLock.Release();
                    }
                }
            }
        }

        public void Reset()
        {
            _stateChangeLock.Wait();
            try
            {
                _logger.Info("Circuit breaker manually reset");
                _state = CircuitState.Closed;
                _failureCount = 0;
                
                lock (_statsLock)
                {
                    _successCount = 0;
                    _rejectedCount = 0;
                    _lastFailureTime = null;
                    _lastSuccessTime = null;
                }
            }
            finally
            {
                _stateChangeLock.Release();
            }
        }

        public CircuitBreakerStatistics GetStatistics()
        {
            lock (_statsLock)
            {
                return new CircuitBreakerStatistics
                {
                    SuccessCount = _successCount,
                    FailureCount = _failureCount,
                    RejectedCount = _rejectedCount,
                    LastFailureTime = _lastFailureTime,
                    LastSuccessTime = _lastSuccessTime
                };
            }
        }
    }

    public class CircuitBreakerOpenException : Exception
    {
        public CircuitBreakerOpenException(string message) : base(message) { }
    }

    /// <summary>
    /// Factory for creating circuit breakers with consistent configuration
    /// </summary>
    public class CircuitBreakerFactory
    {
        private readonly Logger _logger;

        public CircuitBreakerFactory(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ICircuitBreaker CreateForProvider(string providerName)
        {
            return providerName?.ToLower() switch
            {
                "ollama" => new CircuitBreaker(_logger, 10, TimeSpan.FromSeconds(30)), // Local, more tolerant
                "lmstudio" => new CircuitBreaker(_logger, 10, TimeSpan.FromSeconds(30)),
                "openai" => new CircuitBreaker(_logger, 3, TimeSpan.FromMinutes(2)), // Strict for paid APIs
                "anthropic" => new CircuitBreaker(_logger, 3, TimeSpan.FromMinutes(2)),
                "gemini" => new CircuitBreaker(_logger, 5, TimeSpan.FromMinutes(1)),
                _ => new CircuitBreaker(_logger, 5, TimeSpan.FromMinutes(1)) // Default
            };
        }
    }
}