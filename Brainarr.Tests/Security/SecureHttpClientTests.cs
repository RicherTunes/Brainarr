using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Brainarr.Plugin.Services.Security;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using Xunit;

namespace Brainarr.Tests.Security
{
    public class SecureHttpClientTests
    {
        private readonly Mock<IHttpClient> _mockHttpClient;
        private readonly Mock<ILogger> _mockLogger;
        private readonly SecureHttpClient _secureHttpClient;

        public SecureHttpClientTests()
        {
            _mockHttpClient = new Mock<IHttpClient>();
            _mockLogger = new Mock<ILogger>();
            _secureHttpClient = new SecureHttpClient(_mockHttpClient.Object, _mockLogger.Object);
        }

        [Theory]
        [InlineData("192.168.1.1", true)]
        [InlineData("192.168.255.255", true)]
        [InlineData("10.0.0.1", true)]
        [InlineData("10.255.255.255", true)]
        [InlineData("172.16.0.1", true)]
        [InlineData("172.31.255.255", true)]
        [InlineData("172.15.0.1", false)] // Not in private range
        [InlineData("172.32.0.1", false)] // Not in private range
        [InlineData("127.0.0.1", true)]
        [InlineData("localhost", true)]
        [InlineData("8.8.8.8", false)]
        [InlineData("google.com", false)]
        public async Task IsLocalUrl_ShouldCorrectlyIdentifyPrivateIPs(string host, bool expectedIsLocal)
        {
            // Arrange
            var url = expectedIsLocal ? $"http://{host}/api/test" : $"https://{host}/api/test";
            var request = new HttpRequestBuilder(url).Build();
            
            var response = new HttpResponse(request)
            {
                StatusCode = HttpStatusCode.OK,
                Headers = new HttpHeader(),
                Content = "{\"result\":\"test\"}"
            };
            
            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            var result = await _secureHttpClient.ExecuteAsync(request);

            // Assert
            Assert.NotNull(result);
            
            // Verify HTTPS upgrade for non-local URLs
            if (!expectedIsLocal && !url.StartsWith("https"))
            {
                _mockLogger.Verify(x => x.Debug("Upgrading HTTP to HTTPS for external request"), 
                    Times.Once);
            }
        }

        [Fact]
        public async Task ExecuteAsync_ShouldEnforceHttpsForExternalUrls()
        {
            // Arrange
            var request = new HttpRequestBuilder("http://api.example.com/endpoint").Build();
            
            var response = new HttpResponse(request)
            {
                StatusCode = HttpStatusCode.OK,
                Headers = new HttpHeader(),
                Content = "{}"
            };
            
            _mockHttpClient.Setup(x => x.ExecuteAsync(It.Is<HttpRequest>(r => 
                    r.Url.ToString().StartsWith("https://"))))
                .ReturnsAsync(response);

            // Act
            var result = await _secureHttpClient.ExecuteAsync(request);

            // Assert
            Assert.NotNull(result);
            _mockHttpClient.Verify(x => x.ExecuteAsync(It.Is<HttpRequest>(r => 
                r.Url.ToString().StartsWith("https://"))), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldValidateCertificatesForHttps()
        {
            // Arrange
            var request = new HttpRequestBuilder("https://secure.example.com/api").Build();
            
            var response = new HttpResponse(request)
            {
                StatusCode = HttpStatusCode.OK,
                Headers = new HttpHeader(),
                Content = "{}"
            };
            
            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            var result = await _secureHttpClient.ExecuteAsync(request);

            // Assert
            Assert.NotNull(result);
            _mockLogger.Verify(x => x.Debug(It.Is<string>(s => 
                s.Contains("certificate validation"))), Times.AtLeastOnce);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldAddSecurityHeaders()
        {
            // Arrange
            var request = new HttpRequestBuilder("https://api.example.com/endpoint").Build();
            
            HttpRequest capturedRequest = null;
            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => capturedRequest = r)
                .ReturnsAsync(new HttpResponse(request)
                {
                    StatusCode = HttpStatusCode.OK,
                    Headers = new HttpHeader(),
                    Content = "{}"
                });

            // Act
            await _secureHttpClient.ExecuteAsync(request);

            // Assert
            Assert.NotNull(capturedRequest);
            Assert.Contains("X-Content-Type-Options", capturedRequest.Headers.Keys);
            Assert.Equal("nosniff", capturedRequest.Headers["X-Content-Type-Options"]);
            Assert.Contains("X-Frame-Options", capturedRequest.Headers.Keys);
            Assert.Equal("DENY", capturedRequest.Headers["X-Frame-Options"]);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldEnforceMaxResponseSize()
        {
            // Arrange
            var request = new HttpRequestBuilder("https://api.example.com/large").Build();
            var largeContent = new string('x', 51 * 1024 * 1024); // 51MB
            
            var response = new HttpResponse(request)
            {
                StatusCode = HttpStatusCode.OK,
                Headers = new HttpHeader { { "Content-Length", largeContent.Length.ToString() } },
                Content = largeContent
            };
            
            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act & Assert
            await Assert.ThrowsAsync<SecureHttpException>(async () =>
                await _secureHttpClient.ExecuteAsync(request));
        }

        [Theory]
        [InlineData("172.16.0.0", true)]
        [InlineData("172.16.255.255", true)]
        [InlineData("172.20.100.50", true)]
        [InlineData("172.31.0.0", true)]
        [InlineData("172.31.255.255", true)]
        [InlineData("172.15.255.255", false)]
        [InlineData("172.32.0.0", false)]
        [InlineData("172.100.0.0", false)]
        public void RFC1918_172Range_ShouldBeCorrectlyValidated(string ip, bool shouldBePrivate)
        {
            // This test specifically validates the RFC 1918 172.16.0.0/12 range
            // which covers 172.16.0.0 - 172.31.255.255
            
            var uri = new Uri($"http://{ip}");
            var method = _secureHttpClient.GetType()
                .GetMethod("IsLocalUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            var result = (bool)method.Invoke(_secureHttpClient, new object[] { uri });
            
            Assert.Equal(shouldBePrivate, result);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldSanitizeUrlInLogs()
        {
            // Arrange
            var sensitiveUrl = "https://api.example.com/users?apiKey=secret123&token=abc";
            var request = new HttpRequestBuilder(sensitiveUrl).Build();
            
            var response = new HttpResponse(request)
            {
                StatusCode = HttpStatusCode.OK,
                Headers = new HttpHeader(),
                Content = "{}"
            };
            
            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            await _secureHttpClient.ExecuteAsync(request);

            // Assert
            _mockLogger.Verify(x => x.Debug(It.Is<string>(s => 
                !s.Contains("secret123") && !s.Contains("token=abc"))), 
                Times.AtLeastOnce);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldHandleTimeouts()
        {
            // Arrange
            var request = new HttpRequestBuilder("https://api.example.com/timeout").Build();
            
            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new TaskCanceledException("Request timeout"));

            // Act & Assert
            var exception = await Assert.ThrowsAsync<SecureHttpException>(async () =>
                await _secureHttpClient.ExecuteAsync(request));
            
            Assert.Contains("timeout", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldNotLogSensitiveHeaders()
        {
            // Arrange
            var request = new HttpRequestBuilder("https://api.example.com/endpoint")
                .SetHeader("Authorization", "Bearer secret-token")
                .SetHeader("X-API-Key", "super-secret-key")
                .Build();
            
            var response = new HttpResponse(request)
            {
                StatusCode = HttpStatusCode.OK,
                Headers = new HttpHeader(),
                Content = "{}"
            };
            
            _mockHttpClient.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);

            // Act
            await _secureHttpClient.ExecuteAsync(request);

            // Assert
            _mockLogger.Verify(x => x.Debug(It.Is<string>(s => 
                !s.Contains("secret-token") && !s.Contains("super-secret-key"))), 
                Times.AtLeastOnce);
        }
    }
}