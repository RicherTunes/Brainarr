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
    /// <remarks>
    /// SYNC-OVER-ASYNC (Category A): Lidarr host forces synchronous entry points
    /// (Fetch, Test, ConfigureServices) that must call async plugin code.
    /// This bridge is intentional and cannot be eliminated without Lidarr host changes.
    /// Uses dedicated thread pool threads with timeout protection to mitigate deadlock risk.
    /// </remarks>
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
                var mainTask = StartDedicatedTask(task);

                // Block with a hard timeout; avoid pipe/cancellation race on some runners
                if (!mainTask.Wait(timeoutMs))
                {
                    Logger.Warn($"SafeAsyncHelper operation timed out after {timeoutMs}ms");
                    throw new TimeoutException($"Operation timed out after {timeoutMs / 1000} seconds");
                }

                // Unwrap and return the result
                return mainTask.GetAwaiter().GetResult();
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
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
            try
            {
                var mainTask = StartDedicatedTask(task);
                if (!mainTask.Wait(timeoutMs))
                {
                    Logger.Warn($"SafeAsyncHelper operation timed out after {timeoutMs}ms");
                    throw new TimeoutException($"Operation timed out after {timeoutMs / 1000} seconds");
                }

                // Ensure exceptions are unwrapped
                mainTask.GetAwaiter().GetResult();
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                if (ex.InnerException is TaskCanceledException)
                {
                    throw new OperationCanceledException("Operation was canceled", ex.InnerException);
                }
                throw ex.InnerException;
            }
        }

        private static Task<T> StartDedicatedTask<T>(Func<Task<T>> task)
        {
            return Task.Factory
                .StartNew(
                    async () => await task().ConfigureAwait(false),
                    default,
                    TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                    TaskScheduler.Default)
                .Unwrap();
        }

        private static Task StartDedicatedTask(Func<Task> task)
        {
            return Task.Factory
                .StartNew(
                    async () => await task().ConfigureAwait(false),
                    default,
                    TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                    TaskScheduler.Default)
                .Unwrap();
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
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            try
            {
                using var cts = new CancellationTokenSource();
                var timeoutTask = Task.Delay(timeoutMs, cts.Token);
                var completed = Task.WhenAny(task, timeoutTask).GetAwaiter().GetResult();

                if (completed == task)
                {
                    cts.Cancel();
                    return task.GetAwaiter().GetResult();
                }

                Logger.Warn($"SafeAsyncHelper operation timed out after {timeoutMs}ms");
                throw new TimeoutException($"Operation timed out after {timeoutMs / 1000} seconds");
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                if (ex.InnerException is TaskCanceledException canceled)
                {
                    throw new OperationCanceledException("Operation was canceled", canceled);
                }

                throw ex.InnerException;
            }
            catch (TaskCanceledException canceled)
            {
                throw new OperationCanceledException("Operation was canceled", canceled);
            }
            catch (TimeoutException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"SafeAsyncHelper operation failed: {ex.Message}");
                throw;
            }
        }
    }
}
