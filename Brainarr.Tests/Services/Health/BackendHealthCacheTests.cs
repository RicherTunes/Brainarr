using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Lidarr.Plugin.Common.Resilience;
using Brainarr.Tests.Helpers;
using Xunit;

namespace Brainarr.Tests.Services.Health
{
    /// <summary>
    /// Unit tests for BackendHealthCache and the ModelDetectionService fast-fail gate.
    /// </summary>
    [Trait("Category", "Unit")]
    [Trait("Component", "Health")]
    public class BackendHealthCacheTests
    {
        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        /// <summary>Creates a fake TimeProvider whose current time can be advanced.</summary>
        private static FakeTimeProvider MakeFake(DateTimeOffset start)
            => new FakeTimeProvider(start);

        private static Exception MakeSocketException()
        {
            // Simulate what Lidarr's IHttpClient throws when Ollama/LMStudio is not running:
            // HttpRequestException wrapping a SocketException (connection refused).
            var socket = new SocketException((int)SocketError.ConnectionRefused);
            return new HttpRequestException("Connection refused", socket);
        }

        private static Exception MakeSocketExceptionDirect()
            => new SocketException((int)SocketError.ConnectionRefused);

        // ------------------------------------------------------------------ //
        // 1. MarkDown → IsKnownDown within grace returns true
        // ------------------------------------------------------------------ //

        [Fact]
        public void IsKnownDown_AfterMarkDown_WithinGrace_ReturnsTrue()
        {
            var fake = MakeFake(DateTimeOffset.UtcNow);
            var cache = new BackendHealthCache(fake);

            cache.MarkDown("Ollama", "http://localhost:11434", MakeSocketException());

            // Advance only 1 second — well within the 30 s grace period
            fake.Advance(TimeSpan.FromSeconds(1));

            var result = cache.IsKnownDown("Ollama", "http://localhost:11434", out var reason);

            result.Should().BeTrue();
            reason.Should().NotBeNullOrEmpty();
            reason.Should().Contain("known-down");
        }

        // ------------------------------------------------------------------ //
        // 2. MarkDown → IsKnownDown after grace expired returns false
        // ------------------------------------------------------------------ //

        [Fact]
        public void IsKnownDown_AfterMarkDown_GraceExpired_ReturnsFalse()
        {
            var fake = MakeFake(DateTimeOffset.UtcNow);
            var cache = new BackendHealthCache(fake);

            cache.MarkDown("Ollama", "http://localhost:11434", MakeSocketException());

            // Advance beyond the grace period
            fake.Advance(TimeSpan.FromSeconds(BrainarrConstants.BackendDownGraceSeconds + 1));

            var result = cache.IsKnownDown("Ollama", "http://localhost:11434", out var reason);

            result.Should().BeFalse();
            reason.Should().BeNull();
        }

        // ------------------------------------------------------------------ //
        // 3. MarkUp clears the down state immediately
        // ------------------------------------------------------------------ //

        [Fact]
        public void IsKnownDown_AfterMarkUp_ReturnsFalse_RegardlessOfGrace()
        {
            var fake = MakeFake(DateTimeOffset.UtcNow);
            var cache = new BackendHealthCache(fake);

            cache.MarkDown("LMStudio", "http://localhost:1234", MakeSocketException());
            // Confirm it's down
            cache.IsKnownDown("LMStudio", "http://localhost:1234", out _).Should().BeTrue();

            // Clear with MarkUp
            cache.MarkUp("LMStudio", "http://localhost:1234");

            var result = cache.IsKnownDown("LMStudio", "http://localhost:1234", out var reason);
            result.Should().BeFalse();
            reason.Should().BeNull();
        }

        // ------------------------------------------------------------------ //
        // 4. Different (provider, url) keys are independent
        // ------------------------------------------------------------------ //

        [Fact]
        public void Keys_AreScopedByProviderAndUrl()
        {
            var fake = MakeFake(DateTimeOffset.UtcNow);
            var cache = new BackendHealthCache(fake);

            // Mark Ollama on port 11434 as down
            cache.MarkDown("Ollama", "http://localhost:11434", MakeSocketException());

            // LMStudio on a different port should NOT be affected
            cache.IsKnownDown("LMStudio", "http://localhost:1234", out _).Should().BeFalse();

            // Ollama on a different port should NOT be affected
            cache.IsKnownDown("Ollama", "http://localhost:11435", out _).Should().BeFalse();

            // The original entry IS affected
            cache.IsKnownDown("Ollama", "http://localhost:11434", out _).Should().BeTrue();
        }

        [Fact]
        public void Key_IsCaseInsensitive_ForProviderAndUrl()
        {
            var fake = MakeFake(DateTimeOffset.UtcNow);
            var cache = new BackendHealthCache(fake);

            cache.MarkDown("ollama", "http://Localhost:11434", MakeSocketException());

            // Mixed-case lookups should resolve to the same entry
            cache.IsKnownDown("Ollama", "http://localhost:11434", out _).Should().BeTrue();
            cache.IsKnownDown("OLLAMA", "HTTP://LOCALHOST:11434", out _).Should().BeTrue();
        }

        [Fact]
        public void Key_NormalizesTrailingSlash()
        {
            var fake = MakeFake(DateTimeOffset.UtcNow);
            var cache = new BackendHealthCache(fake);

            cache.MarkDown("Ollama", "http://localhost:11434/", MakeSocketException());

            cache.IsKnownDown("Ollama", "http://localhost:11434", out _).Should().BeTrue();
        }

        // ------------------------------------------------------------------ //
        // 5. Concurrent MarkDown calls from many threads don't corrupt state
        // ------------------------------------------------------------------ //

        [Fact]
        public async Task ConcurrentMarkDown_DoesNotCorruptState()
        {
            const int threads = 50;
            var fake = MakeFake(DateTimeOffset.UtcNow);
            var cache = new BackendHealthCache(fake);
            var ex = MakeSocketException();
            var barrier = new System.Threading.Barrier(threads);
            var tasks = new Task[threads];

            for (int i = 0; i < threads; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    barrier.SignalAndWait(); // all threads start at the same time
                    cache.MarkDown("Ollama", "http://localhost:11434", ex);
                });
            }

            await Task.WhenAll(tasks);

            // State should be coherent — backend is down
            cache.IsKnownDown("Ollama", "http://localhost:11434", out var reason).Should().BeTrue();
            reason.Should().NotBeNullOrEmpty();
        }

        // ------------------------------------------------------------------ //
        // IsConnectionClassFailure classification
        // ------------------------------------------------------------------ //

        [Fact]
        public void IsConnectionClassFailure_SocketException_ReturnsTrue()
        {
            var ex = new SocketException((int)SocketError.ConnectionRefused);
            BackendHealthCache.IsConnectionClassFailure(ex).Should().BeTrue();
        }

        [Fact]
        public void IsConnectionClassFailure_HttpRequestExceptionWrappingSocketException_ReturnsTrue()
        {
            var ex = MakeSocketException(); // HttpRequestException(SocketException)
            BackendHealthCache.IsConnectionClassFailure(ex).Should().BeTrue();
        }

        [Fact]
        public void IsConnectionClassFailure_TaskCanceledException_ReturnsFalse()
        {
            // Timeouts from slow-but-alive backends should NOT be counted as connection failures
            BackendHealthCache.IsConnectionClassFailure(new TaskCanceledException()).Should().BeFalse();
        }

        [Fact]
        public void IsConnectionClassFailure_GenericException_ReturnsFalse()
        {
            BackendHealthCache.IsConnectionClassFailure(new InvalidOperationException("some error")).Should().BeFalse();
        }

        [Fact]
        public void IsConnectionClassFailure_Null_ReturnsFalse()
        {
            BackendHealthCache.IsConnectionClassFailure(null).Should().BeFalse();
        }

        [Fact]
        public void MarkDown_WithNonConnectionException_DoesNotMarkDown()
        {
            var fake = MakeFake(DateTimeOffset.UtcNow);
            var cache = new BackendHealthCache(fake);

            // A generic exception should not mark the backend as down
            cache.MarkDown("Ollama", "http://localhost:11434", new Exception("some random error"));

            cache.IsKnownDown("Ollama", "http://localhost:11434", out _).Should().BeFalse();
        }

        // ------------------------------------------------------------------ //
        // 6. Integration: ModelDetectionService skips HTTP on second call
        //    when first call failed with a socket error
        // ------------------------------------------------------------------ //

        [Fact]
        public async Task GetOllamaModelsAsync_SecondCallWithinGrace_DoesNotHitHttp()
        {
            var fake = MakeFake(DateTimeOffset.UtcNow);
            var healthCache = new BackendHealthCache(fake);
            var httpClientMock = new Mock<IHttpClient>();
            var logger = TestLogger.CreateNullLogger();
            var service = new ModelDetectionService(httpClientMock.Object, logger, healthCache);

            // First call: socket exception → will retry 3× (ResiliencePolicy), then catch
            httpClientMock
                .Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpRequestException("Connection refused",
                    new SocketException((int)SocketError.ConnectionRefused)));

            var result1 = await service.GetOllamaModelsAsync("http://localhost:11434");

            // Second call (same provider, same URL, within grace): must NOT call HTTP at all
            httpClientMock.Reset(); // wipe setup so any call would return null / throw
            httpClientMock
                .Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new InvalidOperationException("Should not have been called"));

            var result2 = await service.GetOllamaModelsAsync("http://localhost:11434");

            result2.Should().NotBeEmpty("should return default models when known-down");
            // Crucially: ExecuteAsync was NOT called a second time
            httpClientMock.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Never,
                "second call within grace window must not make an HTTP request");
        }

        [Fact]
        public async Task GetLMStudioModelsAsync_SecondCallWithinGrace_DoesNotHitHttp()
        {
            var fake = MakeFake(DateTimeOffset.UtcNow);
            var healthCache = new BackendHealthCache(fake);
            var httpClientMock = new Mock<IHttpClient>();
            var logger = TestLogger.CreateNullLogger();
            var service = new ModelDetectionService(httpClientMock.Object, logger, healthCache);

            httpClientMock
                .Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpRequestException("Connection refused",
                    new SocketException((int)SocketError.ConnectionRefused)));

            await service.GetLMStudioModelsAsync("http://localhost:1234");

            httpClientMock.Reset();
            httpClientMock
                .Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new InvalidOperationException("Should not have been called"));

            var result = await service.GetLMStudioModelsAsync("http://localhost:1234");

            result.Should().NotBeEmpty();
            httpClientMock.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Never);
        }

        [Fact]
        public async Task GetOllamaModelsAsync_AfterGraceExpires_TriesHttpAgain()
        {
            var fake = MakeFake(DateTimeOffset.UtcNow);
            var healthCache = new BackendHealthCache(fake);
            var httpClientMock = new Mock<IHttpClient>();
            var logger = TestLogger.CreateNullLogger();
            var service = new ModelDetectionService(httpClientMock.Object, logger, healthCache);

            // First call fails → marks backend down
            httpClientMock
                .Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpRequestException("Connection refused",
                    new SocketException((int)SocketError.ConnectionRefused)));

            await service.GetOllamaModelsAsync("http://localhost:11434");

            // Fast-forward past the grace period
            fake.Advance(TimeSpan.FromSeconds(BrainarrConstants.BackendDownGraceSeconds + 5));

            // Still throws (backend still down), but the important thing is it tried
            httpClientMock.Invocations.Clear();

            await service.GetOllamaModelsAsync("http://localhost:11434");

            httpClientMock.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.AtLeastOnce,
                "after grace expires the service must attempt HTTP again");
        }

        [Fact]
        public async Task GetOllamaModelsAsync_SuccessfulCall_ClearsDownState()
        {
            var fake = MakeFake(DateTimeOffset.UtcNow);
            var healthCache = new BackendHealthCache(fake);
            var httpClientMock = new Mock<IHttpClient>();
            var logger = TestLogger.CreateNullLogger();
            var service = new ModelDetectionService(httpClientMock.Object, logger, healthCache);

            // First: mark down via failure
            httpClientMock
                .Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpRequestException("Refused",
                    new SocketException((int)SocketError.ConnectionRefused)));

            await service.GetOllamaModelsAsync("http://localhost:11434");
            healthCache.IsKnownDown("Ollama", "http://localhost:11434", out _).Should().BeTrue();

            // Fast-forward past grace so the cache entry expires
            fake.Advance(TimeSpan.FromSeconds(BrainarrConstants.BackendDownGraceSeconds + 5));

            // Now respond with a successful 200
            var ollamaJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                models = new[] { new { name = "llama3.2:latest" } }
            });
            var successResponse = HttpResponseFactory.CreateResponse(ollamaJson, HttpStatusCode.OK);
            httpClientMock.Reset();
            httpClientMock
                .Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(successResponse);

            await service.GetOllamaModelsAsync("http://localhost:11434");

            // Backend should now be considered healthy
            healthCache.IsKnownDown("Ollama", "http://localhost:11434", out _).Should().BeFalse();
        }
    }

    // ------------------------------------------------------------------ //
    // Minimal FakeTimeProvider for deterministic time control in tests.
    // ------------------------------------------------------------------ //

    /// <summary>
    /// A <see cref="TimeProvider"/> whose current time can be advanced manually.
    /// Keeps tests deterministic without sleeping.
    /// </summary>
    internal sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        private readonly object _lock = new object();

        public FakeTimeProvider(DateTimeOffset start)
        {
            _utcNow = start;
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_lock) return _utcNow;
        }

        /// <summary>Advances the fake clock by <paramref name="delta"/>.</summary>
        public void Advance(TimeSpan delta)
        {
            lock (_lock) _utcNow = _utcNow.Add(delta);
        }
    }
}
