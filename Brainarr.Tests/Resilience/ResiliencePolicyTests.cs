using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Resilience;
using Xunit;

namespace Brainarr.Tests.Resilience
{
    /// <summary>
    /// Tests for <see cref="ResiliencePolicy"/>. Uses <see cref="IClassFixture{T}"/>
    /// to reset static state before each test class run, preventing cross-test pollution.
    /// </summary>
    public class ResiliencePolicyTests : IDisposable
    {
        private static Logger L => LogManager.GetCurrentClassLogger();

        public ResiliencePolicyTests()
        {
            // Reset static state before each test to prevent pollution from other tests
            // that may have configured the adaptive rate limiter.
            ResiliencePolicy.ResetForTesting();
        }

        public void Dispose()
        {
            // Clean up after tests
            ResiliencePolicy.ResetForTesting();
        }

        [Fact]
        public void ResiliencePolicy_does_not_reintroduce_local_retry_backoff_executor()
        {
            var sourcePath = FindSourceFile("Brainarr.Plugin", "Resilience", "ResiliencePolicy.cs");
            var source = File.ReadAllText(sourcePath);

            source.Should().Contain("GenericResilienceExecutor.ExecuteWithResilienceAsync");

            var bannedSnippets = new Dictionary<string, string>
            {
                ["RunWithRetriesAsync"] = "legacy helper name",
                ["Func<CancellationToken, Task<T>>"] = "generic operation retry helper",
                ["Task.Delay("] = "local backoff delay",
                ["Thread.Sleep("] = "blocking local backoff delay",
                ["Random.Shared"] = "local jitter source",
                ["new Random("] = "local jitter source"
            };
            var presentBannedSnippets = bannedSnippets
                .Where(kvp => source.Contains(kvp.Key, StringComparison.Ordinal))
                .Select(kvp => $"{kvp.Key} ({kvp.Value})")
                .ToList();

            presentBannedSnippets.Should().BeEmpty(
                "Brainarr resilience must stay on Common's GenericResilienceExecutor instead of growing a plugin-local retry/backoff helper");

            var methodNames = Regex.Matches(
                    source,
                    @"\b(?:public|private|internal|protected)\s+(?:static\s+)?(?:async\s+)?(?:Task(?:<[^>]+>)?|ValueTask(?:<[^>]+>)?|[A-Za-z0-9_<>,\?\[\]]+)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(")
                .Select(match => match.Groups["name"].Value)
                .ToList();
            var localBackoffMethodNames = methodNames
                .Where(name => name.Contains("Backoff", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("WithRetries", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("RetryPolicy", StringComparison.OrdinalIgnoreCase))
                .ToList();

            localBackoffMethodNames.Should().BeEmpty(
                "retry/backoff helpers belong in Lidarr.Plugin.Common, not Brainarr.Plugin.ResiliencePolicy");
        }

        [Fact]
        public async Task WithHttpResilienceAsync_retries_transient_then_returns_ok()
        {
            int attempts = 0;
            var request = new HttpRequest("http://example") { Method = System.Net.Http.HttpMethod.Post };
            var headers = new HttpHeader();

            var resp = await ResiliencePolicy.WithHttpResilienceAsync(
                request,
                async (req, ct) =>
                {
                    attempts++;
                    await Task.Yield();
                    if (attempts < 2)
                    {
                        return new HttpResponse(req, headers, "fail", HttpStatusCode.InternalServerError);
                    }
                    return new HttpResponse(req, headers, "ok", HttpStatusCode.OK);
                },
                origin: "test",
                logger: L,
                cancellationToken: CancellationToken.None,
                maxRetries: 3);

            attempts.Should().BeGreaterThanOrEqualTo(2);
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            resp.Content.Should().Be("ok");
        }

        [Fact]
        public async Task WithHttpResilienceAsync_returns_last_response_after_exhausting_retries()
        {
            int attempts = 0;
            var request = new HttpRequest("http://example") { Method = System.Net.Http.HttpMethod.Post };
            var headers = new HttpHeader();

            var resp = await ResiliencePolicy.WithHttpResilienceAsync(
                request,
                async (req, ct) =>
                {
                    attempts++;
                    await Task.Yield();
                    return new HttpResponse(req, headers, "fail", HttpStatusCode.BadGateway);
                },
                origin: "test",
                logger: L,
                cancellationToken: CancellationToken.None,
                maxRetries: 2);

            attempts.Should().Be(2);
            resp.Should().NotBeNull();
            resp.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        }

        private static string FindSourceFile(params string[] relativeParts)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            var relativePath = Path.Combine(relativeParts);

            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("Could not find source file", relativePath);
        }
    }
}
