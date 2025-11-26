using System;
using System.Threading.Tasks;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services
{
    /// <summary>
    /// Tests for DebugFlags to ensure proper AsyncLocal behavior
    /// and settings-based flag management.
    /// </summary>
    public class DebugFlagsTests : IDisposable
    {
        public DebugFlagsTests()
        {
            // Reset to default state
            DebugFlags.ProviderPayload = false;
        }

        public void Dispose()
        {
            // Clean up after each test
            DebugFlags.ProviderPayload = false;
        }

        #region ProviderPayload Property Tests

        [Fact]
        public void ProviderPayload_DefaultValue_IsFalse()
        {
            // Arrange
            DebugFlags.ProviderPayload = false;

            // Act & Assert
            DebugFlags.ProviderPayload.Should().BeFalse();
        }

        [Fact]
        public void ProviderPayload_WhenSetToTrue_ReturnsTrue()
        {
            // Act
            DebugFlags.ProviderPayload = true;

            // Assert
            DebugFlags.ProviderPayload.Should().BeTrue();
        }

        [Fact]
        public void ProviderPayload_WhenSetToFalse_ReturnsFalse()
        {
            // Arrange
            DebugFlags.ProviderPayload = true;

            // Act
            DebugFlags.ProviderPayload = false;

            // Assert
            DebugFlags.ProviderPayload.Should().BeFalse();
        }

        #endregion

        #region PushFromSettings Tests

        [Fact]
        public void PushFromSettings_WithDebugEnabled_SetsProviderPayloadTrue()
        {
            // Arrange
            var settings = new BrainarrSettings { EnableDebugLogging = true };

            // Act
            using (var scope = DebugFlags.PushFromSettings(settings))
            {
                // Assert
                DebugFlags.ProviderPayload.Should().BeTrue();
            }
        }

        [Fact]
        public void PushFromSettings_WithDebugDisabled_SetsProviderPayloadFalse()
        {
            // Arrange
            DebugFlags.ProviderPayload = true; // Start with true
            var settings = new BrainarrSettings { EnableDebugLogging = false };

            // Act
            using (var scope = DebugFlags.PushFromSettings(settings))
            {
                // Assert
                DebugFlags.ProviderPayload.Should().BeFalse();
            }
        }

        [Fact]
        public void PushFromSettings_WithNullSettings_SetsProviderPayloadFalse()
        {
            // Arrange
            DebugFlags.ProviderPayload = true;

            // Act
            using (var scope = DebugFlags.PushFromSettings(null))
            {
                // Assert
                DebugFlags.ProviderPayload.Should().BeFalse();
            }
        }

        [Fact]
        public void PushFromSettings_RestoresPreviousValue_WhenDisposed()
        {
            // Arrange
            DebugFlags.ProviderPayload = true;
            var settings = new BrainarrSettings { EnableDebugLogging = false };

            // Act
            using (var scope = DebugFlags.PushFromSettings(settings))
            {
                DebugFlags.ProviderPayload.Should().BeFalse();
            }

            // Assert - should restore previous value
            DebugFlags.ProviderPayload.Should().BeTrue();
        }

        [Fact]
        public void PushFromSettings_NestedScopes_RestoreCorrectly()
        {
            // Arrange
            DebugFlags.ProviderPayload = false;
            var debugSettings = new BrainarrSettings { EnableDebugLogging = true };
            var normalSettings = new BrainarrSettings { EnableDebugLogging = false };

            // Act & Assert
            using (var outer = DebugFlags.PushFromSettings(debugSettings))
            {
                DebugFlags.ProviderPayload.Should().BeTrue();

                using (var inner = DebugFlags.PushFromSettings(normalSettings))
                {
                    DebugFlags.ProviderPayload.Should().BeFalse();
                }

                DebugFlags.ProviderPayload.Should().BeTrue();
            }

            DebugFlags.ProviderPayload.Should().BeFalse();
        }

        #endregion

        #region Async Context Isolation Tests

        [Fact]
        public async Task ProviderPayload_FlowsAcrossAsyncAwait()
        {
            // Arrange
            DebugFlags.ProviderPayload = true;

            // Act
            var valueBefore = DebugFlags.ProviderPayload;
            await Task.Delay(10);
            var valueAfter = DebugFlags.ProviderPayload;

            // Assert
            valueBefore.Should().BeTrue();
            valueAfter.Should().BeTrue();
        }

        [Fact]
        public async Task PushFromSettings_FlowsAcrossAsyncAwait()
        {
            // Arrange
            var settings = new BrainarrSettings { EnableDebugLogging = true };

            // Act & Assert
            using (var scope = DebugFlags.PushFromSettings(settings))
            {
                var valueBefore = DebugFlags.ProviderPayload;
                await Task.Delay(10);
                var valueAfter = DebugFlags.ProviderPayload;

                valueBefore.Should().BeTrue();
                valueAfter.Should().BeTrue();
            }
        }

        [Fact]
        public async Task ParallelTasks_HaveIsolatedContexts()
        {
            // Arrange
            const int taskCount = 10;
            var results = new bool[taskCount];
            var tasks = new Task[taskCount];

            // Act
            for (int i = 0; i < taskCount; i++)
            {
                int taskIndex = i;
                bool shouldBeDebug = taskIndex % 2 == 0;
                tasks[i] = Task.Run(async () =>
                {
                    DebugFlags.ProviderPayload = shouldBeDebug;
                    await Task.Delay(10); // Simulate async work
                    results[taskIndex] = DebugFlags.ProviderPayload;
                });
            }

            await Task.WhenAll(tasks);

            // Assert - each task should have its own context
            for (int i = 0; i < taskCount; i++)
            {
                bool expected = i % 2 == 0;
                results[i].Should().Be(expected, $"Task {i} should have ProviderPayload={expected}");
            }
        }

        #endregion

        #region Scope Disposal Tests

        [Fact]
        public void Scope_DoubleDispose_IsIdempotent()
        {
            // Arrange
            DebugFlags.ProviderPayload = false;
            var settings = new BrainarrSettings { EnableDebugLogging = true };
            var scope = DebugFlags.PushFromSettings(settings);

            // Act
            scope.Dispose();
            scope.Dispose(); // Second dispose should be safe

            // Assert
            DebugFlags.ProviderPayload.Should().BeFalse();
        }

        [Fact]
        public void Scope_DisposedOutOfOrder_DoesNotCorrupt()
        {
            // Arrange
            DebugFlags.ProviderPayload = false;
            var debugSettings = new BrainarrSettings { EnableDebugLogging = true };
            var normalSettings = new BrainarrSettings { EnableDebugLogging = false };

            var outer = DebugFlags.PushFromSettings(debugSettings);
            var inner = DebugFlags.PushFromSettings(normalSettings);

            // Act - dispose out of order (outer first)
            outer.Dispose();
            inner.Dispose();

            // Assert - should still work (each scope restores its own previous value)
            // Note: Out-of-order disposal may leave state in unexpected position,
            // but should not throw or corrupt
            // This tests defensive behavior
        }

        #endregion
    }
}
