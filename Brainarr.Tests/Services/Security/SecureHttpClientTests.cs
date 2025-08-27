using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Brainarr.Tests.Helpers;
using NLog;
using NzbDrone.Common.Http;
using Brainarr.Plugin.Services.Security;
using Xunit;

namespace Brainarr.Tests.Services.Security
{
    [Trait("Category", "Security")]
    public class SecureHttpClientTests
    {
        private readonly Mock<IHttpClient> _httpClientMock;
        private readonly Logger _logger;
        private readonly SecureHttpClient _secureClient;

        public SecureHttpClientTests()
        {
            _httpClientMock = new Mock<IHttpClient>();
            _logger = TestLogger.CreateNullLogger();
            _secureClient = new SecureHttpClient(_httpClientMock.Object, _logger);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidDependencies_InitializesSuccessfully()
        {
            // Arrange & Act
            var client = new SecureHttpClient(_httpClientMock.Object, _logger);

            // Assert
            client.Should().NotBeNull();
        }

        [Fact]
        public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new SecureHttpClient(null, _logger));
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new SecureHttpClient(_httpClientMock.Object, null));
        }

        [Fact]
        public void Constructor_WithNullSecurityConfig_UsesDefault()
        {
            // Act & Assert - Should not throw with null security config
            var client = new SecureHttpClient(_httpClientMock.Object, _logger, null);
            client.Should().NotBeNull();
        }

        #endregion

        #region CreateSecureRequest Tests

        [Theory]
        [InlineData("https://api.openai.com/v1/chat")]
        [InlineData("https://api.anthropic.com/v1/messages")]
        [InlineData("http://localhost:11434/api/generate")]
        public void CreateSecureRequest_WithValidUrl_CreatesRequestBuilder(string url)
        {
            // Act
            var requestBuilder = _secureClient.CreateSecureRequest(url);

            // Assert
            requestBuilder.Should().NotBeNull();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void CreateSecureRequest_WithInvalidUrl_ThrowsException(string invalidUrl)
        {
            // Act & Assert
            _secureClient.Invoking(c => c.CreateSecureRequest(invalidUrl))
                .Should().Throw<ArgumentException>();
        }

        [Theory]
        [InlineData("ftp://invalid.com")]
        [InlineData("file:///local/path")]
        [InlineData("javascript:alert()")]
        public void CreateSecureRequest_WithUnsafeScheme_ThrowsException(string unsafeUrl)
        {
            // Act & Assert
            _secureClient.Invoking(c => c.CreateSecureRequest(unsafeUrl))
                .Should().Throw<ArgumentException>();
        }

        [Fact]
        public void CreateSecureRequest_WithHttpsUrl_AllowsSecureConnection()
        {
            // Act
            var requestBuilder = _secureClient.CreateSecureRequest("https://api.example.com");

            // Assert
            requestBuilder.Should().NotBeNull();
        }

        [Fact]
        public void CreateSecureRequest_WithHttpLocalhost_AllowsDevelopmentConnection()
        {
            // Act & Assert - Should allow HTTP for localhost (development)
            _secureClient.Invoking(c => c.CreateSecureRequest("http://localhost:11434"))
                .Should().NotThrow();
        }

        #endregion

        #region ExecuteAsync Tests

        [Fact]
        public async Task ExecuteAsync_WithValidRequest_ExecutesSuccessfully()
        {
            // Arrange
            var request = new HttpRequest("https://api.example.com/test");
            var expectedResponse = HttpResponseFactory.CreateResponse("Success", HttpStatusCode.OK);
            
            _httpClientMock
                .Setup(c => c.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(expectedResponse);

            // Act
            var response = await _secureClient.ExecuteAsync(request);

            // Assert
            response.Should().NotBeNull();
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            _httpClientMock.Verify(c => c.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WithNullRequest_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _secureClient.ExecuteAsync(null));
        }

        [Fact]
        public async Task ExecuteAsync_WhenHttpClientThrows_PropagatesException()
        {
            // Arrange
            var request = new HttpRequest("https://api.example.com/test");
            var expectedException = new InvalidOperationException("Network error");
            
            _httpClientMock
                .Setup(c => c.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(expectedException);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _secureClient.ExecuteAsync(request));
            exception.Should().BeSameAs(expectedException);
        }

        [Theory]
        [InlineData(HttpStatusCode.OK)]
        [InlineData(HttpStatusCode.Created)]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public async Task ExecuteAsync_WithVariousStatusCodes_ReturnsResponse(HttpStatusCode statusCode)
        {
            // Arrange
            var request = new HttpRequest("https://api.example.com/test");
            var expectedResponse = HttpResponseFactory.CreateResponse("Response content", statusCode);
            
            _httpClientMock
                .Setup(c => c.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(expectedResponse);

            // Act
            var response = await _secureClient.ExecuteAsync(request);

            // Assert
            response.Should().NotBeNull();
            response.StatusCode.Should().Be(statusCode);
        }

        #endregion

        #region Security Validation Tests

        [Fact]
        public async Task ExecuteAsync_WithInsecureRequest_ValidatesAndSecures()
        {
            // Arrange
            var request = new HttpRequest("https://api.example.com/test");
            var response = HttpResponseFactory.CreateResponse("Success", HttpStatusCode.OK);
            
            _httpClientMock
                .Setup(c => c.ExecuteAsync(It.Is<HttpRequest>(r => 
                    r.Headers.ContainsKey("X-Content-Type-Options"))))
                .ReturnsAsync(response);

            // Act
            var result = await _secureClient.ExecuteAsync(request);

            // Assert
            result.Should().NotBeNull();
            _httpClientMock.Verify(c => c.ExecuteAsync(It.Is<HttpRequest>(r => 
                r.Headers.ContainsKey("X-Content-Type-Options"))), Times.Once);
        }

        #endregion

        #region Performance Tests

        [Fact]
        public async Task ExecuteAsync_HighVolumeRequests_PerformsEfficiently()
        {
            // Arrange
            const int requestCount = 100;
            var request = new HttpRequest("https://api.example.com/test");
            var response = HttpResponseFactory.CreateResponse("Success", HttpStatusCode.OK);
            
            _httpClientMock
                .Setup(c => c.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            var startTime = DateTime.UtcNow;

            // Act
            var tasks = Enumerable.Range(0, requestCount)
                .Select(_ => _secureClient.ExecuteAsync(request))
                .ToArray();

            var responses = await Task.WhenAll(tasks);
            var elapsed = DateTime.UtcNow - startTime;

            // Assert
            elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5)); // Should complete efficiently
            responses.Should().HaveCount(requestCount);
            responses.Should().AllSatisfy(r => 
            {
                r.Should().NotBeNull();
                r.StatusCode.Should().Be(HttpStatusCode.OK);
            });
        }

        #endregion

        #region Integration Tests

        [Fact]
        public async Task CreateSecureRequestAndExecute_Integration_WorksTogether()
        {
            // Arrange
            const string testUrl = "https://api.example.com/v1/test";
            var expectedResponse = HttpResponseFactory.CreateResponse("Integration test response", HttpStatusCode.OK);
            
            _httpClientMock
                .Setup(c => c.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(expectedResponse);

            // Act
            var requestBuilder = _secureClient.CreateSecureRequest(testUrl);
            var request = requestBuilder.Build();
            var response = await _secureClient.ExecuteAsync(request);

            // Assert
            response.Should().NotBeNull();
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Should().Be("Integration test response");
        }

        #endregion

        #region Error Handling Tests

        [Theory]
        [InlineData(typeof(TaskCanceledException))]
        [InlineData(typeof(HttpRequestException))]
        [InlineData(typeof(TimeoutException))]
        public async Task ExecuteAsync_WithNetworkErrors_PropagatesCorrectException(Type exceptionType)
        {
            // Arrange
            var request = new HttpRequest("https://api.example.com/test");
            var networkException = (Exception)Activator.CreateInstance(exceptionType, "Network error");
            
            _httpClientMock
                .Setup(c => c.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(networkException);

            // Act & Assert
            var exception = await Assert.ThrowsAsync(exceptionType, () => 
                _secureClient.ExecuteAsync(request));
            exception.Message.Should().Contain("Network error");
        }

        #endregion

        #region Thread Safety Tests

        [Fact]
        public async Task ExecuteAsync_ConcurrentRequests_ThreadSafe()
        {
            // Arrange
            const int concurrentRequests = 20;
            var request = new HttpRequest("https://api.example.com/concurrent");
            var response = HttpResponseFactory.CreateResponse("Concurrent response", HttpStatusCode.OK);
            
            _httpClientMock
                .Setup(c => c.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            var tasks = Enumerable.Range(0, concurrentRequests)
                .Select(_ => _secureClient.ExecuteAsync(request))
                .ToArray();

            var responses = await Task.WhenAll(tasks);

            // Assert
            responses.Should().HaveCount(concurrentRequests);
            responses.Should().AllSatisfy(r => 
            {
                r.Should().NotBeNull();
                r.StatusCode.Should().Be(HttpStatusCode.OK);
                r.Content.Should().Be("Concurrent response");
            });
            
            _httpClientMock.Verify(c => c.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Exactly(concurrentRequests));
        }

        #endregion
    }
}