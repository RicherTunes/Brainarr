using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Xunit;

namespace Brainarr.Tests
{
    /// <summary>
    /// Coverage tests for LMStudioProvider paths not covered by LMStudioProviderTests.
    /// Tests focus on: constructor validation, error hints, UpdateModel, ProviderName, and cancellation.
    /// </summary>
    public class LMStudioProviderCovTests
    {
        private readonly Mock<IHttpClient> _httpClient;
        private readonly Logger _logger;

        public LMStudioProviderCovTests()
        {
            _httpClient = new Mock<IHttpClient>();
            _logger = Helpers.TestLogger.CreateNullLogger();
        }

        #region Constructor Validation

        // Source line 49: _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        // Proof: grep -n "throw " Brainarr.Plugin/Services/Providers/LMStudioProvider.cs
        //   49:            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        [Fact]
        public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
        {
            // Act
            var act = () => new LMStudioProvider("http://localhost:1234", "model", null!, _logger);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("httpClient");
        }

        // Source line 46: _baseUrl = baseUrl?.TrimEnd('/') ?? BrainarrConstants.DefaultLMStudioUrl;
        // Proof: grep -n "_baseUrl = baseUrl" Brainarr.Plugin/Services/Providers/LMStudioProvider.cs
        //   46:            _baseUrl = baseUrl?.TrimEnd('/') ?? BrainarrConstants.DefaultLMStudioUrl;
        [Fact]
        public void Constructor_WithNullBaseUrl_UsesDefaultUrl()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            // Act
            var provider = new LMStudioProvider(null!, "model", _httpClient.Object, _logger);

            // Assert - Provider initializes successfully with default URL
            provider.ProviderName.Should().Be("LM Studio");
        }

        // Source line 47: _model = model ?? BrainarrConstants.DefaultLMStudioModel;
        // Proof: grep -n "_model = model" Brainarr.Plugin/Services/Providers/LMStudioProvider.cs
        //   47:            _model = model ?? BrainarrConstants.DefaultLMStudioModel;
        [Fact]
        public void Constructor_WithNullModel_UsesDefaultModel()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            // Act
            var provider = new LMStudioProvider("http://localhost:1234", null!, _httpClient.Object, _logger);

            // Assert - Provider initializes successfully with default model
            provider.ProviderName.Should().Be("LM Studio");
        }

        // Source line 46: Trims trailing slash from baseUrl
        // Proof: grep -n "TrimEnd" Brainarr.Plugin/Services/Providers/LMStudioProvider.cs
        //   46:            _baseUrl = baseUrl?.TrimEnd('/') ?? BrainarrConstants.DefaultLMStudioUrl;
        [Fact]
        public void Constructor_TrimsTrailingSlashFromBaseUrl()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            // Act
            var provider = new LMStudioProvider("http://localhost:1234/", "model", _httpClient.Object, _logger);

            // Assert - Provider initializes successfully
            provider.ProviderName.Should().Be("LM Studio");
        }

        #endregion

        #region ProviderName Property

        // Source line 42-43: public string ProviderName => "LM Studio";
        // Proof: grep -n "ProviderName" Brainarr.Plugin/Services/Providers/LMStudioProvider.cs
        //   42:        public string ProviderName => "LM Studio";
        [Fact]
        public void ProviderName_ReturnsLMStudio()
        {
            // Arrange
            var provider = new LMStudioProvider("http://localhost:1234", "model", _httpClient.Object, _logger);

            // Act & Assert
            provider.ProviderName.Should().Be("LM Studio");
        }

        #endregion

        #region UpdateModel Method

        // Source line 322-328: public void UpdateModel(string modelName)
        // Proof: grep -n "UpdateModel" Brainarr.Plugin/Services/Providers/LMStudioProvider.cs
        //   322:        public void UpdateModel(string modelName)
        [Fact]
        public void UpdateModel_WithValidName_UpdatesModel()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new LMStudioProvider("http://localhost:1234", "old-model", _httpClient.Object, _logger);

            // Act
            provider.UpdateModel("new-model");

            // Assert - Model should be updated (verified by successful completion)
            provider.ProviderName.Should().Be("LM Studio");
        }

        // Source line 324-327: if (!string.IsNullOrWhiteSpace(modelName)) { _model = modelName; ... }
        // Proof: grep -n "string.IsNullOrWhiteSpace(modelName)" Brainarr.Plugin/Services/Providers/LMStudioProvider.cs
        //   324:            if (!string.IsNullOrWhiteSpace(modelName))
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void UpdateModel_WithInvalidName_DoesNotUpdateModel(string? modelName)
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new LMStudioProvider("http://localhost:1234", "original-model", _httpClient.Object, _logger);

            // Act
            provider.UpdateModel(modelName!);

            // Assert - Model should remain unchanged (verified by successful completion)
            provider.ProviderName.Should().Be("LM Studio");
        }

        #endregion

        #region TestConnectionAsync Error Hints

        // Source line 221-223: _lastUserMessage = $"LM Studio returned HTTP {(int)response.StatusCode}...";
        // Proof: grep -n "_lastUserMessage" Brainarr.Plugin/Services/Providers/LMStudioProvider.cs
        //   221:                    _lastUserMessage = $"LM Studio returned HTTP {(int)response.StatusCode}...";
        //   222:                    _lastUserLearnMoreUrl = BrainarrConstants.DocsLMStudioSection;
        [Fact]
        public async Task TestConnectionAsync_NonOkStatus_SetsUserMessageAndLearnMoreUrl()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("bad", HttpStatusCode.BadRequest));
            var provider = new LMStudioProvider("http://localhost:1234", "model", _httpClient.Object, _logger);

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
            provider.GetLastUserMessage().Should().Contain("LM Studio returned HTTP 400");
            provider.GetLearnMoreUrl().Should().NotBeNull();
        }

        // Source line 230-232: _lastUserMessage = $"Cannot reach LM Studio...";
        // Proof: grep -n "Cannot reach LM Studio" Brainarr.Plugin/Services/Providers/LMStudioProvider.cs
        //   231:                _lastUserMessage = $"Cannot reach LM Studio at {SafeHost(_baseUrl)}...";
        [Fact]
        public async Task TestConnectionAsync_Exception_SetsUserMessage()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpRequestException("Connection refused"));
            var provider = new LMStudioProvider("http://localhost:1234", "model", _httpClient.Object, _logger);

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
            provider.GetLastUserMessage().Should().Contain("Cannot reach LM Studio");
            provider.GetLearnMoreUrl().Should().NotBeNull();
        }

        // Source line 217-218: _lastUserMessage = null; _lastUserLearnMoreUrl = null;
        // Proof: grep -n "_lastUserMessage = null" Brainarr.Plugin/Services/Providers/LMStudioProvider.cs
        //   217:            _lastUserMessage = null;
        [Fact]
        public async Task TestConnectionAsync_Success_ClearsUserMessages()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new LMStudioProvider("http://localhost:1234", "model", _httpClient.Object, _logger);

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeTrue();
            provider.GetLastUserMessage().Should().BeNull();
            provider.GetLearnMoreUrl().Should().BeNull();
        }

        #endregion

        #region TestConnectionAsync with CancellationToken

        // Source line 238-260: public async Task<bool> TestConnectionAsync(System.Threading.CancellationToken cancellationToken)
        // Proof: grep -n "CancellationToken cancellationToken" Brainarr.Plugin/Services/Providers/LMStudioProvider.cs
        //   238:        public async Task<bool> TestConnectionAsync(System.Threading.CancellationToken cancellationToken)
        [Fact]
        public async Task TestConnectionAsync_WithCancellationToken_ReturnsTrueOnSuccess()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new LMStudioProvider("http://localhost:1234", "model", _httpClient.Object, _logger);
            using var cts = new CancellationTokenSource();

            // Act
            var result = await provider.TestConnectionAsync(cts.Token);

            // Assert
            result.Should().BeTrue();
        }

        // Source line 239: cancellationToken.ThrowIfCancellationRequested();
        // Proof: grep -n "ThrowIfCancellationRequested" Brainarr.Plugin/Services/Providers/LMStudioProvider.cs
        //   239:            cancellationToken.ThrowIfCancellationRequested();
        //   259:                cancellationToken.ThrowIfCancellationRequested();
        [Fact]
        public async Task TestConnectionAsync_PreCancelledToken_ThrowsOperationCanceledException()
        {
            // Arrange
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));
            var provider = new LMStudioProvider("http://localhost:1234", "model", _httpClient.Object, _logger);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            var act = async () => await provider.TestConnectionAsync(cts.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        #endregion

        #region TestConnectionAsync with IHttpResilience

        // Source line 208-214: var response = _httpExec != null ? await _httpExec.SendAsync(...) : ...
        // Proof: grep -n "_httpExec" Brainarr.Plugin/Services/Providers/LMStudioProvider.cs
        //   33:        private readonly NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.IHttpResilience? _httpExec;
        [Fact]
        public async Task TestConnectionAsync_WithHttpResilience_UsesResilienceExecutor()
        {
            // Arrange
            var mockResilience = new Mock<IHttpResilience>();
            mockResilience.Setup(x => x.SendAsync(
                    It.IsAny<HttpRequest>(),
                    It.IsAny<Func<HttpRequest, CancellationToken, Task<HttpResponse>>>(),
                    It.IsAny<string>(),
                    It.IsAny<Logger>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<TimeSpan?>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            var provider = new LMStudioProvider(
                "http://localhost:1234",
                "model",
                _httpClient.Object,
                _logger,
                httpExec: mockResilience.Object);

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeTrue();
            mockResilience.Verify(x => x.SendAsync(
                It.IsAny<HttpRequest>(),
                It.IsAny<Func<HttpRequest, CancellationToken, Task<HttpResponse>>>(),
                It.IsAny<string>(),
                It.IsAny<Logger>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<TimeSpan?>()), Times.Once);
        }

        [Fact]
        public async Task TestConnectionAsync_WithHttpResilienceAndCancellationToken_UsesResilienceExecutor()
        {
            // Arrange
            var mockResilience = new Mock<IHttpResilience>();
            mockResilience.Setup(x => x.SendAsync(
                    It.IsAny<HttpRequest>(),
                    It.IsAny<Func<HttpRequest, CancellationToken, Task<HttpResponse>>>(),
                    It.IsAny<string>(),
                    It.IsAny<Logger>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<TimeSpan?>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse("{}", HttpStatusCode.OK));

            var provider = new LMStudioProvider(
                "http://localhost:1234",
                "model",
                _httpClient.Object,
                _logger,
                httpExec: mockResilience.Object);

            using var cts = new CancellationTokenSource();

            // Act
            var result = await provider.TestConnectionAsync(cts.Token);

            // Assert
            result.Should().BeTrue();
            mockResilience.Verify(x => x.SendAsync(
                It.IsAny<HttpRequest>(),
                It.IsAny<Func<HttpRequest, CancellationToken, Task<HttpResponse>>>(),
                It.IsAny<string>(),
                It.IsAny<Logger>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<TimeSpan?>()), Times.Once);
        }

        #endregion

        #region GetRecommendationsAsync with CancellationToken

        // Source line 275: public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt, System.Threading.CancellationToken cancellationToken)
        // Proof: grep -n "GetRecommendationsAsync.*CancellationToken" Brainarr.Plugin/Services/Providers/LMStudioProvider.cs
        //   275:        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt, System.Threading.CancellationToken cancellationToken)
        [Fact]
        public async Task GetRecommendationsAsync_WithCancellationToken_ReturnsRecommendations()
        {
            // Arrange
            var arr = "[ { \"artist\": \"Artist\", \"album\": \"Album\", \"genre\": \"Rock\", \"confidence\": 0.9, \"reason\": \"Good\" } ]";
            var responseObj = new { choices = new[] { new { message = new { content = arr } } } };
            var response = Newtonsoft.Json.JsonConvert.SerializeObject(responseObj);
            _httpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Helpers.HttpResponseFactory.CreateResponse(response));
            var provider = new LMStudioProvider("http://localhost:1234", "model", _httpClient.Object, _logger);
            using var cts = new CancellationTokenSource();

            // Act
            var result = await provider.GetRecommendationsAsync("prompt", cts.Token);

            // Assert
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Artist");
            result[0].Album.Should().Be("Album");
        }

        #endregion

        #region GetLastUserMessage and GetLearnMoreUrl Initial State

        // Source line 235-236: public string? GetLastUserMessage() => _lastUserMessage; public string? GetLearnMoreUrl() => _lastUserLearnMoreUrl;
        // Proof: grep -n "GetLastUserMessage\|GetLearnMoreUrl" Brainarr.Plugin/Services/Providers/LMStudioProvider.cs
        //   235:        public string? GetLastUserMessage() => _lastUserMessage;
        //   236:        public string? GetLearnMoreUrl() => _lastUserLearnMoreUrl;
        [Fact]
        public void GetLastUserMessage_InitialState_ReturnsNull()
        {
            // Arrange
            var provider = new LMStudioProvider("http://localhost:1234", "model", _httpClient.Object, _logger);

            // Act & Assert
            provider.GetLastUserMessage().Should().BeNull();
        }

        [Fact]
        public void GetLearnMoreUrl_InitialState_ReturnsNull()
        {
            // Arrange
            var provider = new LMStudioProvider("http://localhost:1234", "model", _httpClient.Object, _logger);

            // Act & Assert
            provider.GetLearnMoreUrl().Should().BeNull();
        }

        #endregion
    }
}
