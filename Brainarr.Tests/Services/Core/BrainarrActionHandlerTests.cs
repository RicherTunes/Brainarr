using System.Collections.Generic;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class BrainarrActionHandlerTests
    {
        private BrainarrActionHandler CreateSut()
        {
            var http = new Mock<IHttpClient>();
            Logger logger = TestLogger.CreateNullLogger();
            var detection = new ModelDetectionService(http.Object, logger);
            return new BrainarrActionHandler(http.Object, detection, logger);
        }

        [Fact]
        public void HandleAction_Returns_StaticModelOptions_ForCloudProviders()
        {
            var sut = CreateSut();

            var openai = sut.HandleAction("getOpenAIModels", new Dictionary<string, string>());
            openai.Should().NotBeNull();

            var gemini = sut.HandleAction("getGeminiModels", new Dictionary<string, string>());
            gemini.Should().NotBeNull();
        }

        [Fact]
        public void GetModelOptions_ByProviderName_ReturnsOptions()
        {
            var sut = CreateSut();
            var result = sut.GetModelOptions("OpenAI");
            result.Should().NotBeNull();
        }
    }
}
