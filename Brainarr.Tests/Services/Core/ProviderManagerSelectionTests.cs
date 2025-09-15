using System.Collections.Generic;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class ProviderManagerSelectionTests
    {
        private ProviderManager CreateSut()
        {
            var http = new Mock<IHttpClient>();
            var factory = new Mock<IProviderFactory>();
            var retry = new Mock<IRetryPolicy>();
            var limiter = new Mock<IRateLimiter>();
            Logger logger = TestLogger.CreateNullLogger();

            var detection = new ModelDetectionService(http.Object, logger);

            return new ProviderManager(
                http.Object,
                factory.Object,
                detection,
                retry.Object,
                limiter.Object,
                logger);
        }

        [Fact]
        public void SelectBestModel_PrefersHigherRankAndSizeBonus()
        {
            var sut = CreateSut();
            var models = new List<string>
            {
                "qwen2-7b-instruct",
                "llama3-8b-instruct",
                "gpt-3.5"
            };

            var best = sut.SelectBestModel(models);
            best.Should().Be("llama3-8b-instruct");
        }

        [Fact]
        public void SelectBestModel_ReturnsNull_WhenEmpty()
        {
            var sut = CreateSut();
            var best = sut.SelectBestModel(new List<string>());
            best.Should().BeNull();
        }
    }
}
