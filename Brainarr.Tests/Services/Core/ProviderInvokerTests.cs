using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class ProviderInvokerTests
    {
        [Fact]
        public async Task InvokeAsync_FallsBack_WhenCtOverloadNotImplemented()
        {
            var provider = new Mock<IAIProvider>(MockBehavior.Strict);
            provider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new System.NotImplementedException());
            provider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                    .ReturnsAsync(new List<Recommendation> { new Recommendation { Artist = "A" } });

            var invoker = new ProviderInvoker();
            Logger logger = TestLogger.CreateNullLogger();

            var result = await invoker.InvokeAsync(provider.Object, "prompt", logger, CancellationToken.None);

            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("A");
        }
    }
}
