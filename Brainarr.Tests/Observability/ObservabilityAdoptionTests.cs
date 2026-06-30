using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Observability;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.Observability
{
    /// <summary>
    /// Smoke tests verifying Common observability adoption:
    /// PluginLogContext scopes are pushed/cleared at entry points, and
    /// Scrub.Secret/Scrub.Url redact sensitive values correctly.
    /// </summary>
    public class ObservabilityAdoptionTests
    {
        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        private NzbDrone.Core.ImportLists.Brainarr.Brainarr CreateImportList(IBrainarrOrchestrator orchestrator)
        {
            var http = new Mock<IHttpClient>();
            var status = new Mock<IImportListStatusService>();
            var config = new Mock<IConfigService>();
            var parser = new Mock<IParsingService>();
            var artists = new Mock<IArtistService>();
            var albums = new Mock<IAlbumService>();
            var mediaFiles = new Mock<IMediaFileService>();
            var audioTags = new Mock<IAudioTagService>();
            Logger logger = TestLogger.CreateNullLogger("ObsTestLogger");

            return new NzbDrone.Core.ImportLists.Brainarr.Brainarr(
                http.Object,
                status.Object,
                config.Object,
                parser.Object,
                artists.Object,
                albums.Object,
                mediaFiles.Object,
                audioTags.Object,
                logger,
                orchestrator);
        }

        // ------------------------------------------------------------------ //
        // Test 1: BrainarrImportList.Fetch() pushes and clears PluginLogContext
        // ------------------------------------------------------------------ //

        [Fact]
        public void BrainarrImportList_Fetch_PushesLogContext()
        {
            // Arrange
            PluginLogContext.Current.Should().BeNull("no scope should be active before Fetch");

            PluginLogContext? capturedCtx = null;
            var orchestrator = new Mock<IBrainarrOrchestrator>();
            orchestrator
                .Setup(o => o.FetchRecommendations(It.IsAny<BrainarrSettings>()))
                .Returns(() =>
                {
                    // Capture the scope that Fetch() pushed
                    capturedCtx = PluginLogContext.Current;
                    return new List<ImportListItemInfo>();
                });

            var sut = CreateImportList(orchestrator.Object);

            // Act
            sut.Fetch();

            // Assert: scope was active during the call
            capturedCtx.Should().NotBeNull("Fetch() must push a PluginLogContext scope");
            capturedCtx!.PluginName.Should().Be("Brainarr");
            capturedCtx.Operation.Should().Be("ImportListSync");
            capturedCtx.CorrelationId.Should().NotBeNullOrWhiteSpace();

            // Assert: scope is cleaned up after Fetch returns
            PluginLogContext.Current.Should().BeNull("scope must be popped after Fetch returns");
        }

        // ------------------------------------------------------------------ //
        // Test 2: OpenAI provider CompleteAsync pushes LogContext with provider field
        // ------------------------------------------------------------------ //

        [Fact]
        public async Task OpenAiProvider_Complete_PushesLogContext_WithProviderField()
        {
            // Arrange: use a mock HTTP client that returns a minimal valid OpenAI response
            const string OpenAiJson = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}";
            var fakeResponse = HttpResponseFactory.CreateResponse(
                new HttpRequest("https://api.openai.com/v1/chat/completions"),
                OpenAiJson,
                HttpStatusCode.OK);

            var httpMock = new Mock<IHttpClient>();
            PluginLogContext? capturedCtx = null;
            httpMock
                .Setup(h => h.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(_ => capturedCtx = PluginLogContext.Current)
                .ReturnsAsync(fakeResponse);

            Logger logger = TestLogger.CreateNullLogger("OaiObsTest");
            var provider = new BrainarrOpenAiProvider(httpMock.Object, logger, "sk-testkey12345", "gpt-4o-mini");

            // Act
            var request = new LlmRequest { Prompt = "Test" };
            await provider.CompleteAsync(request, CancellationToken.None);

            // Assert: scope was active during the HTTP call
            capturedCtx.Should().NotBeNull("CompleteAsync must push a PluginLogContext scope");
            capturedCtx!.PluginName.Should().Be("Brainarr");
            capturedCtx.Operation.Should().Be("LlmComplete");
            capturedCtx.Provider.Should().Be("openai");
            capturedCtx.CorrelationId.Should().NotBeNullOrWhiteSpace();

            // Assert: scope is cleaned up after CompleteAsync returns
            PluginLogContext.Current.Should().BeNull("scope must be popped after CompleteAsync returns");
        }

        // ------------------------------------------------------------------ //
        // Test 3: Scrub.Url strips API keys from URLs
        // ------------------------------------------------------------------ //

        [Fact]
        public void OpenAiProvider_LogsUrl_AppliesScrub()
        {
            // Scrub.Url must redact sensitive query parameters. Verify the helper
            // preserves clean URLs and redacts param values when they appear.
            var cleanUrl = "https://api.openai.com/v1/chat/completions";
            var urlWithKey = "https://api.openai.com/v1/chat/completions?api_key=sk-secret123";

            Scrub.Url(cleanUrl).Should().Be(cleanUrl,
                "a URL without sensitive params must pass through unchanged");

            Scrub.Url(urlWithKey).Should().NotContain("sk-secret123",
                "Scrub.Url must redact api_key values");
            Scrub.Url(urlWithKey).Should().Contain("api_key=***",
                "Scrub.Url must replace the value with ***");
        }

        // ------------------------------------------------------------------ //
        // Test 4: PluginLogContext scope lifecycle — push/pop/nesting
        // ------------------------------------------------------------------ //

        [Fact]
        public void PluginLogContext_Push_ClearsAfterDispose()
        {
            PluginLogContext.Current.Should().BeNull("no scope at test start");

            using (var ctx = PluginLogContext.Push("Brainarr", "ImportListSync"))
            {
                PluginLogContext.Current.Should().NotBeNull();
                PluginLogContext.Current!.Operation.Should().Be("ImportListSync");
                PluginLogContext.Current.PluginName.Should().Be("Brainarr");
                PluginLogContext.Current.LinePrefix().Should().MatchRegex(@"^\[ImportListSync:[a-f0-9]+\] $");
            }

            PluginLogContext.Current.Should().BeNull("scope must be popped after Dispose");
        }

        [Fact]
        public void PluginLogContext_WithProvider_IncludesProviderInPrefix()
        {
            using var ctx = PluginLogContext.Push("Brainarr", "LlmComplete", provider: "openai");
            PluginLogContext.Current!.Provider.Should().Be("openai");
            PluginLogContext.Current.LinePrefix().Should().Contain(":openai]");
        }

        // ------------------------------------------------------------------ //
        // Test 5: Scrub.Secret redacts API key values
        // ------------------------------------------------------------------ //

        [Theory]
        [InlineData("sk-abc123456789", "sk-***")]
        [InlineData("ab", "***")]    // shorter than leadingVisible → all redacted
        [InlineData("", "***")]      // empty → all redacted
        [InlineData(null, "***")]    // null → all redacted
        public void Scrub_Secret_RedactsCorrectly(string? value, string expected)
        {
            Scrub.Secret(value).Should().Be(expected);
        }
    }
}
