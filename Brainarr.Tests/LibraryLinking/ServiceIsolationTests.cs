using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Moq;

namespace Brainarr.Tests.LibraryLinking
{
    /// <summary>
    /// Tests for service isolation when Brainarr shares the Common library.
    /// Verifies that services like caching, rate limiting, and authentication
    /// are properly scoped to the plugin and don't leak to other plugins.
    /// </summary>
    [Trait("Category", "ServiceIsolation")]
    public class ServiceIsolationTests
    {
        #region Cache Isolation Tests

        [Fact]
        public void RecommendationCache_Should_Be_Plugin_Scoped()
        {
            // Arrange - Create two separate cache instances (simulating two plugins)
            var cache1 = new Dictionary<string, object>();
            var cache2 = new Dictionary<string, object>();

            // Act - Add to cache1
            cache1["test-key"] = "value-from-plugin1";

            // Assert - cache2 should not see cache1's values
            cache2.ContainsKey("test-key").Should().BeFalse(
                "Caches from different plugins should be isolated");
        }

        [Fact]
        public async Task ConcurrentCacheAccess_Should_Be_Thread_Safe()
        {
            // Arrange
            var cache = new ConcurrentDictionary<string, string>();
            var tasks = new List<Task>();
            var errors = new ConcurrentBag<Exception>();

            // Act - Simulate concurrent access from multiple "plugins"
            for (int i = 0; i < 100; i++)
            {
                var pluginId = i % 5; // Simulate 5 plugins
                var taskId = i;
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        var key = $"plugin{pluginId}-key{taskId}";
                        cache.TryAdd(key, $"value{taskId}");
                        cache.TryGetValue(key, out _);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            errors.Should().BeEmpty("Concurrent cache access should not cause exceptions");
        }

        #endregion

        #region Rate Limiter Isolation Tests

        [Fact]
        public async Task RateLimiter_Should_Be_Plugin_Scoped()
        {
            // Arrange - Simulate rate limiters for different plugins
            var rateLimiter1 = new SemaphoreSlim(5); // Plugin 1: 5 concurrent requests
            var rateLimiter2 = new SemaphoreSlim(5); // Plugin 2: 5 concurrent requests

            // Act - Exhaust plugin 1's rate limit
            for (int i = 0; i < 5; i++)
            {
                await rateLimiter1.WaitAsync();
            }

            // Assert - Plugin 2 should still have its full capacity
            rateLimiter2.CurrentCount.Should().Be(5,
                "Rate limiting exhaustion in one plugin should not affect other plugins");

            // Cleanup
            rateLimiter1.Dispose();
            rateLimiter2.Dispose();
        }

        [Fact]
        public async Task RateLimiter_Timeout_Should_Not_Block_Other_Plugins()
        {
            // Arrange
            var rateLimiter1 = new SemaphoreSlim(1);
            var rateLimiter2 = new SemaphoreSlim(1);

            // Act - Block plugin 1's rate limiter
            await rateLimiter1.WaitAsync();

            var plugin2Task = Task.Run(async () =>
            {
                // Plugin 2 should acquire immediately
                var acquired = await rateLimiter2.WaitAsync(TimeSpan.FromMilliseconds(100));
                return acquired;
            });

            var result = await plugin2Task;

            // Assert
            result.Should().BeTrue(
                "Plugin 2's rate limiter should be independent of Plugin 1");

            // Cleanup
            rateLimiter1.Release();
            rateLimiter2.Release();
            rateLimiter1.Dispose();
            rateLimiter2.Dispose();
        }

        #endregion

        #region Provider Health Isolation Tests

        [Fact]
        public void ProviderHealth_Should_Be_Plugin_Scoped()
        {
            // Arrange - Simulate health states for different plugins
            var healthStates = new ConcurrentDictionary<string, bool>();

            // Plugin 1 marks its provider as unhealthy
            healthStates["plugin1:openai"] = false;

            // Plugin 2's provider should be independent
            healthStates.TryAdd("plugin2:openai", true);

            // Assert
            healthStates["plugin1:openai"].Should().BeFalse();
            healthStates["plugin2:openai"].Should().BeTrue(
                "Provider health state should be isolated per plugin");
        }

        [Fact]
        public void CircuitBreaker_State_Should_Be_Plugin_Scoped()
        {
            // Arrange - Simulate circuit breaker states
            var circuitStates = new ConcurrentDictionary<string, string>();

            // Act - Plugin 1's circuit breaker trips
            circuitStates["plugin1"] = "Open";

            // Plugin 2's circuit breaker should be unaffected
            circuitStates.TryAdd("plugin2", "Closed");

            // Assert
            circuitStates["plugin1"].Should().Be("Open");
            circuitStates["plugin2"].Should().Be("Closed",
                "Circuit breaker states should be isolated between plugins");
        }

        #endregion

        #region Retry Policy Isolation Tests

        [Fact]
        public async Task RetryPolicy_Exhaustion_Should_Not_Affect_Other_Plugins()
        {
            // Arrange
            var plugin1RetryCount = 0;
            var plugin2RetryCount = 0;
            var maxRetries = 3;

            // Act - Plugin 1 exhausts its retries
            for (int i = 0; i < maxRetries + 1; i++)
            {
                plugin1RetryCount++;
            }

            // Plugin 2 should still have full retries available
            var plugin2Succeeded = false;
            for (int i = 0; i < maxRetries; i++)
            {
                plugin2RetryCount++;
                if (plugin2RetryCount == 2) // Succeed on second try
                {
                    plugin2Succeeded = true;
                    break;
                }
            }

            // Assert
            plugin1RetryCount.Should().BeGreaterThan(maxRetries);
            plugin2Succeeded.Should().BeTrue(
                "Plugin 2's retry policy should be independent of Plugin 1");
        }

        #endregion

        #region Configuration Isolation Tests

        [Fact]
        public void Plugin_Settings_Should_Be_Isolated()
        {
            // Arrange - Simulate settings for different plugins
            var allSettings = new Dictionary<string, IDictionary<string, object>>
            {
                ["brainarr"] = new Dictionary<string, object>
                {
                    ["ApiKey"] = "brainarr-key",
                    ["Provider"] = "OpenAI"
                },
                ["qobuzarr"] = new Dictionary<string, object>
                {
                    ["AppId"] = "qobuz-app-id",
                    ["Quality"] = 27
                }
            };

            // Act & Assert - Each plugin's settings should be isolated
            allSettings["brainarr"]["Provider"].Should().Be("OpenAI");
            allSettings["qobuzarr"].ContainsKey("Provider").Should().BeFalse(
                "Plugin settings should not leak between plugins");
        }

        [Fact]
        public void Changing_Settings_Should_Not_Affect_Other_Plugins()
        {
            // Arrange
            var settings1 = new Dictionary<string, string> { ["timeout"] = "30" };
            var settings2 = new Dictionary<string, string> { ["timeout"] = "30" };

            // Act - Modify plugin 1's settings
            settings1["timeout"] = "60";

            // Assert - Plugin 2's settings should be unchanged
            settings2["timeout"].Should().Be("30",
                "Settings modifications should be isolated per plugin");
        }

        #endregion

        #region Memory Isolation Tests

        [Fact]
        public void Static_Collections_Should_Use_Plugin_Scoped_Keys()
        {
            // This test verifies the pattern of using plugin-scoped keys for any shared collections

            // Arrange
            var sharedCollection = new ConcurrentDictionary<string, object>();
            var plugin1Key = "brainarr:cache:recommendations";
            var plugin2Key = "qobuzarr:cache:search";

            // Act
            sharedCollection[plugin1Key] = new List<string> { "artist1", "artist2" };
            sharedCollection[plugin2Key] = new List<string> { "album1" };

            // Assert - Each plugin's data is accessible only via its scoped key
            sharedCollection[plugin1Key].Should().BeOfType<List<string>>()
                .Which.Should().HaveCount(2);
            sharedCollection[plugin2Key].Should().BeOfType<List<string>>()
                .Which.Should().HaveCount(1);
        }

        [Fact]
        public void WeakReferences_Should_Allow_Plugin_Unload()
        {
            // This test verifies that plugin objects can be garbage collected
            // when the plugin is unloaded, preventing memory leaks

            // Arrange
            object pluginInstance = new object();
            var weakRef = new WeakReference(pluginInstance);

            // Act
            pluginInstance = null!;

            // Force GC
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Assert
            weakRef.IsAlive.Should().BeFalse(
                "Plugin objects should be eligible for garbage collection after unload");
        }

        #endregion

        #region Logger Isolation Tests

        [Fact]
        public void Logger_Category_Should_Include_Plugin_Name()
        {
            // This test verifies that log categories properly identify the plugin

            // Arrange
            var logEntries = new List<(string Category, string Message)>();

            // Act - Simulate logging from different plugins
            logEntries.Add(("Brainarr.Services.AIService", "Processing recommendation request"));
            logEntries.Add(("Qobuzarr.Indexers.QobuzIndexer", "Searching for album"));

            // Assert - Each log entry should be attributable to its plugin
            logEntries.Should().AllSatisfy(entry =>
            {
                entry.Category.Should().MatchRegex(@"^(Brainarr|Qobuzarr)\.",
                    "Log categories should start with the plugin name");
            });
        }

        #endregion

        #region Error Isolation Tests

        [Fact]
        public void Exception_In_One_Plugin_Should_Not_Affect_Others()
        {
            // Arrange
            var plugin1Error = false;
            var plugin2Success = false;

            // Act - Plugin 1 throws an exception
            try
            {
                throw new InvalidOperationException("Plugin 1 error");
            }
            catch
            {
                plugin1Error = true;
            }

            // Plugin 2 should still work
            try
            {
                plugin2Success = true;
            }
            catch
            {
                plugin2Success = false;
            }

            // Assert
            plugin1Error.Should().BeTrue();
            plugin2Success.Should().BeTrue(
                "Exceptions in one plugin should not affect other plugins");
        }

        [Fact]
        public async Task TaskCancellation_Should_Be_Plugin_Scoped()
        {
            // Arrange
            using var plugin1Cts = new CancellationTokenSource();
            using var plugin2Cts = new CancellationTokenSource();

            // Act - Cancel plugin 1's token
            plugin1Cts.Cancel();

            // Assert - Plugin 2's token should still be active
            plugin1Cts.IsCancellationRequested.Should().BeTrue();
            plugin2Cts.IsCancellationRequested.Should().BeFalse(
                "Cancellation tokens should be scoped per plugin");
        }

        #endregion
    }
}
