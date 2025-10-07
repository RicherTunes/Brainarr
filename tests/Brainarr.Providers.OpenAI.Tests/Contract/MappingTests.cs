using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Text;
using Brainarr.TestKit.Providers.Fakes;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Providers.OpenAI.Tests.Contract
{
    public class MappingTests
    {
        [Trait("scope", "provider-contract")]
        [Fact]
        public async Task Maps_ChatCompletion_To_Recommendations()
        {
            var logger = LogManager.GetCurrentClassLogger();
            var assetPath = Path.Combine(AppContext.BaseDirectory, "Contract", "TestAssets", "openai.chat.min.json");
            Assert.True(File.Exists(assetPath), $"Missing test asset at {assetPath}");
            var json = await File.ReadAllTextAsync(assetPath);

            var http = new FakeHttpClient(req => new HttpResponse(req, new HttpHeader(), Encoding.UTF8.GetBytes(json), System.Net.HttpStatusCode.OK));
            var provider = new OpenAIProvider(http, logger, apiKey: "sk-test", model: "gpt-4o-mini", preferStructured: false);

            var recs = await provider.GetRecommendationsAsync("Recommend exactly 1 album");
            Assert.NotNull(recs);
            Assert.NotEmpty(recs);
            var r = recs[0];
            Assert.Equal("Pink Floyd", r.Artist);
            Assert.Equal("The Dark Side of the Moon", r.Album);
            Assert.Equal("Progressive Rock", r.Genre);
            Assert.True(r.Confidence > 0.5);
        }
    }
}
