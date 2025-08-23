using System;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Provides safe mechanisms for executing async code in synchronous contexts.
    /// This is specifically designed for Lidarr plugin compatibility where the
    /// framework requires synchronous methods but our implementation is async.
    /// </summary>
    /// <remarks>
    /// IMPORTANT: This class prevents deadlocks that occur with .GetAwaiter().GetResult()
    /// in ASP.NET/UI contexts by ensuring the async operation runs on a thread pool thread
    /// without a SynchronizationContext.
    /// </remarks>
    public static class AsyncHelper
    {
        private static readonly TaskFactory _taskFactory = new TaskFactory(
            CancellationToken.None,
            TaskCreationOptions.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);

        /// <summary>
        /// Executes an async operation synchronously without risk of deadlock.
        /// Use this when you MUST call async code from a sync method (like Lidarr's Fetch()).
        /// </summary>
        /// <typeparam name="TResult">The type of the result</typeparam>
        /// <param name="func">The async function to execute</param>
        /// <returns>The result of the async operation</returns>
        /// <example>
        /// public override IList&lt;ImportListItemInfo&gt; Fetch()
        /// {
        ///     return AsyncHelper.RunSync(() => FetchAsync());
        /// }
        /// </example>
        public static TResult RunSync<TResult>(Func<Task<TResult>> func)
        {
            if (func == null)
                throw new ArgumentNullException(nameof(func));

            // This ensures the async method runs on a thread pool thread
            // without a SynchronizationContext, preventing deadlocks
            return _taskFactory
                .StartNew(func)
                .Unwrap()
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Executes an async operation synchronously without risk of deadlock.
        /// Use this for async methods that don't return a value.
        /// </summary>
        /// <param name="func">The async function to execute</param>
        public static void RunSync(Func<Task> func)
        {
            if (func == null)
                throw new ArgumentNullException(nameof(func));

            _taskFactory
                .StartNew(func)
                .Unwrap()
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Executes an async operation with a timeout to prevent hanging.
        /// </summary>
        /// <typeparam name="TResult">The type of the result</typeparam>
        /// <param name="func">The async function to execute</param>
        /// <param name="timeout">Maximum time to wait for the operation</param>
        /// <returns>The result of the async operation</returns>
        /// <exception cref="TimeoutException">Thrown when the operation exceeds the timeout</exception>
        public static TResult RunSyncWithTimeout<TResult>(Func<Task<TResult>> func, TimeSpan timeout)
        {
            if (func == null)
                throw new ArgumentNullException(nameof(func));

            using (var cts = new CancellationTokenSource(timeout))
            {
                try
                {
                    var task = _taskFactory
                        .StartNew(func)
                        .Unwrap();

                    task.Wait(cts.Token);
                    return task.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds} seconds");
                }
            }
        }
    }
}