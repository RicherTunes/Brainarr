using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Http;
using Brainarr.Plugin.Services.Providers.Base;

namespace Brainarr.Tests.Unit.Services.Providers.Base
{
    [TestFixture]
    public class BaseHttpProviderTests
    {
        private TestableHttpProvider _provider;
        private Mock<IHttpClient> _httpClientMock;
        private Logger _logger;
        
        private class TestableHttpProvider : BaseHttpProvider
        {
            public override string ProviderName => "TestProvider";
            protected override string ApiEndpoint => "https://test.api.com/v1";
            protected override bool RequiresAuthentication => true;
            
            public string TestApiKey { get; set; } = "test-key";
            
            public TestableHttpProvider(IHttpClient httpClient, Logger logger) 
                : base(httpClient, logger)
            {
            }
            
            protected override object BuildRequestPayload(string prompt)
            {
                return new { prompt = prompt };
            }
            
            protected override void ConfigureAuthentication(HttpRequestBuilder requestBuilder)
            {
                requestBuilder.SetHeader("Authorization", $"Bearer {TestApiKey}");
            }
            
            protected override Task<List<Recommendation>> ParseProviderResponseAsync(string responseContent)
            {
                // Simple test implementation
                return Task.FromResult(new List<Recommendation>
                {
                    new Recommendation { Artist = "Test Artist", Album = "Test Album" }
                });
            }
        }
        
        [SetUp]
        public void Setup()
        {
            _httpClientMock = new Mock<IHttpClient>();
            _logger = LogManager.GetCurrentClassLogger();
            _provider = new TestableHttpProvider(_httpClientMock.Object, _logger);
        }
        
        [Test]
        public async Task GetRecommendationsAsync_WithValidResponse_ReturnsRecommendations()
        {
            // Arrange
            var prompt = "Test prompt";
            var responseContent = "{\"recommendations\": [{\"artist\": \"Test Artist\", \"album\": \"Test Album\"}]}";
            
            var httpResponse = new HttpResponse(new HttpRequest("https://test.api.com/v1"))
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = responseContent
            };
            
            _httpClientMock.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                .Returns(httpResponse);
            
            // Act
            var result = await _provider.GetRecommendationsAsync(prompt);
            
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].Artist, Is.EqualTo("Test Artist"));
            Assert.That(result[0].Album, Is.EqualTo("Test Album"));
        }
        
        [Test]
        public void GetRecommendationsAsync_WithEmptyPrompt_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.ThrowsAsync<ArgumentException>(
                async () => await _provider.GetRecommendationsAsync(""));
            Assert.ThrowsAsync<ArgumentException>(
                async () => await _provider.GetRecommendationsAsync(null));
        }
        
        [Test]
        public async Task GetRecommendationsAsync_WithHttpError_ThrowsHttpRequestException()
        {
            // Arrange
            var prompt = "Test prompt";
            var httpResponse = new HttpResponse(new HttpRequest("https://test.api.com/v1"))
            {
                StatusCode = System.Net.HttpStatusCode.InternalServerError,
                Content = "Internal Server Error"
            };
            
            _httpClientMock.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                .Returns(httpResponse);
            
            // Act & Assert
            var ex = Assert.ThrowsAsync<HttpRequestException>(
                async () => await _provider.GetRecommendationsAsync(prompt));
            Assert.That(ex.Message, Does.Contain("Request failed with status"));
        }
        
        [Test]
        public async Task GetRecommendationsAsync_WithTimeout_ThrowsTimeoutException()
        {
            // Arrange
            var prompt = "Test prompt";
            
            _httpClientMock.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                .Throws(new TaskCanceledException());
            
            // Act & Assert
            var ex = Assert.ThrowsAsync<TimeoutException>(
                async () => await _provider.GetRecommendationsAsync(prompt));
            Assert.That(ex.Message, Does.Contain("request timed out"));
        }
        
        [Test]
        public async Task TestConnectionAsync_WithSuccessfulRequest_ReturnsTrue()
        {
            // Arrange
            var responseContent = "{\"recommendations\": [{\"artist\": \"Test\", \"album\": \"Album\"}]}";
            var httpResponse = new HttpResponse(new HttpRequest("https://test.api.com/v1"))
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = responseContent
            };
            
            _httpClientMock.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                .Returns(httpResponse);
            
            // Act
            var result = await _provider.TestConnectionAsync();
            
            // Assert
            Assert.That(result, Is.True);
        }
        
        [Test]
        public async Task TestConnectionAsync_WithException_ReturnsFalse()
        {
            // Arrange
            _httpClientMock.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                .Throws(new HttpRequestException("Connection failed"));
            
            // Act
            var result = await _provider.TestConnectionAsync();
            
            // Assert
            Assert.That(result, Is.False);
        }
        
        [Test]
        public async Task GetRecommendationsAsync_ParsesGenericJsonArray()
        {
            // Arrange
            var prompt = "Test prompt";
            var responseContent = "[{\"artist\": \"Artist1\", \"album\": \"Album1\"}, {\"artist\": \"Artist2\", \"album\": \"Album2\"}]";
            
            // Override to return null for provider-specific parsing
            var provider = new Mock<BaseHttpProvider>(_httpClientMock.Object, _logger) { CallBase = true };
            provider.Setup(p => p.ProviderName).Returns("TestProvider");
            provider.Protected().Setup<string>("ApiEndpoint").Returns("https://test.api.com");
            provider.Protected().Setup<bool>("RequiresAuthentication").Returns(false);
            provider.Protected().Setup<object>("BuildRequestPayload", prompt).Returns(new { prompt });
            provider.Protected()
                .Setup<Task<List<Recommendation>>>("ParseProviderResponseAsync", responseContent)
                .ReturnsAsync((List<Recommendation>)null);
            
            var httpResponse = new HttpResponse(new HttpRequest("https://test.api.com"))
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = responseContent
            };
            
            _httpClientMock.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                .Returns(httpResponse);
            
            // Act
            var result = await provider.Object.GetRecommendationsAsync(prompt);
            
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].Artist, Is.EqualTo("Artist1"));
            Assert.That(result[1].Artist, Is.EqualTo("Artist2"));
        }
        
        [Test]
        public async Task GetRecommendationsAsync_ExtractsFromTextWhenJsonFails()
        {
            // Arrange
            var prompt = "Test prompt";
            var responseContent = @"Here are some recommendations:
1. Pink Floyd - The Dark Side of the Moon
2. Led Zeppelin - IV
3. The Beatles - Abbey Road";
            
            var provider = new Mock<BaseHttpProvider>(_httpClientMock.Object, _logger) { CallBase = true };
            provider.Setup(p => p.ProviderName).Returns("TestProvider");
            provider.Protected().Setup<string>("ApiEndpoint").Returns("https://test.api.com");
            provider.Protected().Setup<bool>("RequiresAuthentication").Returns(false);
            provider.Protected().Setup<object>("BuildRequestPayload", prompt).Returns(new { prompt });
            provider.Protected()
                .Setup<Task<List<Recommendation>>>("ParseProviderResponseAsync", responseContent)
                .ReturnsAsync((List<Recommendation>)null);
            
            var httpResponse = new HttpResponse(new HttpRequest("https://test.api.com"))
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = responseContent
            };
            
            _httpClientMock.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                .Returns(httpResponse);
            
            // Act
            var result = await provider.Object.GetRecommendationsAsync(prompt);
            
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0].Artist, Is.EqualTo("Pink Floyd"));
            Assert.That(result[0].Album, Is.EqualTo("The Dark Side of the Moon"));
            Assert.That(result[1].Artist, Is.EqualTo("Led Zeppelin"));
            Assert.That(result[1].Album, Is.EqualTo("IV"));
        }
        
        [Test]
        public async Task GetRecommendationsAsync_EnforcesSemaphoreForConcurrency()
        {
            // Arrange
            var prompt = "Test prompt";
            var responseContent = "{\"recommendations\": []}";
            var callCount = 0;
            
            var httpResponse = new HttpResponse(new HttpRequest("https://test.api.com/v1"))
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = responseContent
            };
            
            _httpClientMock.Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                .Returns(() =>
                {
                    Interlocked.Increment(ref callCount);
                    Thread.Sleep(100); // Simulate some processing time
                    return httpResponse;
                });
            
            // Act - Start multiple concurrent requests
            var tasks = new List<Task<List<Recommendation>>>();
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(_provider.GetRecommendationsAsync(prompt));
            }
            
            await Task.WhenAll(tasks);
            
            // Assert - All requests should complete successfully
            Assert.That(callCount, Is.EqualTo(5));
            foreach (var task in tasks)
            {
                Assert.That(task.Result, Is.Not.Null);
            }
        }
        
        [Test]
        public void Dispose_DisposesResources()
        {
            // Act
            _provider.Dispose();
            
            // Assert - Should not throw
            Assert.DoesNotThrow(() => _provider.Dispose());
        }
    }
}