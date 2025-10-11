using System.Net;
using System.Threading.Tasks;
using System.Text;
using Brainarr.TestKit.Providers.Fakes;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;
using Brainarr.TestKit.Providers.Http;

namespace Brainarr.Providers.OpenAI.Tests.Contract
{
    public class NoRetryOn4xxTests
    {
        [Trait("scope", "provider-contract")]
        [Fact]
        public async Task No_Retry_On_Non429_4xx()
        {
            var logger = LogManager.GetCurrentClassLogger();
            var http = new FakeHttpClient(req => HttpResponseFactory.Error(req, HttpStatusCode.BadRequest, "{ \"error\": \"bad req\" }") );
            var exec = new TestResilience();
            var provider = new OpenAIProvider(http, logger, apiKey: "sk-test", model: "gpt-4o-mini", preferStructured: false, httpExec: exec);

            // Provider should not retry on 4xx (non-429). Current behavior returns an empty list instead of throwing.
            var recs = await provider.GetRecommendationsAsync("Recommend exactly 1 album");
            Assert.Empty(recs);
            // Provider tries at most once per body shape; two shapes (structured, unstructured) => 2 calls, no resilience retries
            Assert.Equal(2, exec.Calls);
        }
    }
}
