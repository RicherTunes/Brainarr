using System;
using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NLog;
using Brainarr.Tests.Helpers;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests
{
    [Trait("Category", "Unit")]
    public class BrainarrImportListDelegationTests
    {
        private readonly Mock<IHttpClient> _httpClient = new Mock<IHttpClient>();
        private readonly Mock<IImportListStatusService> _status = new Mock<IImportListStatusService>();
        private readonly Mock<IConfigService> _config = new Mock<IConfigService>();
        private readonly Mock<IParsingService> _parsing = new Mock<IParsingService>();
        private readonly Mock<IArtistService> _artists = new Mock<IArtistService>();
        private readonly Mock<IAlbumService> _albums = new Mock<IAlbumService>();
        private readonly Mock<IBrainarrOrchestrator> _orch = new Mock<IBrainarrOrchestrator>();
        private readonly Logger _logger = TestLogger.CreateNullLogger();

        private NzbDrone.Core.ImportLists.Brainarr.Brainarr CreateSut()
        {
            var sut = new NzbDrone.Core.ImportLists.Brainarr.Brainarr(
                _httpClient.Object,
                _status.Object,
                _config.Object,
                _parsing.Object,
                _artists.Object,
                _albums.Object,
                _logger,
                _orch.Object);

            // Initialize Settings via reflection so Fetch/Test can run
            var prop = sut.GetType().BaseType!
                .GetProperty("Settings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            prop!.SetValue(sut, new BrainarrSettings());
            return sut;
        }

        [Fact]
        public void Fetch_Delegates_To_Orchestrator()
        {
            var expected = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "A", Album = "B" }
            };
            _orch.Setup(o => o.FetchRecommendations(It.IsAny<BrainarrSettings>()))
                .Returns(expected);

            var sut = CreateSut();
            var result = sut.Fetch();

            result.Should().BeEquivalentTo(expected);
            _orch.Verify(o => o.FetchRecommendations(It.IsAny<BrainarrSettings>()), Times.Once);
        }

        [Fact]
        public void TestConfiguration_Delegates_To_Orchestrator()
        {
            var failures = new List<ValidationFailure>();
            _orch.Setup(o => o.ValidateConfiguration(It.IsAny<BrainarrSettings>(), It.IsAny<List<ValidationFailure>>()))
                .Callback<BrainarrSettings, List<ValidationFailure>>((_, __) => { });

            var sut = CreateSut();
            sut.TestConfiguration(failures);

            failures.Should().BeEmpty();
            _orch.Verify(o => o.ValidateConfiguration(It.IsAny<BrainarrSettings>(), failures), Times.Once);
        }

        [Fact]
        public void RequestAction_Delegates_To_Orchestrator()
        {
            const string action = "testConnection";
            var query = new Dictionary<string, string> { { "provider", "openai" } };
            var expected = new { status = "ok" };
            _orch.Setup(o => o.HandleAction(action, query, It.IsAny<BrainarrSettings>()))
                .Returns(expected);

            var sut = CreateSut();
            var result = sut.RequestAction(action, query);

            result.Should().BeEquivalentTo(expected);
            _orch.Verify(o => o.HandleAction(action, query, It.IsAny<BrainarrSettings>()), Times.Once);
        }
    }
}

