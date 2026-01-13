using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using CommonResilience = Lidarr.Plugin.Common.Services.Resilience;

namespace NzbDrone.Core.ImportLists.Brainarr.Resilience
{
    /// <summary>
    /// Circuit breaker pattern implementation for provider resilience.
    /// Prevents cascading failures by temporarily blocking calls to failing providers.
    /// Implements the classic three-state pattern: Closed → Open → HalfOpen → Closed.
    /// </summary>
    /// <remarks>
    /// This implementation wraps Lidarr.Plugin.Common's CircuitBreaker and adds:
    /// - Timeout support for operations
    /// - NLog integration for logging
    /// - Provider-specific factory configurations
    /// </remarks>
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
        Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName, CancellationToken cancellationToken = default);

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
        /// Gets the current number of failures in the sliding window.
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

    /// <summary>
    /// Circuit breaker implementation that wraps Lidarr.Plugin.Common's CircuitBreaker
    /// and adds timeout support for operations.
    /// </summary>
    public class CircuitBreaker : ICircuitBreaker
    {
        private readonly CommonResilience.ICircuitBreaker _inner;
        private readonly TimeSpan _timeout;
        private readonly Logger _logger;

        public CircuitState State => MapState(_inner.State);
        public int FailureCount => _inner.Statistics.FailuresInWindow;
        public DateTime? LastFailureTime => _inner.Statistics.LastFailureTime;

        public CircuitBreaker(
            int failureThreshold = 3,
            int openDurationSeconds = 60,
            int timeoutSeconds = 30,
            Logger logger = null,
            TimeProvider timeProvider = null)
        {
            _timeout = TimeSpan.FromSeconds(timeoutSeconds);
            _logger = logger ?? LogManager.GetCurrentClassLogger();

            var options = new CommonResilience.CircuitBreakerOptions
            {
                FailureThreshold = failureThreshold,
                SlidingWindowSize = Math.Max(failureThreshold, 10),
                OpenDuration = TimeSpan.FromSeconds(openDurationSeconds),
                SuccessThresholdInHalfOpen = 1
            };

            // Create inner circuit breaker with NLog adapter and optional TimeProvider
            _inner = new CommonResilience.CircuitBreaker(
                $"brainarr-{Guid.NewGuid():N}",
                options,
                new NLogAdapter(_logger),
                timeProvider);
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
        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _inner.ExecuteAsync(async ct =>
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(_timeout);

                    var task = operation();
                    var completedTask = await Task.WhenAny(task, Task.Delay(_timeout, cts.Token)).ConfigureAwait(false);

                    if (completedTask != task)
                    {
                        throw new TimeoutException($"Operation {operationName} timed out after {_timeout.TotalSeconds}s");
                    }

                    return await task.ConfigureAwait(false);
                }, cancellationToken, operationName).ConfigureAwait(false);
            }
            catch (CommonResilience.CircuitBreakerOpenException ex)
            {
                var msg = $"Circuit breaker is open for {operationName}. Service unavailable.";
                _logger.Warn(msg);
                throw new CircuitBreakerOpenException(msg);
            }
        }

        /// <summary>
        /// Manually resets the circuit breaker to closed state, clearing all failure history.
        /// Thread-safe operation that immediately allows new requests to proceed.
        /// </summary>
        public void Reset()
        {
            _inner.Reset();
            _logger.Info("Circuit breaker reset");
        }

        private static CircuitState MapState(CommonResilience.CircuitState state)
        {
            return state switch
            {
                CommonResilience.CircuitState.Closed => CircuitState.Closed,
                CommonResilience.CircuitState.Open => CircuitState.Open,
                CommonResilience.CircuitState.HalfOpen => CircuitState.HalfOpen,
                _ => CircuitState.Closed
            };
        }

        /// <summary>
        /// Adapter to bridge NLog Logger to Microsoft.Extensions.Logging.ILogger
        /// </summary>
        private class NLogAdapter : Microsoft.Extensions.Logging.ILogger
        {
            private readonly Logger _logger;

            public NLogAdapter(Logger logger)
            {
                _logger = logger;
            }

            IDisposable Microsoft.Extensions.Logging.ILogger.BeginScope<TState>(TState state) => NullScope.Instance;

            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

            public void Log<TState>(
                Microsoft.Extensions.Logging.LogLevel logLevel,
                Microsoft.Extensions.Logging.EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var message = formatter(state, exception);
                switch (logLevel)
                {
                    case Microsoft.Extensions.Logging.LogLevel.Trace:
                    case Microsoft.Extensions.Logging.LogLevel.Debug:
                        _logger.Debug(exception, message);
                        break;
                    case Microsoft.Extensions.Logging.LogLevel.Information:
                        _logger.Info(exception, message);
                        break;
                    case Microsoft.Extensions.Logging.LogLevel.Warning:
                        _logger.Warn(exception, message);
                        break;
                    case Microsoft.Extensions.Logging.LogLevel.Error:
                    case Microsoft.Extensions.Logging.LogLevel.Critical:
                        _logger.Error(exception, message);
                        break;
                }
            }

            private sealed class NullScope : IDisposable
            {
                public static NullScope Instance { get; } = new NullScope();
                public void Dispose() { }
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
