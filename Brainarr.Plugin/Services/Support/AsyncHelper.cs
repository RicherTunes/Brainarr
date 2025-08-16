using System;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Support
{
    /// <summary>
    /// Provides safe mechanisms for executing async code in synchronous contexts.
    /// This is necessary when interfacing with Lidarr's synchronous base classes.
    /// </summary>
    public static class AsyncHelper
    {
        private static readonly TaskFactory _taskFactory = new TaskFactory(
            CancellationToken.None,
            TaskCreationOptions.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);

        /// <summary>
        /// Executes an async Task method synchronously in a safe manner that avoids deadlocks.
        /// Uses a dedicated task factory to ensure proper context handling.
        /// </summary>
        public static void RunSync(Func<Task> task)
        {
            _taskFactory
                .StartNew(task)
                .Unwrap()
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Executes an async Task method synchronously with a timeout.
        /// </summary>
        public static void RunSync(Func<Task> task, TimeSpan timeout)
        {
            var cts = new CancellationTokenSource(timeout);
            try
            {
                _taskFactory
                    .StartNew(() => task(), cts.Token)
                    .Unwrap()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds} seconds");
            }
        }

        /// <summary>
        /// Executes an async Task&lt;T&gt; method synchronously and returns the result.
        /// Uses a dedicated task factory to avoid deadlocks.
        /// </summary>
        public static T RunSync<T>(Func<Task<T>> task)
        {
            return _taskFactory
                .StartNew(task)
                .Unwrap()
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Executes an async Task&lt;T&gt; method synchronously with a timeout.
        /// </summary>
        public static T RunSync<T>(Func<Task<T>> task, TimeSpan timeout)
        {
            var cts = new CancellationTokenSource(timeout);
            try
            {
                return _taskFactory
                    .StartNew(() => task(), cts.Token)
                    .Unwrap()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds} seconds");
            }
        }
    }
}