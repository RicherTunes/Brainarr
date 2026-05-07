using System.Collections.Generic;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests
{
    /// <summary>
    /// Coverage tests for ProviderManager.cs - tests for uncovered paths.
    /// Source: Brainarr.Plugin/Services/Core/ProviderManager.cs
    /// </summary>
    public class ProviderManagerCovTests
    {
        private readonly Mock<IHttpClient> _httpClientMock;
        private readonly Mock<IProviderFactory> _providerFactoryMock;
        private readonly Mock<IRetryPolicy> _retryPolicyMock;
        private readonly Mock<IRateLimiter> _rateLimiterMock;
        private readonly Logger _logger;

        public ProviderManagerCovTests()
        {
            _httpClientMock = new Mock<IHttpClient>();
            _providerFactoryMock = new Mock<IProviderFactory>();
            _retryPolicyMock = new Mock<IRetryPolicy>();
            _rateLimiterMock = new Mock<IRateLimiter>();
            _logger = TestLogger.CreateNullLogger();
        }

        private ProviderManager CreateSut()
        {
            var modelDetection = new ModelDetectionService(_httpClientMock.Object, _logger);
            return new ProviderManager(
                _httpClientMock.Object,
                _providerFactoryMock.Object,
                modelDetection,
                _retryPolicyMock.Object,
                _rateLimiterMock.Object,
                _logger);
        }

        #region GetCurrentProvider Tests (Lines 41-47)

        [Fact]
        public void GetCurrentProvider_WhenNotInitialized_ReturnsNull()
        {
            // Arrange
            var sut = CreateSut();

            // Act
            var result = sut.GetCurrentProvider();

            // Assert - Source line 45: return _currentProvider (null when not initialized)
            result.Should().BeNull("because provider was never initialized");
        }

        #endregion

        #region IsProviderReady Tests (Lines 163-166)

        [Fact]
        public void IsProviderReady_WhenNotInitialized_ReturnsFalse()
        {
            // Arrange
            var sut = CreateSut();

            // Act
            var result = sut.IsProviderReady();

            // Assert - Source line 165: return _currentProvider != null
            result.Should().BeFalse("because _currentProvider is null before initialization");
        }

        #endregion

        #region Dispose Tests (Lines 168-171)

        [Fact]
        public void Dispose_WhenNoProvider_DoesNotThrow()
        {
            // Arrange
            var sut = CreateSut();

            // Act - Source lines 168-171: calls DisposeCurrentProvider which handles null provider
            var act = () => sut.Dispose();

            // Assert
            act.Should().NotThrow("because DisposeCurrentProvider handles null provider at line 262");
        }

        #endregion

        #region UpdateProvider Tests (Lines 85-100)

        [Fact]
        public void UpdateProvider_WithSameSettingsAndSameModel_DoesNotCallUpdateModel()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                BaseUrl = "https://api.openai.com",
                OpenAIModel = "gpt-4"
            };

            var mockProvider = new Mock<IAIProvider>();
            _providerFactoryMock.Setup(x => x.CreateProvider(
                It.IsAny<BrainarrSettings>(),
                It.IsAny<IHttpClient>(),
                It.IsAny<Logger>()))
                .Returns(mockProvider.Object);

            var sut = CreateSut();
            sut.InitializeProvider(settings);

            // Act - Same settings (provider current) and same model
            // Source lines 86-100: if provider is current and model unchanged, UpdateModel not called
            sut.UpdateProvider(settings);

            // Assert - UpdateModel should NOT be called since model hasn't changed
            // Line 93-99 only executes if HasModelChanged returns true
            mockProvider.Verify(x => x.UpdateModel(It.IsAny<string>()), Times.Never,
                "because model has not changed (line 93: HasModelChanged check)");
        }

        #endregion

        #region SelectBestModel Size Bonus Tests (Lines 241-258)

        [Fact]
        public void SelectBestModel_With70bModel_GetsHighestSizeBonus()
        {
            // Arrange
            var sut = CreateSut();
            var models = new List<string>
            {
                "llama3-8b-instruct",   // base 100 + 5 = 105
                "llama3-70b-instruct"   // base 100 + 20 = 120 (line 248: 70b bonus)
            };

            // Act
            var best = sut.SelectBestModel(models);

            // Assert - Source line 248: if model.Contains("70b") sizeBonus = 20
            best.Should().Be("llama3-70b-instruct",
                "because 70b gets +20 bonus (line 248: sizeBonus = 20)");
        }

        [Fact]
        public void SelectBestModel_With34bModel_GetsSizeBonus15()
        {
            // Arrange
            var sut = CreateSut();
            var models = new List<string>
            {
                "mixtral-8b",    // base 83 + 5 = 88
                "mixtral-34b"    // base 83 + 15 = 98 (line 249: 34b bonus)
            };

            // Act
            var best = sut.SelectBestModel(models);

            // Assert - Source line 249: if model.Contains("34b") sizeBonus = 15
            best.Should().Be("mixtral-34b",
                "because 34b gets +15 bonus (line 249: sizeBonus = 15 for 34b)");
        }

        [Fact]
        public void SelectBestModel_With33bModel_GetsSizeBonus15()
        {
            // Arrange
            var sut = CreateSut();
            var models = new List<string>
            {
                "mistral-8b",    // base 85 + 5 = 90
                "mistral-33b"    // base 85 + 15 = 100 (line 249: 33b bonus)
            };

            // Act
            var best = sut.SelectBestModel(models);

            // Assert - Source line 249: if model.Contains("33b") sizeBonus = 15
            best.Should().Be("mistral-33b",
                "because 33b gets +15 bonus (line 249: sizeBonus = 15 for 33b)");
        }

        [Fact]
        public void SelectBestModel_With13bModel_GetsSizeBonus10()
        {
            // Arrange
            var sut = CreateSut();
            var models = new List<string>
            {
                "llama2-7b",     // base 90 + 5 = 95
                "llama2-13b"     // base 90 + 10 = 100 (line 250: 13b bonus)
            };

            // Act
            var best = sut.SelectBestModel(models);

            // Assert - Source line 250: if model.Contains("13b") sizeBonus = 10
            best.Should().Be("llama2-13b",
                "because 13b gets +10 bonus (line 250: sizeBonus = 10)");
        }

        [Fact]
        public void SelectBestModel_With7bModel_GetsSizeBonus5()
        {
            // Arrange
            var sut = CreateSut();
            var models = new List<string>
            {
                "llama3",        // base 100 + 0 = 100 (no size bonus)
                "llama3-7b"      // base 100 + 5 = 105 (line 251: 7b bonus)
            };

            // Act
            var best = sut.SelectBestModel(models);

            // Assert - Source line 251: if model.Contains("7b") sizeBonus = 5
            best.Should().Be("llama3-7b",
                "because 7b gets +5 bonus (line 251: sizeBonus = 5 for 7b)");
        }

        [Fact]
        public void SelectBestModel_WithOnlyUnknownModels_ReturnsShortestByName()
        {
            // Arrange
            var sut = CreateSut();
            var models = new List<string>
            {
                "unknown-very-long-model-name",  // score 0, long name
                "xyz-short"                       // score 0, short name
            };

            // Act - Source lines 153-155: OrderByDescending(score).ThenBy(length)
            var best = sut.SelectBestModel(models);

            // Assert - Source line 257: return 0 for unknown models
            // Line 154: ThenBy(x => x.Model.Length) - shortest wins on tie
            best.Should().Be("xyz-short",
                "because unknown models get score 0 (line 257), then sorted by length ascending (line 154)");
        }

        #endregion

        #region InitializeProvider Auto-Detection Tests (Lines 184-190)

        [Fact]
        public void InitializeProvider_WithLMStudioAutoDetection_CreatesProvider()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.LMStudio,
                BaseUrl = "http://localhost:1234",
                EnableAutoDetection = true  // Proxies to AutoDetectModel
            };

            var mockProvider = new Mock<IAIProvider>();
            _providerFactoryMock.Setup(x => x.CreateProvider(
                It.IsAny<BrainarrSettings>(),
                It.IsAny<IHttpClient>(),
                It.IsAny<Logger>()))
                .Returns(mockProvider.Object);

            var sut = CreateSut();

            // Act - Source lines 186-189: LMStudio also triggers auto-detection
            sut.InitializeProvider(settings);

            // Assert - Provider should be created and ready
            sut.IsProviderReady().Should().BeTrue(
                "because LMStudio with AutoDetectModel=true should initialize (lines 187-189)");
        }

        #endregion

        #region SelectBestModel Tie-Breaker Tests (Lines 147-157)

        [Fact]
        public void SelectBestModel_WithSameScore_PrefersShorterName()
        {
            // Arrange
            var sut = CreateSut();
            var models = new List<string>
            {
                "llama3-instruct-v2-long",  // score 100, long name
                "llama3-short"               // score 100, short name
            };

            // Act - Source line 154: ThenBy(x => x.Model.Length)
            var best = sut.SelectBestModel(models);

            // Assert
            best.Should().Be("llama3-short",
                "because on same score, shorter name wins (line 154: ThenBy(x => x.Model.Length))");
        }

        #endregion
    }
}
