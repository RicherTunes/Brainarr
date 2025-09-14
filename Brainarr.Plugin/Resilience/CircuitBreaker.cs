using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Resilience
{
    /// <summary>
    /// Circuit breaker pattern implementation for provider resilience.
    /// Prevents cascading failures by temporarily blocking calls to failing providers.
    /// Implements the classic three-state pattern: Closed → Open → HalfOpen → Closed.
    /// </summary>
    public interface ICircuitBreaker
    {
        /// <summary>
        /// Executes an operation with circuit breaker protection.
        /// Will throw CircuitBreakerOpenException if the circuit is open.
        /// </summary>
        /// <typeparam name="T">Return type of the operation</typeparam>
        /// <param name="operation">The operation to execute safely</param>
        /// <param name="operationName">Human-readable name for logging</param>
        /// <returns>Result of the operation if successful</returns>
        /// <exception cref="CircuitBreakerOpenException">Circuit is open due to recent failures</exception>
        Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName);

        /// <summary>
        /// Manually resets the circuit breaker to the closed state.
        /// Clears failure count and allows operations to proceed.
        /// </summary>
        void Reset();

        /// <summary>
        /// Gets the current state of the circuit breaker.
        /// </summary>
        CircuitState State { get; }

        /// <summary>
        /// Gets the current number of consecutive failures.
        /// Resets to zero when circuit transitions to closed state.
        /// </summary>
        int FailureCount { get; }

        /// <summary>
        /// Gets the timestamp of the most recent failure.
        /// Null if no failures have occurred since last reset.
        /// </summary>
        DateTime? LastFailureTime { get; }
    }

    public enum CircuitState
    {
        Closed,     // Normal operation
        Open,       // Blocking calls due to failures
        HalfOpen    // Testing if service recovered
    }

    public class CircuitBreaker : ICircuitBreaker
    {
        private readonly Logger _logger;
        private readonly int _failureThreshold;
        private readonly TimeSpan _openDuration;
        private readonly TimeSpan _timeout;

        private CircuitState _state = CircuitState.Closed;
        private int _failureCount;
        private DateTime? _lastFailureTime;
        private DateTime? _openedAt;
        private readonly SemaphoreSlim _stateChangeLock = new SemaphoreSlim(1, 1);

        public CircuitState State => _state;
        public int FailureCount => _failureCount;
        public DateTime? LastFailureTime => _lastFailureTime;

        public CircuitBreaker(
            int failureThreshold = 3,
            int openDurationSeconds = 60,
            int timeoutSeconds = 30,
            Logger logger = null)
        {
            _failureThreshold = failureThreshold;
            _openDuration = TimeSpan.FromSeconds(openDurationSeconds);
            _timeout = TimeSpan.FromSeconds(timeoutSeconds);
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Executes an operation with full circuit breaker protection including timeouts.
        /// Handles state transitions and failure recording automatically.
        /// </summary>
        /// <typeparam name="T">Return type of the protected operation</typeparam>
        /// <param name="operation">The async operation to execute with protection</param>
        /// <param name="operationName">Human-readable operation name for logging and error messages</param>
        /// <returns>Result of the operation if successful</returns>
        /// <exception cref="CircuitBreakerOpenException">Circuit is open, operation blocked</exception>
        /// <exception cref="TimeoutException">Operation exceeded configured timeout</exception>
        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName)
        {
            await EnsureCircuitState();

            switch (_state)
            {
                case CircuitState.Open:
                    var msg = $"Circuit breaker is open for {operationName}. Service unavailable.";
                    _logger.Warn(msg);
                    throw new CircuitBreakerOpenException(msg);

                case CircuitState.HalfOpen:
                    return await ExecuteInHalfOpen(operation, operationName);

                case CircuitState.Closed:
                default:
                    return await ExecuteInClosed(operation, operationName);
            }
        }

        private async Task<T> ExecuteInClosed<T>(Func<Task<T>> operation, string operationName)
        {
            try
            {
                using var cts = new CancellationTokenSource(_timeout);
                var task = operation();
                var completedTask = await Task.WhenAny(task, Task.Delay(_timeout, cts.Token));

                if (completedTask != task)
                {
                    throw new TimeoutException($"Operation {operationName} timed out after {_timeout.TotalSeconds}s");
                }

                var result = await task;
                await RecordSuccess();
                return result;
            }
            catch (Exception ex)
            {
                await RecordFailure(ex, operationName);
                throw;
            }
        }

        private async Task<T> ExecuteInHalfOpen<T>(Func<Task<T>> operation, string operationName)
        {
            try
            {
                _logger.Debug($"Testing {operationName} in half-open state");

                using var cts = new CancellationTokenSource(_timeout);
                var task = operation();
                var completedTask = await Task.WhenAny(task, Task.Delay(_timeout, cts.Token));

                if (completedTask != task)
                {
                    throw new TimeoutException($"Operation {operationName} timed out in half-open state");
                }

                var result = await task;
                await TransitionToClosed();
                _logger.Info($"Circuit breaker closed for {operationName} - service recovered");
                return result;
            }
            catch (Exception ex)
            {
                await TransitionToOpen(ex, operationName);
                throw;
            }
        }

        private async Task RecordSuccess()
        {
            await _stateChangeLock.WaitAsync();
            try
            {
                if (_failureCount > 0)
                {
                    _failureCount = Math.Max(0, _failureCount - 1);
                }
            }
            finally
            {
                _stateChangeLock.Release();
            }
        }

        private async Task RecordFailure(Exception ex, string operationName)
        {
            await _stateChangeLock.WaitAsync();
            try
            {
                _failureCount++;
                _lastFailureTime = DateTime.UtcNow;

                _logger.Warn($"Operation {operationName} failed ({_failureCount}/{_failureThreshold}): {ex.Message}");

                if (_failureCount >= _failureThreshold && _state == CircuitState.Closed)
                {
                    await TransitionToOpenInternal(ex, operationName);
                }
            }
            finally
            {
                _stateChangeLock.Release();
            }
        }

        private async Task TransitionToOpen(Exception ex, string operationName)
        {
            await _stateChangeLock.WaitAsync();
            try
            {
                await TransitionToOpenInternal(ex, operationName);
            }
            finally
            {
                _stateChangeLock.Release();
            }
        }

        private Task TransitionToOpenInternal(Exception ex, string operationName)
        {
            _state = CircuitState.Open;
            _openedAt = DateTime.UtcNow;
            _logger.Error($"Circuit breaker opened for {operationName} after {_failureCount} failures. Last error: {ex.Message}");
            return Task.CompletedTask;
        }

        private async Task TransitionToClosed()
        {
            await _stateChangeLock.WaitAsync();
            try
            {
                _state = CircuitState.Closed;
                _failureCount = 0;
                _openedAt = null;
            }
            finally
            {
                _stateChangeLock.Release();
            }
        }

        private async Task EnsureCircuitState()
        {
            if (_state == CircuitState.Open && _openedAt.HasValue)
            {
                var elapsed = DateTime.UtcNow - _openedAt.Value;
                if (elapsed >= _openDuration)
                {
                    await _stateChangeLock.WaitAsync();
                    try
                    {
                        if (_state == CircuitState.Open) // Double-check
                        {
                            _state = CircuitState.HalfOpen;
                            _logger.Debug("Circuit breaker transitioned to half-open");
                        }
                    }
                    finally
                    {
                        _stateChangeLock.Release();
                    }
                }
            }
        }

        /// <summary>
        /// Manually resets the circuit breaker to closed state, clearing all failure history.
        /// Thread-safe operation that immediately allows new requests to proceed.
        /// Typically used for administrative recovery or testing scenarios.
        /// </summary>
        public void Reset()
        {
            _stateChangeLock.Wait();
            try
            {
                _state = CircuitState.Closed;
                _failureCount = 0;
                _lastFailureTime = null;
                _openedAt = null;
                _logger.Info("Circuit breaker reset");
            }
            finally
            {
                _stateChangeLock.Release();
            }
        }
    }

    public class CircuitBreakerOpenException : Exception
    {
        public CircuitBreakerOpenException(string message) : base(message) { }
    }

    /// <summary>
    /// Factory for creating provider-specific circuit breakers.
    /// </summary>
    public class CircuitBreakerFactory
    {
        private readonly ConcurrentDictionary<string, ICircuitBreaker> _breakers;
        private readonly Logger _logger;

        public CircuitBreakerFactory(Logger logger = null)
        {
            _breakers = new ConcurrentDictionary<string, ICircuitBreaker>();
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        public ICircuitBreaker GetOrCreate(string provider)
        {
            return _breakers.GetOrAdd(provider, p =>
            {
                _logger.Debug($"Creating circuit breaker for provider: {p}");

                // Provider-specific configurations
                return p switch
                {
                    "Ollama" => new CircuitBreaker(5, 30, 60, _logger),      // Local, more tolerant
                    "LMStudio" => new CircuitBreaker(5, 30, 60, _logger),    // Local, more tolerant
                    "OpenAI" => new CircuitBreaker(3, 60, 30, _logger),      // Cloud, standard
                    "Anthropic" => new CircuitBreaker(3, 60, 30, _logger),   // Cloud, standard
                    "Groq" => new CircuitBreaker(2, 30, 15, _logger),        // Fast, less tolerant
                    _ => new CircuitBreaker(3, 60, 30, _logger)              // Default
                };
            });
        }

        public void ResetAll()
        {
            foreach (var breaker in _breakers.Values)
            {
                breaker.Reset();
            }
            _logger.Info("All circuit breakers reset");
        }
    }
}
