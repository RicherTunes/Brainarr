using System.Threading.Tasks;
using Brainarr.TestKit.Providers.Logging;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Providers.OpenAI.Tests.Contract
{
    public class FallbackWarningTests
    {
        [Trait("scope", "provider-contract")]
        [Fact]
        public async Task Logs_Warning_When_IHttpResilience_Not_Injected()
        {
            using var sink = new TestLoggerSink();
            var logger = LogManager.GetCurrentClassLogger();
            var http = new Brainarr.TestKit.Providers.Fakes.FakeHttpClient(req =>
            {
                var body = "{\"choices\":[{\"message\":{\"content\":\"[]\"}}]}";
                return new HttpResponse(req, new HttpHeader(), body, System.Net.HttpStatusCode.OK);
            });
            var provider = new OpenAIProvider(http, logger, apiKey: "sk-test", model: "gpt-4o-mini", preferStructured: false);
            await provider.GetRecommendationsAsync("[]");
            // Prefer EventId=12001 if present; otherwise match on message text
            var idCount = sink.CountEventId(12001);
            var warnCount = sink.CountWarningsContaining("IHttpResilience not injected");
            Assert.True(idCount > 0 || warnCount > 0, "Expected fallback warning to be logged (EventId=12001 or message substring)");
        }
    }
}
