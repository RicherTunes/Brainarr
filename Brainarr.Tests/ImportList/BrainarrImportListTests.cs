using System.Collections.Generic;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.ImportList
{
    public class BrainarrImportListTests
    {
        private NzbDrone.Core.ImportLists.Brainarr.Brainarr CreateSut(IBrainarrOrchestrator orchestrator)
        {
            var http = new Mock<IHttpClient>();
            var status = new Mock<IImportListStatusService>();
            var config = new Mock<IConfigService>();
            var parser = new Mock<IParsingService>();
            var artists = new Mock<IArtistService>();
            var albums = new Mock<IAlbumService>();
            Logger logger = TestLogger.CreateNullLogger();

            return new NzbDrone.Core.ImportLists.Brainarr.Brainarr(
                http.Object,
                status.Object,
                config.Object,
                parser.Object,
                artists.Object,
                albums.Object,
                logger,
                orchestrator);
        }

        [Fact]
        public void Fetch_ReturnsItems_FromOrchestrator()
        {
            var orchestrator = new Mock<IBrainarrOrchestrator>();
            orchestrator.Setup(o => o.FetchRecommendations(It.IsAny<BrainarrSettings>()))
                        .Returns(new List<ImportListItemInfo> { new ImportListItemInfo { Artist = "A" } });

            var sut = CreateSut(orchestrator.Object);
            var items = sut.Fetch();

            items.Should().HaveCount(1);
            items[0].Artist.Should().Be("A");
        }

        [Fact]
        public void RequestAction_Delegates_To_Orchestrator()
        {
            var orchestrator = new Mock<IBrainarrOrchestrator>();
            orchestrator.Setup(o => o.HandleAction("ping", It.IsAny<IDictionary<string, string>>(), It.IsAny<BrainarrSettings>()))
                        .Returns("pong");

            var sut = CreateSut(orchestrator.Object);
            var result = sut.RequestAction("ping", new Dictionary<string, string>());
            result.Should().Be("pong");
        }

        [Fact]
        public void TestConfiguration_Delegates_To_Orchestrator()
        {
            var failures = new List<FluentValidation.Results.ValidationFailure>();
            var orchestrator = new Mock<IBrainarrOrchestrator>();
            orchestrator.Setup(o => o.ValidateConfiguration(It.IsAny<BrainarrSettings>(), failures))
                        .Verifiable();

            var sut = CreateSut(orchestrator.Object);
            sut.TestConfiguration(failures);
            orchestrator.Verify();
        }
    }
}
