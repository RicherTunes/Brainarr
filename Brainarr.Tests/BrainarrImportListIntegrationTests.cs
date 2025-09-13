
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests
{
    [Trait("Category", "Unit")]
    public class BrainarrImportListIntegrationTests
    {
        private readonly Mock<IHttpClient> _httpClientMock;
        private readonly Mock<IImportListStatusService> _importListStatusServiceMock;
        private readonly Mock<IConfigService> _configServiceMock;
        private readonly Mock<IParsingService> _parsingServiceMock;
        private readonly Mock<IArtistService> _artistServiceMock;
        private readonly Mock<IAlbumService> _albumServiceMock;
        private readonly Mock<IBrainarrOrchestrator> _orchestratorMock;
        private readonly Logger _logger;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Brainarr _brainarrImportList;

        public BrainarrImportListIntegrationTests()
        {
            _httpClientMock = new Mock<IHttpClient>();
            _importListStatusServiceMock = new Mock<IImportListStatusService>();
            _configServiceMock = new Mock<IConfigService>();
            _parsingServiceMock = new Mock<IParsingService>();
            _artistServiceMock = new Mock<IArtistService>();
            _albumServiceMock = new Mock<IAlbumService>();
            _orchestratorMock = new Mock<IBrainarrOrchestrator>();
            _logger = TestLogger.CreateNullLogger();

            // Setup orchestrator to accept any settings
            _orchestratorMock
                .Setup(x => x.FetchRecommendations(It.IsAny<BrainarrSettings>()))
                .Returns(new List<ImportListItemInfo>());

            _orchestratorMock
                .Setup(x => x.ValidateConfiguration(It.IsAny<BrainarrSettings>(), It.IsAny<List<ValidationFailure>>()))
                .Callback<BrainarrSettings, List<ValidationFailure>>((_, failures) => { /* No errors */ });

            _brainarrImportList = new NzbDrone.Core.ImportLists.Brainarr.Brainarr(
                _httpClientMock.Object,
                _importListStatusServiceMock.Object,
                _configServiceMock.Object,
                _parsingServiceMock.Object,
                _artistServiceMock.Object,
                _albumServiceMock.Object,
                _logger,
                _orchestratorMock.Object);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidDependencies_InitializesSuccessfully()
        {
            // Assert
            _brainarrImportList.Should().NotBeNull();
            _brainarrImportList.Name.Should().Be("Brainarr AI Music Discovery");
            _brainarrImportList.ListType.Should().Be(ImportListType.Program);
        }

        [Fact]
        public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new NzbDrone.Core.ImportLists.Brainarr.Brainarr(
                    null,
                    _importListStatusServiceMock.Object,
                    _configServiceMock.Object,
                    _parsingServiceMock.Object,
                    _artistServiceMock.Object,
                    _albumServiceMock.Object,
                    _logger));
        }

        [Fact]
        public void Constructor_WithNullArtistService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new NzbDrone.Core.ImportLists.Brainarr.Brainarr(
                    _httpClientMock.Object,
                    _importListStatusServiceMock.Object,
                    _configServiceMock.Object,
                    _parsingServiceMock.Object,
                    null,
                    _albumServiceMock.Object,
                    _logger));
        }

        #endregion

        #region Property Tests

        [Fact]
        public void Name_ReturnsCorrectValue()
        {
            // Act & Assert
            _brainarrImportList.Name.Should().Be("Brainarr AI Music Discovery");
        }

        [Fact]
        public void ListType_ReturnsProgram()
        {
            // Act & Assert
            _brainarrImportList.ListType.Should().Be(ImportListType.Program);
        }

        [Fact]
        public void MinRefreshInterval_ReturnsCorrectValue()
        {
            // Act & Assert
            _brainarrImportList.MinRefreshInterval.Should().Be(TimeSpan.FromHours(6)); // BrainarrConstants.MinRefreshIntervalHours = 6
        }

        #endregion

        #region Fetch Tests

        [Fact]
        public void Fetch_WithValidSettings_CallsOrchestrator()
        {
            // Arrange
            var expectedRecommendations = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "Test Artist", Album = "Test Album" }
            };

            _orchestratorMock
                .Setup(x => x.FetchRecommendations(It.IsAny<BrainarrSettings>()))
                .Returns(expectedRecommendations);

            // Initialize Settings on the import list via reflection
            var settingsProp = _brainarrImportList.GetType().BaseType!
                .GetProperty("Settings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            settingsProp!.SetValue(_brainarrImportList, new BrainarrSettings());

            // Act
            var result = _brainarrImportList.Fetch();

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEquivalentTo(expectedRecommendations);
            _orchestratorMock.Verify(x => x.FetchRecommendations(It.IsAny<BrainarrSettings>()), Times.Once);
        }

        [Fact]
        public void Fetch_WhenOrchestratorReturnsEmpty_ReturnsEmptyList()
        {
            // Arrange
            _orchestratorMock
                .Setup(x => x.FetchRecommendations(It.IsAny<BrainarrSettings>()))
                .Returns(new List<ImportListItemInfo>());

            // Initialize Settings
            var settingsProp = _brainarrImportList.GetType().BaseType!
                .GetProperty("Settings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            settingsProp!.SetValue(_brainarrImportList, new BrainarrSettings());

            // Act
            var result = _brainarrImportList.Fetch();

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public void Fetch_WhenOrchestratorThrows_PropagatesException()
        {
            // Arrange
            var expectedException = new InvalidOperationException("Test exception");
            _orchestratorMock
                .Setup(x => x.FetchRecommendations(It.IsAny<BrainarrSettings>()))
                .Throws(expectedException);

            // Initialize Settings
            var settingsProp = _brainarrImportList.GetType().BaseType!
                .GetProperty("Settings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            settingsProp!.SetValue(_brainarrImportList, new BrainarrSettings());

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => _brainarrImportList.Fetch());
            exception.Message.Should().Be("Test exception");
        }

        #endregion

        #region Test (Validation) Tests

        [Fact]
        public void Test_WithValidConfiguration_DoesNotAddFailures()
        {
            // Arrange
            var failures = new List<ValidationFailure>();
            _orchestratorMock
                .Setup(x => x.ValidateConfiguration(It.IsAny<BrainarrSettings>(), It.IsAny<List<ValidationFailure>>()))
                .Callback<BrainarrSettings, List<ValidationFailure>>((settings, failuresList) =>
                {
                    // Don't add any failures for valid configuration
                });

            // Initialize Settings
            var settingsProp = _brainarrImportList.GetType().BaseType!
                .GetProperty("Settings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            settingsProp!.SetValue(_brainarrImportList, new BrainarrSettings());

            // Act
            _brainarrImportList.TestConfiguration(failures);

            // Assert
            failures.Should().BeEmpty();
            _orchestratorMock.Verify(x => x.ValidateConfiguration(It.IsAny<BrainarrSettings>(), failures), Times.Once);
        }

        [Fact]
        public void Test_WithInvalidConfiguration_AddsFailures()
        {
            // Arrange
            var failures = new List<ValidationFailure>();
            _orchestratorMock
                .Setup(x => x.ValidateConfiguration(It.IsAny<BrainarrSettings>(), It.IsAny<List<ValidationFailure>>()))
                .Callback<BrainarrSettings, List<ValidationFailure>>((settings, failuresList) =>
                {
                    failuresList.Add(new ValidationFailure("Provider", "Invalid provider configuration"));
                    failuresList.Add(new ValidationFailure("ApiKey", "API key is required"));
                });

            // Initialize Settings
            var settingsProp = _brainarrImportList.GetType().BaseType!
                .GetProperty("Settings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            settingsProp!.SetValue(_brainarrImportList, new BrainarrSettings());

            // Act
            _brainarrImportList.TestConfiguration(failures);

            // Assert
            failures.Should().HaveCount(2);
            failures[0].PropertyName.Should().Be("Provider");
            failures[0].ErrorMessage.Should().Be("Invalid provider configuration");
            failures[1].PropertyName.Should().Be("ApiKey");
            failures[1].ErrorMessage.Should().Be("API key is required");
        }

        #endregion

        #region RequestAction Tests

        /* These tests are commented out as RequestAction method doesn't exist in the current implementation

        [Fact]
        public void RequestAction_WithValidAction_CallsOrchestrator()
        {
            // Arrange
            const string action = "testConnection";
            var query = new Dictionary<string, string> { { "provider", "openai" } };
            var expectedResult = new { status = "success" };

            _orchestratorMock
                .Setup(x => x.HandleAction(action, query, It.IsAny<BrainarrSettings>()))
                .Returns(expectedResult);

            // Act
            var result = _brainarrImportList.RequestAction(action, query);

            // Assert
            result.Should().BeEquivalentTo(expectedResult);
            _orchestratorMock.Verify(x => x.HandleAction(action, query, It.IsAny<BrainarrSettings>()), Times.Once);
        }

        [Fact]
        public void RequestAction_WithNullAction_CallsOrchestrator()
        {
            // Arrange
            var query = new Dictionary<string, string>();
            var expectedResult = new { error = "Invalid action" };

            _orchestratorMock
                .Setup(x => x.HandleAction(null, query, It.IsAny<BrainarrSettings>()))
                .Returns(expectedResult);

            // Act
            var result = _brainarrImportList.RequestAction(null, query);

            // Assert
            result.Should().BeEquivalentTo(expectedResult);
            _orchestratorMock.Verify(x => x.HandleAction(null, query, It.IsAny<BrainarrSettings>()), Times.Once);
        }

        [Fact]
        public void RequestAction_WithEmptyQuery_CallsOrchestrator()
        {
            // Arrange
            const string action = "getModels";
            var query = new Dictionary<string, string>();
            var expectedResult = new { models = new string[0] };

            _orchestratorMock
                .Setup(x => x.HandleAction(action, query, It.IsAny<BrainarrSettings>()))
                .Returns(expectedResult);

            // Act
            var result = _brainarrImportList.RequestAction(action, query);

            // Assert
            result.Should().BeEquivalentTo(expectedResult);
            _orchestratorMock.Verify(x => x.HandleAction(action, query, It.IsAny<BrainarrSettings>()), Times.Once);
        }

        [Fact]
        public void RequestAction_WhenOrchestratorThrows_PropagatesException()
        {
            // Arrange
            const string action = "testConnection";
            var query = new Dictionary<string, string>();
            var expectedException = new ArgumentException("Invalid action parameter");

            _orchestratorMock
                .Setup(x => x.HandleAction(action, query, It.IsAny<BrainarrSettings>()))
                .Throws(expectedException);

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => _brainarrImportList.RequestAction(action, query));
            exception.Message.Should().Be("Invalid action parameter");
        }
        */

        #endregion

        #region Integration Scenario Tests

        /* [Fact]
        public void CompleteWorkflow_FetchValidateAndAction_WorksCorrectly()
        {
            // Arrange
            var recommendations = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "The Beatles", Album = "Abbey Road" },
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "Dark Side of the Moon" }
            };

            var validationFailures = new List<ValidationFailure>();
            var actionResult = new { status = "connected", provider = "OpenAI" };

            _orchestratorMock
                .Setup(x => x.FetchRecommendations(It.IsAny<BrainarrSettings>()))
                .Returns(recommendations);

            _orchestratorMock
                .Setup(x => x.ValidateConfiguration(It.IsAny<BrainarrSettings>(), It.IsAny<List<ValidationFailure>>()))
                .Callback<BrainarrSettings, List<ValidationFailure>>((_, failures) => { });

            _orchestratorMock
                .Setup(x => x.HandleAction("testConnection", It.IsAny<Dictionary<string, string>>(), It.IsAny<BrainarrSettings>()))
                .Returns(actionResult);

            // Act
            var fetchResult = _brainarrImportList.Fetch();
            _brainarrImportList.TestConfiguration(validationFailures);
            var actionQueryResult = _brainarrImportList.RequestAction("testConnection", new Dictionary<string, string>());

            // Assert
            fetchResult.Should().HaveCount(2);
            fetchResult.Should().Contain(r => r.Artist == "The Beatles" && r.Album == "Abbey Road");
            validationFailures.Should().BeEmpty();
            actionQueryResult.Should().BeEquivalentTo(actionResult);

            // Verify all methods called exactly once
            _orchestratorMock.Verify(x => x.FetchRecommendations(It.IsAny<BrainarrSettings>()), Times.Once);
            _orchestratorMock.Verify(x => x.ValidateConfiguration(It.IsAny<BrainarrSettings>(), It.IsAny<List<ValidationFailure>>()), Times.Once);
            _orchestratorMock.Verify(x => x.HandleAction("testConnection", It.IsAny<Dictionary<string, string>>(), It.IsAny<BrainarrSettings>()), Times.Once);
        } */

        #endregion
    }
}
