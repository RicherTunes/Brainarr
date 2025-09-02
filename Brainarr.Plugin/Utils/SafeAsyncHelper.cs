using System;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Utils
{
    /// <summary>
    /// Safer alternative to AsyncHelper.RunSync with better deadlock prevention.
    /// Provides a more controlled way to bridge sync/async boundaries in Lidarr plugins.
    /// </summary>
    public static class SafeAsyncHelper
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Safely executes an async method from a synchronous context with timeout protection.
        /// Uses a dedicated thread to avoid deadlocks in ASP.NET contexts.
        /// </summary>
        /// <typeparam name="T">The return type</typeparam>
        /// <param name="task">The async task to execute</param>
        /// <param name="timeoutMs">Timeout in milliseconds (default: 2 minutes)</param>
        /// <returns>The task result</returns>
        public static T RunSafeSync<T>(Func<Task<T>> task, int timeoutMs = BrainarrConstants.DefaultAsyncTimeoutMs)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeoutMs);
                
                // Create the task and a timeout task
                var mainTask = Task.Run(async () =>
                {
                    return await task().ConfigureAwait(false);
                });
                
                var timeoutTask = Task.Delay(timeoutMs, cts.Token);
                
                // Wait for either the main task or timeout
                var completedTask = Task.WhenAny(mainTask, timeoutTask).Result;
                
                if (completedTask == timeoutTask)
                {
                    Logger.Warn($"SafeAsyncHelper operation timed out after {timeoutMs}ms");
                    throw new TimeoutException($"Operation timed out after {timeoutMs / 1000} seconds");
                }
                
                // Return the result if main task completed
                return mainTask.Result;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                // Unwrap AggregateException to get the actual exception
                if (ex.InnerException is TaskCanceledException)
                {
                    throw new OperationCanceledException("Operation was canceled", ex.InnerException);
                }
                throw ex.InnerException;
            }
        }

        /// <summary>
        /// Safely executes an async method from a synchronous context (void return).
        /// </summary>
        /// <param name="task">The async task to execute</param>
        /// <param name="timeoutMs">Timeout in milliseconds (default: 2 minutes)</param>
        public static void RunSafeSync(Func<Task> task, int timeoutMs = BrainarrConstants.DefaultAsyncTimeoutMs)
        {
            CancellationTokenSource? cts = null;
            try
            {
                cts = new CancellationTokenSource(timeoutMs);
                
                Task.Run(async () =>
                {
                    try
                    {
                        await task().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
                    {
                        Logger.Warn($"SafeAsyncHelper operation timed out after {timeoutMs}ms");
                        throw new TimeoutException($"Operation timed out after {timeoutMs / 1000} seconds");
                    }
                }, cts.Token).Wait(cts.Token);
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                if (ex.InnerException is TaskCanceledException)
                {
                    throw new OperationCanceledException("Operation was canceled", ex.InnerException);
                }
                throw ex.InnerException;
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Operation timed out after {timeoutMs / 1000} seconds");
            }
            finally
            {
                cts?.Dispose();
            }
        }

        /// <summary>
        /// Executes an async method with a specific timeout, returning the result or default on timeout.
        /// Useful for optional operations that shouldn't block the main flow.
        /// </summary>
        /// <typeparam name="T">The return type</typeparam>
        /// <param name="task">The async task to execute</param>
        /// <param name="timeoutMs">Timeout in milliseconds</param>
        /// <returns>The task result or default(T) on timeout</returns>
        public static T RunSyncWithTimeout<T>(Task<T> task, int timeoutMs = BrainarrConstants.DefaultAsyncTimeoutMs)
        {
            try
            {
                using (var cts = new CancellationTokenSource(timeoutMs))
                {
                    var completedTask = Task.WhenAny(task, Task.Delay(timeoutMs, cts.Token)).Result;

                    if (completedTask == task)
                    {
                        // Propagate exceptions if the task faulted
                        return task.GetAwaiter().GetResult();
                    }

                    Logger.Warn($"SafeAsyncHelper operation timed out after {timeoutMs}ms");
                    throw new TimeoutException($"Operation timed out after {timeoutMs / 1000} seconds");
                }
            }
            catch (Exception ex)
            {
                // Re-throw to allow callers/tests to assert specific exceptions
                if (ex is TimeoutException) throw;
                Logger.Error(ex, $"SafeAsyncHelper operation failed: {ex.Message}");
                throw;
            }
        }
    }
}
