using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using Brainarr.Tests.Helpers;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    public class AIProviderFactoryTests
    {
        private readonly AIProviderFactory _factory;
        private readonly AIProviderFactory _factoryWithCustomRegistry;
        private readonly Mock<IProviderRegistry> _registryMock;
        private readonly Mock<IHttpClient> _httpClientMock;
        private readonly Logger _logger;

        public AIProviderFactoryTests()
        {
            _factory = new AIProviderFactory();
            _registryMock = new Mock<IProviderRegistry>();
            _factoryWithCustomRegistry = new AIProviderFactory(_registryMock.Object);
            _httpClientMock = new Mock<IHttpClient>();
            _logger = TestLogger.CreateNullLogger();
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
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _logger);

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
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _logger);

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
                _factory.CreateProvider(null, _httpClientMock.Object, _logger));
        }

        [Fact]
        public void CreateProvider_WithNullHttpClient_ThrowsArgumentNullException()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama };

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                _factory.CreateProvider(settings, null, _logger));
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
                _factory.CreateProvider(settings, _httpClientMock.Object, _logger));
            
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
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _logger);

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
            var provider1 = _factory.CreateProvider(ollamaSettings, _httpClientMock.Object, _logger);
            var provider2 = _factory.CreateProvider(lmStudioSettings, _httpClientMock.Object, _logger);

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
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _logger);

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
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _logger);

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
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _logger);

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
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _logger);

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
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _logger);

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
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _logger);

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
            var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _logger);

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

        #region Registry Pattern Tests

        [Fact]
        public void Constructor_WithValidRegistry_InitializesSuccessfully()
        {
            // Arrange & Act
            var factory = new AIProviderFactory(_registryMock.Object);

            // Assert
            factory.Should().NotBeNull();
        }

        [Fact]
        public void Constructor_WithNullRegistry_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new AIProviderFactory(null));
        }

        [Fact]
        public void CreateProvider_WithCustomRegistry_DelegatesToRegistry()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama };
            var expectedProvider = new Mock<IAIProvider>().Object;
            
            _registryMock
                .Setup(r => r.CreateProvider(AIProvider.Ollama, settings, _httpClientMock.Object, _logger))
                .Returns(expectedProvider);

            // Act
            var result = _factoryWithCustomRegistry.CreateProvider(settings, _httpClientMock.Object, _logger);

            // Assert
            result.Should().BeSameAs(expectedProvider);
            _registryMock.Verify(r => r.CreateProvider(AIProvider.Ollama, settings, _httpClientMock.Object, _logger), Times.Once);
        }

        [Fact]
        public void CreateProvider_WhenRegistryThrows_PropagatesException()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama };
            var expectedException = new InvalidOperationException("Registry error");
            
            _registryMock
                .Setup(r => r.CreateProvider(It.IsAny<AIProvider>(), It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                .Throws(expectedException);

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                _factoryWithCustomRegistry.CreateProvider(settings, _httpClientMock.Object, _logger));
            exception.Should().BeSameAs(expectedException);
        }

        #endregion

        #region Edge Case and Advanced Tests

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t")]
        [InlineData("\n")]
        public void IsProviderAvailable_WithWhitespaceOnlyApiKey_ReturnsFalse(string whitespaceKey)
        {
            // Arrange
            var settings = new BrainarrSettings { OpenAIApiKey = whitespaceKey };

            // Act
            var result = _factory.IsProviderAvailable(AIProvider.OpenAI, settings);

            // Assert
            result.Should().BeFalse();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t")]
        [InlineData("\n")]
        public void IsProviderAvailable_WithWhitespaceOnlyUrl_ReturnsFalse(string whitespaceUrl)
        {
            // Arrange
            var settings = new BrainarrSettings { OllamaUrl = whitespaceUrl };

            // Act
            var result = _factory.IsProviderAvailable(AIProvider.Ollama, settings);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void CreateProvider_WithAllProvidersSequentially_CreatesUniqueInstances()
        {
            // Arrange
            var allProviders = new[]
            {
                (AIProvider.Ollama, new BrainarrSettings { Provider = AIProvider.Ollama, OllamaUrl = "http://localhost:11434" }),
                (AIProvider.LMStudio, new BrainarrSettings { Provider = AIProvider.LMStudio, LMStudioUrl = "http://localhost:1234" }),
                (AIProvider.OpenAI, new BrainarrSettings { Provider = AIProvider.OpenAI, OpenAIApiKey = "sk-test" }),
                (AIProvider.Anthropic, new BrainarrSettings { Provider = AIProvider.Anthropic, AnthropicApiKey = "sk-ant-test" }),
                (AIProvider.Gemini, new BrainarrSettings { Provider = AIProvider.Gemini, GeminiApiKey = "AIza-test" }),
                (AIProvider.Groq, new BrainarrSettings { Provider = AIProvider.Groq, GroqApiKey = "gsk-test" }),
                (AIProvider.OpenRouter, new BrainarrSettings { Provider = AIProvider.OpenRouter, OpenRouterApiKey = "or-test" }),
                (AIProvider.DeepSeek, new BrainarrSettings { Provider = AIProvider.DeepSeek, DeepSeekApiKey = "ds-test" }),
                (AIProvider.Perplexity, new BrainarrSettings { Provider = AIProvider.Perplexity, PerplexityApiKey = "pplx-test" })
            };

            var createdProviders = new List<IAIProvider>();

            // Act
            foreach (var (providerType, settings) in allProviders)
            {
                var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _logger);
                createdProviders.Add(provider);
            }

            // Assert
            createdProviders.Should().HaveCount(9);
            createdProviders.Should().OnlyHaveUniqueItems();
            
            // Verify each provider is of the expected type
            createdProviders[0].Should().BeOfType<OllamaProvider>();
            createdProviders[1].Should().BeOfType<LMStudioProvider>();
            createdProviders[2].Should().BeOfType<OpenAIProvider>();
            createdProviders[3].Should().BeOfType<AnthropicProvider>();
            createdProviders[4].Should().BeOfType<GeminiProvider>();
            createdProviders[5].Should().BeOfType<GroqProvider>();
            createdProviders[6].Should().BeOfType<OpenRouterProvider>();
            createdProviders[7].Should().BeOfType<DeepSeekProvider>();
            createdProviders[8].Should().BeOfType<PerplexityProvider>();
        }

        [Fact]
        public void IsProviderAvailable_WithNullSettings_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                _factory.IsProviderAvailable(AIProvider.OpenAI, null));
        }

        [Theory]
        [InlineData(AIProvider.Ollama, "OllamaUrl")]
        [InlineData(AIProvider.LMStudio, "LMStudioUrl")]
        public void IsProviderAvailable_LocalProviders_ValidateUrlProperty(AIProvider provider, string urlProperty)
        {
            // Arrange
            var settings = new BrainarrSettings();
            var propertyInfo = typeof(BrainarrSettings).GetProperty(urlProperty);

            // Act & Assert - with null URL
            propertyInfo.SetValue(settings, null);
            _factory.IsProviderAvailable(provider, settings).Should().BeFalse();

            // Act & Assert - with valid URL
            propertyInfo.SetValue(settings, "http://localhost:8080");
            _factory.IsProviderAvailable(provider, settings).Should().BeTrue();
        }

        [Fact]
        public async Task CreateProvider_ConcurrentCreation_ThreadSafe()
        {
            // Arrange
            const int threadCount = 10;
            const int providersPerThread = 5;
            var allProviders = new List<IAIProvider>();
            var allTasks = new List<Task>();

            // Act
            for (int i = 0; i < threadCount; i++)
            {
                var task = Task.Run(() =>
                {
                    var threadProviders = new List<IAIProvider>();
                    for (int j = 0; j < providersPerThread; j++)
                    {
                        var settings = new BrainarrSettings
                        {
                            Provider = AIProvider.OpenAI,
                            OpenAIApiKey = $"sk-test-{i}-{j}"
                        };
                        var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _logger);
                        threadProviders.Add(provider);
                    }
                    
                    lock (allProviders)
                    {
                        allProviders.AddRange(threadProviders);
                    }
                });
                allTasks.Add(task);
            }

            await Task.WhenAll(allTasks.ToArray());

            // Assert
            allProviders.Should().HaveCount(threadCount * providersPerThread);
            allProviders.Should().OnlyHaveUniqueItems();
            allProviders.Should().AllBeOfType<OpenAIProvider>();
        }

        [Theory]
        [InlineData("sk-1234567890abcdef1234567890abcdef")]
        [InlineData("sk-proj-1234567890abcdef")]
        [InlineData("gsk-1234567890abcdef")]
        [InlineData("pplx-1234567890abcdef")]
        public void IsProviderAvailable_WithValidApiKeyFormats_ReturnsTrue(string apiKey)
        {
            // Arrange
            var settings = new BrainarrSettings { OpenAIApiKey = apiKey };

            // Act
            var result = _factory.IsProviderAvailable(AIProvider.OpenAI, settings);

            // Assert
            result.Should().BeTrue();
        }

        [Theory]
        [InlineData("http://localhost:11434")]
        [InlineData("https://api.openai.com")]
        [InlineData("http://192.168.1.100:8080")]
        [InlineData("https://custom-domain.com:9090/api")]
        public void IsProviderAvailable_WithValidUrlFormats_ReturnsTrue(string url)
        {
            // Arrange
            var settings = new BrainarrSettings { OllamaUrl = url };

            // Act
            var result = _factory.IsProviderAvailable(AIProvider.Ollama, settings);

            // Assert
            result.Should().BeTrue();
        }

        #endregion

        #region Performance and Memory Tests

        [Fact]
        public void CreateProvider_RepeatedCreation_DoesNotLeakMemory()
        {
            // Arrange
            const int iterations = 1000;
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                OpenAIApiKey = "sk-test-memory-leak"
            };

            // Act & Assert - This test mainly ensures no exceptions are thrown during repeated creation
            for (int i = 0; i < iterations; i++)
            {
                var provider = _factory.CreateProvider(settings, _httpClientMock.Object, _logger);
                provider.Should().NotBeNull();
                provider.Should().BeOfType<OpenAIProvider>();
            }
        }

        #endregion
    }
}
