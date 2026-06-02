using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using Xunit;

namespace Brainarr.Tests.Services.Providers
{
    /// <summary>
    /// Security guard: the Gemini model-dropdown enumeration carries the API key in the URL query, so
    /// it MUST set SuppressHttpError — otherwise a non-2xx (invalid/unactivated key, common first-run)
    /// makes the host throw an HttpException whose URL-bearing message NLog renders unredacted, leaking
    /// the key into logs (the same leak fixed in BrainarrGeminiProvider; this is the sibling path).
    /// </summary>
    public class GeminiModelDiscoveryTests
    {
        private readonly Mock<IHttpClient> _http = new();
        private readonly Logger _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();

        [Fact]
        public async Task GetModelOptionsAsync_SetsSuppressHttpError_AndKeyStaysInQuery()
        {
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"models\":[{\"name\":\"models/gemini-1.5-flash\"}]}"));

            var discovery = new GeminiModelDiscovery(_http.Object, _logger);
            await discovery.GetModelOptionsAsync("AIza-secret-key");

            captured.Should().NotBeNull();
            captured!.SuppressHttpError.Should().BeTrue(
                "the key is in the URL; a host HttpException would render it unredacted into logs");
            captured.Url.ToString().Should().Contain("key=AIza-secret-key", "Gemini authenticates via the ?key= query param");
        }

        [Fact]
        public async Task GetModelOptionsAsync_NonSuccess_ReturnsDefaults_WithoutThrowingOrLeaking()
        {
            // With SuppressHttpError set, a 400/401 returns a response (handled by the status-code
            // branch) rather than throwing a URL-bearing exception. The call must degrade to defaults.
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.BadRequest));

            var discovery = new GeminiModelDiscovery(_http.Object, _logger);

            Func<Task> act = async () => await discovery.GetModelOptionsAsync("AIza-secret-key");
            await act.Should().NotThrowAsync("a failed key probe must fail soft, not throw a URL-bearing exception");

            var result = await discovery.GetModelOptionsAsync("AIza-secret-key");
            result.Should().NotBeEmpty("falls back to the default model list when enumeration fails");
        }
    }
}
