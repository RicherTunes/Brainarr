using System;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public interface IRetryPolicy
    {
        Task<T> ExecuteAsync<T>(Func<Task<T>> action, string operationName);
    }

    public class ExponentialBackoffRetryPolicy : IRetryPolicy
    {
        private readonly Logger _logger;
        private readonly int _maxRetries;
        private readonly TimeSpan _initialDelay;

        public ExponentialBackoffRetryPolicy(Logger logger, int? maxRetries = null, TimeSpan? initialDelay = null)
        {
            _logger = logger;
            _maxRetries = maxRetries ?? BrainarrConstants.MaxRetryAttempts;
            _initialDelay = initialDelay ?? TimeSpan.FromMilliseconds(BrainarrConstants.InitialRetryDelayMs);
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, string operationName)
        {
            Exception lastException = null;
            
            for (int attempt = 0; attempt < _maxRetries; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        var delay = TimeSpan.FromMilliseconds(_initialDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                        _logger.Info($"Retry {attempt}/{_maxRetries} for {operationName} after {delay.TotalSeconds}s delay");
                        await Task.Delay(delay);
                    }

                    return await action();
                }
                catch (TaskCanceledException)
                {
                    // Don't retry on cancellation
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.Warn($"Attempt {attempt + 1}/{_maxRetries} failed for {operationName}: {ex.Message}");
                    
                    if (attempt == _maxRetries - 1)
                    {
                        _logger.Error(ex, $"All {_maxRetries} attempts failed for {operationName}");
                        throw new RetryExhaustedException($"Operation '{operationName}' failed after {_maxRetries} attempts", ex);
                    }
                }
            }

            throw new RetryExhaustedException($"Operation '{operationName}' failed after {_maxRetries} attempts", lastException);
        }
    }

    public class RetryExhaustedException : Exception
    {
        public RetryExhaustedException(string message, Exception innerException) 
            : base(message, innerException) 
        {
        }
    }
}