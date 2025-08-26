using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using NzbDrone.Core.ImportLists.Brainarr.Services;

namespace Brainarr.Tests
{
    /// <summary>
    /// Tests for AsyncHelper to ensure it prevents deadlocks in synchronous contexts.
    /// These tests verify that async code can be safely called from sync methods
    /// without causing the deadlocks that occur with .GetAwaiter().GetResult().
    /// </summary>
    public class AsyncHelperTests
    {
        [Fact]
        [Trait("Category", "Critical")]
        public void RunSync_WithAsyncMethod_DoesNotDeadlock()
        {
            // Arrange
            async Task<string> AsyncMethod()
            {
                await Task.Delay(10);
                return "Success";
            }

            // Act
            var result = AsyncHelper.RunSync(() => AsyncMethod());

            // Assert
            Assert.Equal("Success", result);
        }

        [Fact]
        [Trait("Category", "Critical")]
        public void RunSync_WithMultipleAsyncCalls_DoesNotDeadlock()
        {
            // Arrange
            async Task<int> AsyncCalculation(int value)
            {
                await Task.Delay(5);
                return value * 2;
            }

            // Act - Multiple async calls in sequence
            var result1 = AsyncHelper.RunSync(() => AsyncCalculation(5));
            var result2 = AsyncHelper.RunSync(() => AsyncCalculation(10));
            var result3 = AsyncHelper.RunSync(() => AsyncCalculation(15));

            // Assert
            Assert.Equal(10, result1);
            Assert.Equal(20, result2);
            Assert.Equal(30, result3);
        }

        [Fact]
        [Trait("Category", "Critical")]
        public void RunSync_WithConfigureAwaitFalse_WorksCorrectly()
        {
            // Arrange
            async Task<string> AsyncMethodWithConfigureAwait()
            {
                await Task.Delay(10).ConfigureAwait(false);
                // After ConfigureAwait(false), we should be on a thread pool thread
                Assert.Null(SynchronizationContext.Current);
                return "No Deadlock";
            }

            // Act
            var result = AsyncHelper.RunSync(() => AsyncMethodWithConfigureAwait());

            // Assert
            Assert.Equal("No Deadlock", result);
        }

        [Fact]
        [Trait("Category", "Critical")]
        public async Task RunSync_SimulatesUIContext_DoesNotDeadlock()
        {
            // This test simulates the deadlock scenario that occurs in UI/ASP.NET contexts
            // when using .GetAwaiter().GetResult() directly
            
            var tcs = new TaskCompletionSource<bool>();
            Exception caughtException = null;
            
            // Create a thread with a SynchronizationContext (simulating UI/ASP.NET)
            var thread = new Thread(() =>
            {
                try
                {
                    // Set up a SynchronizationContext (like in WinForms/WPF/ASP.NET)
                    SynchronizationContext.SetSynchronizationContext(new TestSynchronizationContext());
                    
                    // This would deadlock with .GetAwaiter().GetResult()
                    // but should work with AsyncHelper
                    var result = AsyncHelper.RunSync(async () =>
                    {
                        await Task.Delay(10);
                        return "No Deadlock in UI Context";
                    });
                    
                    Assert.Equal("No Deadlock in UI Context", result);
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                    tcs.SetResult(false);
                }
            });
            
            // Only set apartment state on Windows platform
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                thread.SetApartmentState(ApartmentState.STA);
            }
            thread.Start();
            
            // Wait for test to complete (with timeout to prevent hanging)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                var completed = await tcs.Task.WaitAsync(cts.Token);
                Assert.True(completed);
            }
            catch (TimeoutException)
            {
                Assert.Fail("Test timed out - possible deadlock!");
            }
            
            // Assert
            Assert.Null(caughtException);
        }

        [Fact]
        [Trait("Category", "Critical")]
        public void RunSyncWithTimeout_ExceedsTimeout_ThrowsTimeoutException()
        {
            // Arrange
            async Task<string> LongRunningTask()
            {
                await Task.Delay(200); // Longer than 100ms timeout to test timeout behavior
                return "Should not reach here";
            }

            // Act & Assert
            Assert.Throws<TimeoutException>(() =>
                AsyncHelper.RunSyncWithTimeout(() => LongRunningTask(), TimeSpan.FromMilliseconds(100))
            );
        }

        [Fact]
        [Trait("Category", "Critical")]
        public void RunSyncWithTimeout_CompletesBeforeTimeout_ReturnsResult()
        {
            // Arrange
            async Task<string> QuickTask()
            {
                await Task.Delay(10);
                return "Completed";
            }

            // Act
            var result = AsyncHelper.RunSyncWithTimeout(() => QuickTask(), TimeSpan.FromSeconds(1));

            // Assert
            Assert.Equal("Completed", result);
        }

        [Fact]
        [Trait("Category", "Critical")]
        public void RunSync_WithException_PropagatesException()
        {
            // Arrange
            async Task<string> FailingAsyncMethod()
            {
                await Task.Delay(10);
                throw new InvalidOperationException("Test exception");
            }

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                AsyncHelper.RunSync(() => FailingAsyncMethod())
            );
            
            Assert.Equal("Test exception", exception.Message);
        }

        [Fact]
        [Trait("Category", "Critical")]
        public void RunSync_WithCancellationToken_RespectsCancellation()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.Cancel(); // Pre-cancel the token

            async Task<string> CancellableTask(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(10, token);
                return "Should not reach here";
            }

            // Act & Assert
            Assert.Throws<OperationCanceledException>(() =>
                AsyncHelper.RunSync(() => CancellableTask(cts.Token))
            );
        }

        [Fact]
        [Trait("Category", "Performance")]
        public async Task RunSync_HighConcurrency_NoDeadlocks()
        {
            // Arrange
            const int concurrentTasks = 100;
            var tasks = new List<Task>();
            var errors = new List<Exception>();
            var successCount = 0;

            async Task<int> AsyncWork(int id)
            {
                await Task.Delay(Random.Shared.Next(1, 10));
                return id;
            }

            // Act - Launch many concurrent sync-over-async operations
            for (int i = 0; i < concurrentTasks; i++)
            {
                var taskId = i;
                var task = Task.Run(() =>
                {
                    try
                    {
                        var result = AsyncHelper.RunSync(() => AsyncWork(taskId));
                        if (result == taskId)
                        {
                            Interlocked.Increment(ref successCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (errors)
                        {
                            errors.Add(ex);
                        }
                    }
                });
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);

            // Assert
            Assert.Empty(errors);
            Assert.Equal(concurrentTasks, successCount);
        }

        /// <summary>
        /// Test SynchronizationContext that simulates UI/ASP.NET context behavior
        /// </summary>
        private class TestSynchronizationContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object state)
            {
                // In a real UI context, this would post to the UI thread
                // For testing, we just execute synchronously
                d(state);
            }

            public override void Send(SendOrPostCallback d, object state)
            {
                d(state);
            }
        }
    }
}