using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using CommonBreaker = Lidarr.Plugin.Common.Services.Resilience.AdvancedCircuitBreaker;
using CommonOptions = Lidarr.Plugin.Common.Services.Resilience.AdvancedCircuitBreakerOptions;
using CommonState = Lidarr.Plugin.Common.Services.Resilience.CircuitState;
using CommonOpenException = Lidarr.Plugin.Common.Services.Resilience.CircuitBreakerOpenException;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Resilience
{
    /// <summary>
    /// Adapts Common's AdvancedCircuitBreaker to Brainarr's ICircuitBreaker interface.
    /// This adapter preserves existing Brainarr semantics while delegating to Common's implementation.
    /// </summary>
    /// <remarks>
    /// Key behavior preserved from Brainarr's original CircuitBreaker:
    /// - TaskCanceledException is treated as a failure (surprising but documented)
    /// - HttpRequestException with "4" AND "Bad Request" in message is excluded (brittle string matching)
    /// - TimeoutException and HttpRequestException are treated as failures
    /// - Other exceptions pass through without affecting breaker state
    /// </remarks>
    internal sealed class BrainarrCircuitBreakerAdapter : ICircuitBreaker
    {
        private readonly CommonBreaker _inner;
        private readonly Logger _logger;
        private readonly TimeSpan _breakDuration;
        private DateTime _lastStateChange = DateTime.UtcNow;

        public BrainarrCircuitBreakerAdapter(string resourceName, CommonOptions options, Logger logger)
        {
            ResourceName = resourceName ?? throw new ArgumentNullException(nameof(resourceName));
            _logger = logger ?? LogManager.GetCurrentClassLogger();

            // Configure Common's options with Brainarr's exception classification
            var configuredOptions = ConfigureOptions(options);
            _breakDuration = configuredOptions.BreakDuration;
            _inner = new CommonBreaker(resourceName, configuredOptions);        
            _inner.StateChanged += OnInnerStateChanged;
        }

        public string ResourceName { get; }

        public CircuitState State => MapState(_inner.State);

        public DateTime LastStateChange => _lastStateChange;

        public int ConsecutiveFailures => _inner.ConsecutiveFailures;

        public double FailureRate => _inner.FailureRate;

        public event EventHandler<CircuitBreakerEventArgs> CircuitOpened;
        public event EventHandler<CircuitBreakerEventArgs> CircuitClosed;

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _inner.ExecuteAsync(operation).ConfigureAwait(false);
            }
            catch (CommonOpenException ex)
            {
                // Translate Common's exception to Brainarr's
                throw new CircuitBreakerOpenException(
                    $"Circuit breaker is open for {ResourceName}. Will retry at {DateTime.UtcNow.Add(ex.RetryAfter ?? TimeSpan.Zero):HH:mm:ss}");
            }
        }

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

        public CircuitBreakerStatistics GetStatistics()
        {
            var stats = _inner.Statistics;
            DateTime? nextHalfOpenAttempt = null;
            if (State == CircuitState.Open)
            {
                nextHalfOpenAttempt = stats.LastOpenedTime?.Add(_breakDuration) ?? _lastStateChange.Add(_breakDuration);
            }
            return new CircuitBreakerStatistics
            {
                ResourceName = ResourceName,
                State = State,
                ConsecutiveFailures = ConsecutiveFailures,
                FailureRate = FailureRate,
                TotalOperations = (int)(stats.TotalSuccesses + stats.TotalFailures),
                LastStateChange = _lastStateChange,
                NextHalfOpenAttempt = nextHalfOpenAttempt,
                RecentOperations = null // Not exposed by Common, and not critical for behavior
            };
        }

        public void Reset()
        {
            _inner.Reset();
            _lastStateChange = DateTime.UtcNow;
            _logger.Info($"Circuit breaker manually RESET for {ResourceName}");
        }

        /// <summary>
        /// Configures Common's AdvancedCircuitBreakerOptions with Brainarr's exception classification.
        /// </summary>
        private static CommonOptions ConfigureOptions(CommonOptions baseOptions)
        {
            // Clone the base options and override exception classification
            return new CommonOptions
            {
                ConsecutiveFailureThreshold = baseOptions?.ConsecutiveFailureThreshold ?? 5,
                FailureRateThreshold = baseOptions?.FailureRateThreshold ?? 0.5,
                MinimumThroughput = baseOptions?.MinimumThroughput ?? 10,
                SamplingWindowSize = baseOptions?.SamplingWindowSize ?? 20,
                BreakDuration = baseOptions?.BreakDuration ?? TimeSpan.FromSeconds(30),
                HalfOpenSuccessThreshold = baseOptions?.HalfOpenSuccessThreshold ?? 3,
                IsFailure = IsFailure,
                IsIgnored = IsIgnored
            };
        }

        /// <summary>
        /// Determines if an exception counts as a failure (trips the breaker).
        /// Matches original Brainarr behavior: TaskCanceledException, TimeoutException, HttpRequestException
        /// EXCEPT client errors with "4" AND "Bad Request" in message.
        /// </summary>
        private static bool IsFailure(Exception ex)
        {
            // First check if it's a handled type
            if (!(ex is TaskCanceledException || ex is TimeoutException || ex is HttpRequestException))
            {
                return false;
            }

            // Exclude client errors (4xx) - brittle string matching preserved from original
            if (ex.Message.Contains("4") && ex.Message.Contains("Bad Request"))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determines if an exception should be ignored entirely (not counted at all).
        /// Currently null - all non-failure exceptions pass through without counting.
        /// </summary>
        private static bool IsIgnored(Exception ex)
        {
            // In Brainarr's original implementation, non-handled exceptions just pass through
            // without being recorded. The Common breaker handles this via IsFailure returning false.
            return false;
        }

        private void OnInnerStateChanged(object sender, global::Lidarr.Plugin.Common.Services.Resilience.CircuitBreakerEventArgs e)
        {
            _lastStateChange = DateTime.UtcNow;

            var args = new CircuitBreakerEventArgs
            {
                ResourceName = ResourceName,
                State = MapState(e.NewState),
                Timestamp = DateTime.UtcNow
            };

            if (e.NewState == CommonState.Open)
            {
                _logger.Error($"Circuit breaker OPENED for {ResourceName}. " +
                             $"Failures: {ConsecutiveFailures}, Rate: {FailureRate:P}");
                CircuitOpened?.Invoke(this, args);
            }
            else if (e.NewState == CommonState.Closed && e.PreviousState != CommonState.Closed)
            {
                _logger.Info($"Circuit breaker CLOSED for {ResourceName}. Provider recovered successfully.");
                CircuitClosed?.Invoke(this, args);
            }
            else if (e.NewState == CommonState.HalfOpen)
            {
                _logger.Info($"Circuit breaker transitioned to HALF-OPEN for {ResourceName}");
            }
        }

        private static CircuitState MapState(CommonState state)
        {
            return state switch
            {
                CommonState.Closed => CircuitState.Closed,
                CommonState.Open => CircuitState.Open,
                CommonState.HalfOpen => CircuitState.HalfOpen,
                _ => CircuitState.Closed
            };
        }
    }
}
