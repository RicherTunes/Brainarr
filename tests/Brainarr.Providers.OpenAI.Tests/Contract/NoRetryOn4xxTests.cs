using System.Net;
using System.Threading.Tasks;
using System.Text;
using Brainarr.TestKit.Providers.Fakes;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Providers.OpenAI.Tests.Contract
{
    public class NoRetryOn4xxTests
    {
        [Trait("scope", "provider-contract")]
        [Fact]
        public async Task No_Retry_On_Non429_4xx()
        {
            var logger = LogManager.GetCurrentClassLogger();
            var http = new FakeHttpClient(req => new HttpResponse(req, new HttpHeader(), Encoding.UTF8.GetBytes("{ \"error\": \"bad req\" }"), HttpStatusCode.BadRequest));
            var exec = new TestResilience();
            var provider = new OpenAIProvider(http, logger, apiKey: "sk-test", model: "gpt-4o-mini", preferStructured: false, httpExec: exec);

            await Assert.ThrowsAnyAsync<System.Exception>(() => provider.GetRecommendationsAsync("Recommend exactly 1 album"));
            Assert.Equal(1, exec.Calls);
        }
    }
}
