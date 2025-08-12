using System;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class AIProviderFactoryTests
    {
        private readonly AIProviderFactory _factory;
        private readonly Mock<IHttpClient> _httpClientMock;
        private readonly Mock<Logger> _loggerMock;

        public AIProviderFactoryTests()
        {
            _factory = new AIProviderFactory();
            _httpClientMock = new Mock<IHttpClient>();
            _loggerMock = new Mock<Logger>();
        }

        [Fact]
        public void CreateProvider_WithOllamaSettings_ReturnsOllamaProvider()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434",
                OllamaModel = "llama2"
            };

            // Act
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _loggerMock.Object);

            // Assert
            provider.Should().NotBeNull();
            provider.Should().BeOfType<OllamaProvider>();
            provider.ProviderName.Should().Be("Ollama");
        }

        [Fact]
        public void CreateProvider_WithLMStudioSettings_ReturnsLMStudioProvider()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.LMStudio,
                LMStudioUrl = "http://localhost:1234",
                LMStudioModel = "model"
            };

            // Act
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _loggerMock.Object);

            // Assert
            provider.Should().NotBeNull();
            provider.Should().BeOfType<LMStudioProvider>();
            provider.ProviderName.Should().Be("LM Studio");
        }

        [Fact]
        public void CreateProvider_WithNullSettings_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                _factory.CreateProvider(null, _httpClientMock.Object, _loggerMock.Object));
        }

        [Fact]
        public void CreateProvider_WithNullHttpClient_ThrowsArgumentNullException()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama };

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                _factory.CreateProvider(settings, null, _loggerMock.Object));
        }

        [Fact]
        public void CreateProvider_WithNullLogger_ThrowsArgumentNullException()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama };

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                _factory.CreateProvider(settings, _httpClientMock.Object, null));
        }

        [Theory]
        [InlineData((AIProvider)999)]
        [InlineData((AIProvider)(-1))]
        public void CreateProvider_WithUnsupportedProvider_ThrowsNotSupportedException(AIProvider provider)
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = provider };

            // Act & Assert
            var exception = Assert.Throws<NotSupportedException>(() =>
                _factory.CreateProvider(settings, _httpClientMock.Object, _loggerMock.Object));
            
            exception.Message.Should().Contain($"Provider type {provider} is not supported");
        }

        [Fact]
        public void IsProviderAvailable_WithOllamaAndUrl_ReturnsTrue()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                OllamaUrl = "http://localhost:11434"
            };

            // Act
            var result = _factory.IsProviderAvailable(AIProvider.Ollama, settings);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void IsProviderAvailable_WithOllamaDefaultUrl_ReturnsTrue()
        {
            // Arrange
            var settings = new BrainarrSettings();
            // Default constructor sets the URL, so it should be available

            // Act
            var result = _factory.IsProviderAvailable(AIProvider.Ollama, settings);

            // Assert
            result.Should().BeTrue(); // Ollama with default URL is considered available
            settings.OllamaUrl.Should().NotBeNullOrWhiteSpace(); // Should have default URL
        }

        [Fact]
        public void IsProviderAvailable_WithLMStudioAndUrl_ReturnsTrue()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                LMStudioUrl = "http://localhost:1234"
            };

            // Act
            var result = _factory.IsProviderAvailable(AIProvider.LMStudio, settings);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void CreateProvider_UsesDefaultUrlWhenNotSpecified()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = null,
                OllamaModel = null
            };

            // Act
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _loggerMock.Object);

            // Assert
            provider.Should().NotBeNull();
            provider.Should().BeOfType<OllamaProvider>();
        }

        [Fact]
        public void CreateProvider_CanCreateMultipleProviders_WithDifferentSettings()
        {
            // Arrange
            var ollamaSettings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434"
            };

            var lmStudioSettings = new BrainarrSettings
            {
                Provider = AIProvider.LMStudio,
                LMStudioUrl = "http://localhost:1234"
            };

            // Act
            var provider1 = _factory.CreateProvider(ollamaSettings, _httpClientMock.Object, _loggerMock.Object);
            var provider2 = _factory.CreateProvider(lmStudioSettings, _httpClientMock.Object, _loggerMock.Object);

            // Assert
            provider1.Should().NotBeNull();
            provider2.Should().NotBeNull();
            provider1.Should().NotBeSameAs(provider2);
            provider1.ProviderName.Should().NotBe(provider2.ProviderName);
        }

        [Fact]
        public void CreateProvider_WithPerplexitySettings_ReturnsPerplexityProvider()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Perplexity,
                PerplexityApiKey = "pplx-test-key-123",
                PerplexityModel = "Sonar_Large"
            };

            // Act
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _loggerMock.Object);

            // Assert
            provider.Should().NotBeNull();
            provider.Should().BeOfType<PerplexityProvider>();
            provider.ProviderName.Should().Be("Perplexity");
        }

        [Fact]
        public void CreateProvider_WithOpenAISettings_ReturnsOpenAIProvider()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                OpenAIApiKey = "sk-test-key-123",
                OpenAIModel = "GPT4o_Mini"
            };

            // Act
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _loggerMock.Object);

            // Assert
            provider.Should().NotBeNull();
            provider.Should().BeOfType<OpenAIProvider>();
            provider.ProviderName.Should().Be("OpenAI");
        }

        [Fact]
        public void CreateProvider_WithAnthropicSettings_ReturnsAnthropicProvider()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Anthropic,
                AnthropicApiKey = "sk-ant-test-key-123",
                AnthropicModel = "Claude35_Haiku"
            };

            // Act
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _loggerMock.Object);

            // Assert
            provider.Should().NotBeNull();
            provider.Should().BeOfType<AnthropicProvider>();
            provider.ProviderName.Should().Be("Anthropic");
        }

        [Fact]
        public void CreateProvider_WithOpenRouterSettings_ReturnsOpenRouterProvider()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenRouter,
                OpenRouterApiKey = "or-test-key-123",
                OpenRouterModel = "Claude35_Haiku"
            };

            // Act
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _loggerMock.Object);

            // Assert
            provider.Should().NotBeNull();
            provider.Should().BeOfType<OpenRouterProvider>();
            provider.ProviderName.Should().Be("OpenRouter");
        }

        [Fact]
        public void CreateProvider_WithDeepSeekSettings_ReturnsDeepSeekProvider()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.DeepSeek,
                DeepSeekApiKey = "ds-test-key-123",
                DeepSeekModel = "DeepSeek_Chat"
            };

            // Act
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _loggerMock.Object);

            // Assert
            provider.Should().NotBeNull();
            provider.Should().BeOfType<DeepSeekProvider>();
            provider.ProviderName.Should().Be("DeepSeek");
        }

        [Fact]
        public void CreateProvider_WithGeminiSettings_ReturnsGeminiProvider()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Gemini,
                GeminiApiKey = "AIza-test-key-123",
                GeminiModel = "Gemini_15_Flash"
            };

            // Act
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _loggerMock.Object);

            // Assert
            provider.Should().NotBeNull();
            provider.Should().BeOfType<GeminiProvider>();
            provider.ProviderName.Should().Be("Google Gemini");
        }

        [Fact]
        public void CreateProvider_WithGroqSettings_ReturnsGroqProvider()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Groq,
                GroqApiKey = "gsk-test-key-123",
                GroqModel = "Llama33_70B"
            };

            // Act
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _loggerMock.Object);

            // Assert
            provider.Should().NotBeNull();
            provider.Should().BeOfType<GroqProvider>();
            provider.ProviderName.Should().Be("Groq");
        }

        [Theory]
        [InlineData(AIProvider.Perplexity, "PerplexityApiKey")]
        [InlineData(AIProvider.OpenAI, "OpenAIApiKey")]
        [InlineData(AIProvider.Anthropic, "AnthropicApiKey")]
        [InlineData(AIProvider.OpenRouter, "OpenRouterApiKey")]
        [InlineData(AIProvider.DeepSeek, "DeepSeekApiKey")]
        [InlineData(AIProvider.Gemini, "GeminiApiKey")]
        [InlineData(AIProvider.Groq, "GroqApiKey")]
        public void IsProviderAvailable_WithApiKey_ReturnsTrue(AIProvider provider, string apiKeyProperty)
        {
            // Arrange
            var settings = new BrainarrSettings();
            var propertyInfo = typeof(BrainarrSettings).GetProperty(apiKeyProperty);
            propertyInfo.SetValue(settings, "test-api-key");

            // Act
            var result = _factory.IsProviderAvailable(provider, settings);

            // Assert
            result.Should().BeTrue();
        }

        [Theory]
        [InlineData(AIProvider.Perplexity)]
        [InlineData(AIProvider.OpenAI)]
        [InlineData(AIProvider.Anthropic)]
        [InlineData(AIProvider.OpenRouter)]
        [InlineData(AIProvider.DeepSeek)]
        [InlineData(AIProvider.Gemini)]
        [InlineData(AIProvider.Groq)]
        public void IsProviderAvailable_WithoutApiKey_ReturnsFalse(AIProvider provider)
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            var result = _factory.IsProviderAvailable(provider, settings);

            // Assert
            result.Should().BeFalse();
        }
    }
}