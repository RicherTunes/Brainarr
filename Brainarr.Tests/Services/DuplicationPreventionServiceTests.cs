using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Brainarr.Tests.Helpers;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.Services
{
    [Trait("Category", "Unit")]
    public class DuplicationPreventionServiceTests : IDisposable
    {
        private readonly Logger _logger;
        private readonly DuplicationPreventionService _service;

        public DuplicationPreventionServiceTests()
        {
            _logger = TestLogger.CreateNullLogger();
            _service = new DuplicationPreventionService(_logger);
        }

        public void Dispose()
        {
            _service?.Dispose();
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidLogger_InitializesSuccessfully()
        {
            // Arrange & Act
            var service = new DuplicationPreventionService(_logger);

            // Assert
            service.Should().NotBeNull();

            // Cleanup
            service.Dispose();
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new DuplicationPreventionService(null));
        }

        #endregion

        #region DeduplicateRecommendations Tests

        [Fact]
        public void DeduplicateRecommendations_WithNullList_ReturnsEmptyList()
        {
            // Act
            var result = _service.DeduplicateRecommendations(null);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public void DeduplicateRecommendations_WithEmptyList_ReturnsEmptyList()
        {
            // Arrange
            var recommendations = new List<ImportListItemInfo>();

            // Act
            var result = _service.DeduplicateRecommendations(recommendations);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public void DeduplicateRecommendations_WithDuplicates_RemovesDuplicates()
        {
            // Arrange
            var recommendations = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "The Wall" },
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "The Wall" },
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "The Wall" },
                new ImportListItemInfo { Artist = "Led Zeppelin", Album = "IV" },
                new ImportListItemInfo { Artist = "Led Zeppelin", Album = "IV" },
                new ImportListItemInfo { Artist = "The Beatles", Album = "Abbey Road" }
            };

            // Act
            var result = _service.DeduplicateRecommendations(recommendations);

            // Assert
            result.Should().HaveCount(3);
            result.Select(r => $"{r.Artist}|{r.Album}").Should().BeEquivalentTo(
                "Pink Floyd|The Wall",
                "Led Zeppelin|IV",
                "The Beatles|Abbey Road"
            );
        }

        [Fact]
        public void DeduplicateRecommendations_WithCaseDifferences_NormalizesCasing()
        {
            // Arrange
            var recommendations = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "The Wall" },
                new ImportListItemInfo { Artist = "PINK FLOYD", Album = "THE WALL" },
                new ImportListItemInfo { Artist = "pink floyd", Album = "the wall" },
                new ImportListItemInfo { Artist = "The Beatles", Album = "Abbey Road" }
            };

            // Act
            var result = _service.DeduplicateRecommendations(recommendations);

            // Assert
            result.Should().HaveCount(2);
            result[0].Artist.Should().Be("Pink Floyd"); // First occurrence preserved
            result[1].Artist.Should().Be("The Beatles");
        }

        [Fact]
        public void DeduplicateRecommendations_WithWhitespaceVariations_NormalizesWhitespace()
        {
            // Arrange
            var recommendations = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "The Wall" },
                new ImportListItemInfo { Artist = "  Pink   Floyd  ", Album = "  The   Wall  " },
                new ImportListItemInfo { Artist = "Pink\tFloyd", Album = "The\t\tWall" },
                new ImportListItemInfo { Artist = "The Beatles", Album = "Abbey Road" }
            };

            // Act
            var result = _service.DeduplicateRecommendations(recommendations);

            // Assert
            result.Should().HaveCount(2);
        }

        [Fact]
        public void DeduplicateRecommendations_DecodesHtmlEntities()
        {
            var recommendations = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "AC/DC &amp; Friends", Album = "Best Of" },
                new ImportListItemInfo { Artist = "AC/DC & Friends", Album = "Best Of" }
            };

            var result = _service.DeduplicateRecommendations(recommendations);

            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("AC/DC & Friends");
        }

        [Fact]
        public void DeduplicateRecommendations_WithNullOrEmptyFields_HandlesGracefully()
        {
            // Arrange
            var recommendations = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "The Wall" },
                new ImportListItemInfo { Artist = null, Album = "Some Album" },
                new ImportListItemInfo { Artist = "", Album = "Another Album" },
                new ImportListItemInfo { Artist = "Some Artist", Album = null },
                new ImportListItemInfo { Artist = "Some Artist", Album = "" },
                new ImportListItemInfo { Artist = null, Album = null }
            };

            // Act
            var result = _service.DeduplicateRecommendations(recommendations);

            // Assert
            result.Should().HaveCount(5); // All are considered unique due to different null/empty combinations
            result.Should().Contain(r => r.Artist == "Pink Floyd" && r.Album == "The Wall");
        }

        [Fact]
        public void DeduplicateRecommendations_PreservesOriginalOrder()
        {
            // Arrange
            var recommendations = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Artist C", Album = "Album C" },
                new ImportListItemInfo { Artist = "Artist A", Album = "Album A" },
                new ImportListItemInfo { Artist = "Artist B", Album = "Album B" },
                new ImportListItemInfo { Artist = "Artist A", Album = "Album A" } // Duplicate
            };

            // Act
            var result = _service.DeduplicateRecommendations(recommendations);

            // Assert
            result.Should().HaveCount(3);
            result[0].Artist.Should().Be("Artist C");
            result[1].Artist.Should().Be("Artist A");
            result[2].Artist.Should().Be("Artist B");
        }

        #endregion

        #region FilterPreviouslyRecommended Tests

        [Fact]
        public void FilterPreviouslyRecommended_WithNullList_ReturnsEmptyList()
        {
            // Act
            var result = _service.FilterPreviouslyRecommended(null);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public void FilterPreviouslyRecommended_WithEmptyList_ReturnsEmptyList()
        {
            // Arrange
            var recommendations = new List<ImportListItemInfo>();

            // Act
            var result = _service.FilterPreviouslyRecommended(recommendations);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public void FilterPreviouslyRecommended_WithNewItems_ReturnsAllItems()
        {
            // Arrange
            var recommendations = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "The Wall" },
                new ImportListItemInfo { Artist = "The Beatles", Album = "Abbey Road" }
            };

            // Act
            var result = _service.FilterPreviouslyRecommended(recommendations);

            // Assert
            result.Should().HaveCount(2);
            result.Should().BeEquivalentTo(recommendations);
        }

        [Fact]
        public void FilterPreviouslyRecommended_WithPreviouslyRecommended_FiltersCorrectly()
        {
            // Arrange
            var firstBatch = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "The Wall" },
                new ImportListItemInfo { Artist = "The Beatles", Album = "Abbey Road" }
            };

            var secondBatch = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "The Wall" }, // Already recommended
                new ImportListItemInfo { Artist = "Led Zeppelin", Album = "IV" }, // New
                new ImportListItemInfo { Artist = "The Beatles", Album = "Abbey Road" } // Already recommended
            };

            // Act
            _service.DeduplicateRecommendations(firstBatch); // Prime the history (DeduplicateRecommendations adds to history)
            var result = _service.FilterPreviouslyRecommended(secondBatch);

            // Assert
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Led Zeppelin");
            result[0].Album.Should().Be("IV");
        }
        [Fact]
        public void FilterPreviouslyRecommended_AllowsSessionAllowListEntries()
        {
            var firstBatch = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Muse", Album = "Origin of Symmetry" }
            };
            _service.DeduplicateRecommendations(firstBatch);

            var secondBatch = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Muse", Album = "Origin of Symmetry" }
            };

            var sessionAllowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "muse|origin of symmetry"
            };

            var result = _service.FilterPreviouslyRecommended(secondBatch, sessionAllowList);

            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Muse");
            result[0].Album.Should().Be("Origin of Symmetry");
        }


        [Fact]
        public void FilterPreviouslyRecommended_CaseInsensitive_FiltersCorrectly()
        {
            // Arrange
            var firstBatch = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "The Wall" }
            };

            var secondBatch = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "PINK FLOYD", Album = "THE WALL" }, // Same but different case
                new ImportListItemInfo { Artist = "Led Zeppelin", Album = "IV" }
            };

            // Act
            _service.DeduplicateRecommendations(firstBatch); // Prime the history (DeduplicateRecommendations adds to history)
            var result = _service.FilterPreviouslyRecommended(secondBatch);

            // Assert
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Led Zeppelin");
        }

        #endregion

        #region ClearHistory Tests

        [Fact]
        public void ClearHistory_AfterRecommendations_ResetsHistory()
        {
            // Arrange
            var recommendations = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "The Wall" },
                new ImportListItemInfo { Artist = "The Beatles", Album = "Abbey Road" }
            };

            _service.FilterPreviouslyRecommended(recommendations);

            // Act
            _service.ClearHistory();

            // Assert - Same items should not be filtered after clearing history
            var result = _service.FilterPreviouslyRecommended(recommendations);
            result.Should().HaveCount(2);
            result.Should().BeEquivalentTo(recommendations);
        }

        [Fact]
        public void ClearHistory_WithEmptyHistory_DoesNotThrow()
        {
            // Act & Assert
            _service.Invoking(s => s.ClearHistory()).Should().NotThrow();
        }

        #endregion

        #region PreventConcurrentFetch Tests

        [Fact]
        public async Task PreventConcurrentFetch_WithValidOperation_ExecutesSuccessfully()
        {
            // Arrange
            const string operationKey = "test-operation";
            var executionCount = 0;

            // Act
            var result = await _service.PreventConcurrentFetch(operationKey, async () =>
            {
                executionCount++;
                await Task.Delay(10);
                return "success";
            });

            // Assert
            result.Should().Be("success");
            executionCount.Should().Be(1);
        }

        [Fact]
        public async Task PreventConcurrentFetch_WithConcurrentCalls_SerializesExecution()
        {
            // Arrange
            const string operationKey = "test-operation";
            var executionOrder = new List<int>();
            var executionCount = 0;

            // Act
            var tasks = Enumerable.Range(1, 3).Select(i =>
                _service.PreventConcurrentFetch(operationKey, async () =>
                {
                    var myNumber = Interlocked.Increment(ref executionCount);
                    await Task.Delay(10);

                    lock (executionOrder)
                    {
                        executionOrder.Add(myNumber);
                    }

                    return myNumber;
                })
            ).ToArray();

            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().HaveCount(3);
            executionOrder.Should().BeInAscendingOrder(); // Should execute sequentially
        }
        [Fact]
        public async Task PreventConcurrentFetch_WithDifferentKeys_AllowsConcurrentExecution()
        {
            // Use a TCS gate instead of Barrier to avoid testhost instability on Windows runners
            var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var running = 0;
            var observedMax = 0;

            async Task<string> ExecuteAsync(string payload)
            {
                await startGate.Task; // ensure both tasks start together
                var inflight = Interlocked.Increment(ref running);
                UpdateMax(ref observedMax, inflight);

                try
                {
                    await Task.Delay(150);
                    return payload;
                }
                finally
                {
                    Interlocked.Decrement(ref running);
                }
            }

            var first = _service.PreventConcurrentFetch("key1", () => ExecuteAsync("result1"));
            var second = _service.PreventConcurrentFetch("key2", () => ExecuteAsync("result2"));

            // Release both operations to run concurrently
            startGate.SetResult();

            var results = await Task.WhenAll(first, second);

            results.Should().BeEquivalentTo("result1", "result2");
            observedMax.Should().BeGreaterThanOrEqualTo(2, "different keys should run in parallel");
        }

        [Fact]
        public async Task PreventConcurrentFetch_WithSameKey_DeduplicatesConcurrentCalls()
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var invocationCount = 0;

            async Task<string> ExecuteAsync()
            {
                Interlocked.Increment(ref invocationCount);
                started.TrySetResult(true);
                await gate.Task;
                return "result";
            }

            var first = _service.PreventConcurrentFetch("shared", ExecuteAsync);
            await started.Task;
            var second = _service.PreventConcurrentFetch("shared", ExecuteAsync);

            gate.SetResult(true);

            var results = await Task.WhenAll(first, second);

            results.Should().BeEquivalentTo("result", "result");
            Volatile.Read(ref invocationCount).Should().Be(1);
        }
        [Fact]
        public async Task PreventConcurrentFetch_WithThrottling_DelaysRapidCalls()
        {
            // Arrange
            const string operationKey = "throttled-operation";
            var executionTimes = new List<DateTime>();

            // Act
            var results = await Task.WhenAll(
                _service.PreventConcurrentFetch(operationKey, async () =>
                {
                    executionTimes.Add(DateTime.UtcNow);
                    return "first";
                }),
                Task.Delay(10).ContinueWith(_ => _service.PreventConcurrentFetch(operationKey, async () =>
                {
                    executionTimes.Add(DateTime.UtcNow);
                    return "second";
                })).Unwrap()
            );

            // Assert
            results.Should().BeEquivalentTo("first", "second");
            executionTimes.Should().HaveCount(2);

            var timeDifference = executionTimes[1] - executionTimes[0];
            timeDifference.Should().BeGreaterThan(TimeSpan.FromSeconds(4.5)); // Should be throttled
        }

        [Fact]
        public async Task PreventConcurrentFetch_WhenOperationThrows_PropagatesException()
        {
            // Arrange
            const string operationKey = "failing-operation";
            var expectedException = new InvalidOperationException("Test exception");

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.PreventConcurrentFetch(operationKey, () => Task.FromException<string>(expectedException))
            );

            exception.Should().BeSameAs(expectedException);
        }

        [Fact]
        public async Task PreventConcurrentFetch_AfterDispose_ThrowsObjectDisposedException()
        {
            // Arrange
            var disposableService = new DuplicationPreventionService(_logger);
            disposableService.Dispose();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                disposableService.PreventConcurrentFetch("key", () => Task.FromResult("result"))
            );
        }

        #endregion

        #region Integration Tests

        [Fact]
        public void DeduplicateAndFilter_Integration_WorksCorrectly()
        {
            // Arrange - First batch with duplicates
            var firstBatch = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "The Wall" },
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "The Wall" }, // Duplicate
                new ImportListItemInfo { Artist = "The Beatles", Album = "Abbey Road" }
            };

            // Second batch with some overlaps
            var secondBatch = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "The Wall" }, // Already seen
                new ImportListItemInfo { Artist = "Led Zeppelin", Album = "IV" },
                new ImportListItemInfo { Artist = "Led Zeppelin", Album = "IV" }, // Duplicate
                new ImportListItemInfo { Artist = "Queen", Album = "Bohemian Rhapsody" }
            };

            // Act
            var firstResult = _service.DeduplicateRecommendations(firstBatch);
            // For the second batch, first filter against history, THEN deduplicate
            var filteredSecond = _service.FilterPreviouslyRecommended(secondBatch);
            var secondResult = _service.DeduplicateRecommendations(filteredSecond);

            // Assert
            firstResult.Should().HaveCount(2); // Duplicates removed from first batch
            filteredSecond.Should().HaveCount(3); // Pink Floyd filtered out as already recommended
            secondResult.Should().HaveCount(2); // After deduplication: Led Zeppelin and Queen

            secondResult.Should().Contain(r => r.Artist == "Led Zeppelin");
            secondResult.Should().Contain(r => r.Artist == "Queen");
            secondResult.Should().NotContain(r => r.Artist == "Pink Floyd");
        }

        #endregion

        #region Thread Safety Tests

        [Fact]
        public async Task ConcurrentDeduplication_ThreadSafe()
        {
            // Arrange
            const int taskCount = 10;
            const int itemsPerTask = 20;
            var allResults = new List<ImportListItemInfo>[taskCount];

            // Act - Run deduplication concurrently
            var tasks = Enumerable.Range(0, taskCount).Select(async taskIndex =>
            {
                var recommendations = Enumerable.Range(0, itemsPerTask).Select(i =>
                    new ImportListItemInfo
                    {
                        Artist = $"Artist {i % 5}", // Create some duplicates
                        Album = $"Album {i % 3}"
                    }).ToList();

                var result = _service.DeduplicateRecommendations(recommendations);
                allResults[taskIndex] = result;
            });

            await Task.WhenAll(tasks);

            // Assert - All tasks should complete without error
            allResults.Should().NotContain(r => r == null);
            foreach (var result in allResults)
            {
                result.Should().NotBeEmpty();
                // Each result should be deduplicated
                result.Select(r => $"{r.Artist}|{r.Album}").Should().OnlyHaveUniqueItems();
            }
        }

        [Fact]
        public async Task ConcurrentFiltering_ThreadSafe()
        {
            // Arrange
            const int taskCount = 10;
            var allResults = new List<ImportListItemInfo>[taskCount];

            // Act - Run filtering concurrently
            var tasks = Enumerable.Range(0, taskCount).Select(async taskIndex =>
            {
                var recommendations = new List<ImportListItemInfo>
                {
                    new ImportListItemInfo { Artist = $"Artist {taskIndex}", Album = $"Album {taskIndex}" }
                };

                var result = _service.FilterPreviouslyRecommended(recommendations);
                allResults[taskIndex] = result;
                await Task.Delay(1); // Small delay to increase concurrency chances
            });

            await Task.WhenAll(tasks);

            // Assert - All tasks should complete without error
            allResults.Should().NotContain(r => r == null);

            // Each task should have gotten their unique item on first call
            foreach (var result in allResults)
            {
                result.Should().HaveCount(1);
            }
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            // Act & Assert
            _service.Invoking(s => s.Dispose()).Should().NotThrow();
            _service.Invoking(s => s.Dispose()).Should().NotThrow();
        }

        [Fact]
        public void Dispose_ClearsAllResources()
        {
            // Arrange
            var disposableService = new DuplicationPreventionService(_logger);

            // Add some data to track
            var recommendations = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Test Artist", Album = "Test Album" }
            };
            disposableService.FilterPreviouslyRecommended(recommendations);

            // Act
            disposableService.Dispose();

            // Assert - Should throw when trying to use after dispose
            Assert.Throws<ObjectDisposedException>(() =>
                disposableService.DeduplicateRecommendations(recommendations)
            );
        }


        private static void UpdateMax(ref int target, int candidate)
        {
            while (true)
            {
                var snapshot = Volatile.Read(ref target);
                if (candidate <= snapshot)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref target, candidate, snapshot) == snapshot)
                {
                    return;
                }
            }
        }

        #endregion
    }
}
