using System;
using System.Threading.Tasks;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services
{
    /// <summary>
    /// Comprehensive tests for CorrelationContext to ensure proper
    /// correlation ID management across async boundaries.
    /// </summary>
    public class CorrelationContextTests : IDisposable
    {
        public CorrelationContextTests()
        {
            // Start with a clean context
            CorrelationContext.Clear();
        }

        public void Dispose()
        {
            // Clean up after each test
            CorrelationContext.Clear();
        }

        #region Current Property Tests

        [Fact]
        public void Current_WhenNotSet_GeneratesNewId()
        {
            // Arrange
            CorrelationContext.Clear();

            // Act
            var id = CorrelationContext.Current;

            // Assert
            id.Should().NotBeNullOrEmpty();
            id.Should().Contain("_"); // Format: timestamp_randomhex
        }

        [Fact]
        public void Current_WhenSet_ReturnsSameValue()
        {
            // Arrange
            const string testId = "test-correlation-id";
            CorrelationContext.Current = testId;

            // Act
            var result = CorrelationContext.Current;

            // Assert
            result.Should().Be(testId);
        }

        [Fact]
        public void Current_CalledMultipleTimes_ReturnsSameId()
        {
            // Arrange
            CorrelationContext.Clear();

            // Act
            var first = CorrelationContext.Current;
            var second = CorrelationContext.Current;
            var third = CorrelationContext.Current;

            // Assert
            first.Should().Be(second);
            second.Should().Be(third);
        }

        #endregion

        #region StartNew Tests

        [Fact]
        public void StartNew_GeneratesNewId()
        {
            // Arrange
            var originalId = CorrelationContext.Current;

            // Act
            var newId = CorrelationContext.StartNew();

            // Assert
            newId.Should().NotBeNullOrEmpty();
            newId.Should().NotBe(originalId);
            CorrelationContext.Current.Should().Be(newId);
        }

        [Fact]
        public void StartNew_MultipleCallsGenerateUniqueIds()
        {
            // Act
            var id1 = CorrelationContext.StartNew();
            var id2 = CorrelationContext.StartNew();
            var id3 = CorrelationContext.StartNew();

            // Assert
            id1.Should().NotBe(id2);
            id2.Should().NotBe(id3);
            id1.Should().NotBe(id3);
        }

        #endregion

        #region BeginScope Tests

        [Fact]
        public void BeginScope_CreatesNewIdByDefault()
        {
            // Arrange
            var originalId = CorrelationContext.Current;

            // Act
            using (var scope = CorrelationContext.BeginScope())
            {
                // Assert inside scope
                CorrelationContext.Current.Should().NotBe(originalId);
            }

            // Assert after scope - original should be restored
            CorrelationContext.Current.Should().Be(originalId);
        }

        [Fact]
        public void BeginScope_WithProvidedId_UsesThatId()
        {
            // Arrange
            const string customId = "custom-scope-id";
            var originalId = CorrelationContext.Current;

            // Act
            using (var scope = CorrelationContext.BeginScope(customId))
            {
                // Assert inside scope
                CorrelationContext.Current.Should().Be(customId);
            }

            // Assert after scope
            CorrelationContext.Current.Should().Be(originalId);
        }

        [Fact]
        public void BeginScope_NestedScopes_RestoreCorrectly()
        {
            // Arrange
            var originalId = CorrelationContext.Current;

            // Act & Assert
            using (var scope1 = CorrelationContext.BeginScope("scope1"))
            {
                CorrelationContext.Current.Should().Be("scope1");

                using (var scope2 = CorrelationContext.BeginScope("scope2"))
                {
                    CorrelationContext.Current.Should().Be("scope2");

                    using (var scope3 = CorrelationContext.BeginScope("scope3"))
                    {
                        CorrelationContext.Current.Should().Be("scope3");
                    }

                    CorrelationContext.Current.Should().Be("scope2");
                }

                CorrelationContext.Current.Should().Be("scope1");
            }

            CorrelationContext.Current.Should().Be(originalId);
        }

        #endregion

        #region Clear Tests

        [Fact]
        public void Clear_RemovesCurrentId()
        {
            // Arrange
            CorrelationContext.Current = "test-id";

            // Act
            CorrelationContext.Clear();

            // Assert
            CorrelationContext.HasCurrent.Should().BeFalse();
        }

        [Fact]
        public void Clear_AfterClear_CurrentGeneratesNewId()
        {
            // Arrange
            var firstId = CorrelationContext.Current;
            CorrelationContext.Clear();

            // Act
            var newId = CorrelationContext.Current;

            // Assert
            newId.Should().NotBe(firstId);
        }

        #endregion

        #region TryPeek Tests

        [Fact]
        public void TryPeek_WhenIdExists_ReturnsTrue()
        {
            // Arrange
            CorrelationContext.Current = "peek-test-id";

            // Act
            var result = CorrelationContext.TryPeek(out var id);

            // Assert
            result.Should().BeTrue();
            id.Should().Be("peek-test-id");
        }

        [Fact]
        public void TryPeek_WhenNoId_ReturnsFalse()
        {
            // Arrange
            CorrelationContext.Clear();

            // Act
            var result = CorrelationContext.TryPeek(out var id);

            // Assert
            result.Should().BeFalse();
            id.Should().BeNull();
        }

        [Fact]
        public void TryPeek_DoesNotCreateNewId()
        {
            // Arrange
            CorrelationContext.Clear();

            // Act
            CorrelationContext.TryPeek(out _);

            // Assert
            CorrelationContext.HasCurrent.Should().BeFalse();
        }

        #endregion

        #region HasCurrent Tests

        [Fact]
        public void HasCurrent_WhenIdSet_ReturnsTrue()
        {
            // Arrange
            CorrelationContext.Current = "has-current-test";

            // Act & Assert
            CorrelationContext.HasCurrent.Should().BeTrue();
        }

        [Fact]
        public void HasCurrent_WhenCleared_ReturnsFalse()
        {
            // Arrange
            CorrelationContext.Clear();

            // Act & Assert
            CorrelationContext.HasCurrent.Should().BeFalse();
        }

        #endregion

        #region GenerateCorrelationId Tests

        [Fact]
        public void GenerateCorrelationId_ReturnsValidFormat()
        {
            // Act
            var id = CorrelationContext.GenerateCorrelationId();

            // Assert
            id.Should().NotBeNullOrEmpty();
            id.Should().Contain("_");

            var parts = id.Split('_');
            parts.Should().HaveCount(2);
            parts[0].Should().HaveLength(14); // yyyyMMddHHmmss
            parts[1].Should().HaveLength(8);  // 8 hex characters
        }

        [Fact]
        public void GenerateCorrelationId_MultipleCallsGenerateUniqueIds()
        {
            // Act
            var ids = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < 100; i++)
            {
                ids.Add(CorrelationContext.GenerateCorrelationId());
            }

            // Assert - all IDs should be unique
            ids.Should().HaveCount(100);
        }

        #endregion

        #region Async Context Flow Tests

        [Fact]
        public async Task Current_FlowsAcrossAsyncAwait()
        {
            // Arrange
            const string testId = "async-flow-test";
            CorrelationContext.Current = testId;

            // Act
            var idBeforeAwait = CorrelationContext.Current;
            await Task.Delay(10);
            var idAfterAwait = CorrelationContext.Current;

            // Assert
            idBeforeAwait.Should().Be(testId);
            idAfterAwait.Should().Be(testId);
        }

        [Fact]
        public async Task BeginScope_FlowsAcrossAsyncAwait()
        {
            // Arrange
            var originalId = CorrelationContext.Current;

            // Act & Assert
            using (var scope = CorrelationContext.BeginScope("async-scope"))
            {
                var idBefore = CorrelationContext.Current;
                await Task.Delay(10);
                var idAfter = CorrelationContext.Current;

                idBefore.Should().Be("async-scope");
                idAfter.Should().Be("async-scope");
            }

            CorrelationContext.Current.Should().Be(originalId);
        }

        [Fact]
        public async Task ParallelTasks_HaveIsolatedContexts()
        {
            // Arrange
            const int taskCount = 10;
            var results = new string[taskCount];
            var tasks = new Task[taskCount];

            // Act
            for (int i = 0; i < taskCount; i++)
            {
                int taskIndex = i;
                tasks[i] = Task.Run(async () =>
                {
                    CorrelationContext.Current = $"task-{taskIndex}";
                    await Task.Delay(10); // Simulate async work
                    results[taskIndex] = CorrelationContext.Current;
                });
            }

            await Task.WhenAll(tasks);

            // Assert - each task should have its own context
            for (int i = 0; i < taskCount; i++)
            {
                results[i].Should().Be($"task-{i}");
            }
        }

        #endregion
    }
}
