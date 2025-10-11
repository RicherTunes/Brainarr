using System.Threading.Tasks;
using Brainarr.TestKit.Providers.Logging;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;
using System.Text;
using Brainarr.TestKit.Providers.Http;

namespace Brainarr.Providers.OpenAI.Tests.Contract
{
    public class FallbackWarningTests
    {
        [Trait("scope", "provider-contract")]
        [Fact]
        public async Task Logs_Warning_When_IHttpResilience_Not_Injected()
        {
            // ensure warn-once cache is clear so this test can observe the warning
            NzbDrone.Core.ImportLists.Brainarr.Services.LoggerExtensions.ClearWarnOnceKeysForTests();
            using var sink = new TestLoggerSink();
            var logger = LogManager.GetCurrentClassLogger();
            var http = new Brainarr.TestKit.Providers.Fakes.FakeHttpClient(req =>
                HttpResponseFactory.Ok(req, "{\"choices\":[{\"message\":{\"content\":\"[]\"}}]}"));
            var provider = new OpenAIProvider(http, logger, apiKey: "sk-test", model: "gpt-4o-mini", preferStructured: false);
            await provider.GetRecommendationsAsync("[]");
            // Prefer a deterministic check via warn-once registry; also keep log sink as a soft check
            var warned = NzbDrone.Core.ImportLists.Brainarr.Services.LoggerExtensions.HasWarnedOnceForTests(12001, "OpenAIProvider");
            var idCount = sink.CountEventId(12001);
            var warnCount = sink.CountWarningsContaining("IHttpResilience not injected");
            Assert.True(warned || idCount > 0 || warnCount > 0, "Expected fallback warning to be logged (EventId=12001 or message substring)");
        }
    }
}
