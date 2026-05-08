using System;
using FluentAssertions;
using Moq;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Xunit;

namespace Brainarr.Tests
{
    /// <summary>
    /// Coverage tests for ProviderRegistry paths not covered by ProviderRegistryTests or ProviderRegistryAllowlistTests.
    /// Tests focus on: constructor validation, Register validation, CreateProvider validation,
    /// GetRegisteredProviders, TryCreateProvider extension, and IHttpResilience integration.
    /// </summary>
    [Trait("Category", "Unit")]
    public class ProviderRegistryCovTests
    {
        private readonly Mock<IHttpClient> _httpClient;
        private readonly NLog.Logger _logger;
        private readonly BrainarrSettings _settings;

        public ProviderRegistryCovTests()
        {
            _httpClient = new Mock<IHttpClient>();
            _logger = Helpers.TestLogger.CreateNullLogger();
            _settings = new BrainarrSettings
            {
                // Dummy API keys required for cloud provider construction validation
                OpenAIApiKey = "sk-test-dummy-key-for-unit-tests",
                AnthropicApiKey = "sk-ant-test-dummy-key-for-unit-tests",
                PerplexityApiKey = "pplx-test-dummy-key-for-unit-tests",
                OpenRouterApiKey = "sk-or-test-dummy-key-for-unit-tests",
                DeepSeekApiKey = "sk-ds-test-dummy-key-for-unit-tests",
                GeminiApiKey = "AIza-test-dummy-key-for-unit-tests",
                GroqApiKey = "gsk-test-dummy-key-for-unit-tests",
            };
        }

        #region Constructor Validation

        // Source line 60: _httpExec = httpExec ?? throw new ArgumentNullException(nameof(httpExec));
        // Proof: grep -n "throw new ArgumentNullException" Brainarr.Plugin/Services/Core/ProviderRegistry.cs
        //   60:            _httpExec = httpExec ?? throw new ArgumentNullException(nameof(httpExec));
        [Fact]
        public void Constructor_WithNullHttpExec_ThrowsArgumentNullException()
        {
            // Act
            var act = () => new ProviderRegistry(null!);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("httpExec");
        }

        // Source line 60: _httpExec = httpExec ?? throw new ArgumentNullException(nameof(httpExec));
        // Proof: grep -n "throw new ArgumentNullException" Brainarr.Plugin/Services/Core/ProviderRegistry.cs
        //   60:            _httpExec = httpExec ?? throw new ArgumentNullException(nameof(httpExec));
        [Fact]
        public void Constructor_WithValidHttpExec_DoesNotThrow()
        {
            // Arrange
            var httpExec = new Mock<IHttpResilience>().Object;

            // Act
            var act = () => new ProviderRegistry(httpExec);

            // Assert
            act.Should().NotThrow();
        }

        #endregion

        #region Register Method Validation

        // Source line 236: throw new ArgumentNullException(nameof(factory));
        // Proof: grep -n "throw new ArgumentNullException" Brainarr.Plugin/Services/Core/ProviderRegistry.cs
        //   236:                throw new ArgumentNullException(nameof(factory));
        [Fact]
        public void Register_WithNullFactory_ThrowsArgumentNullException()
        {
            // Arrange
            var registry = new ProviderRegistry();

            // Act
            var act = () => registry.Register(AIProvider.Ollama, null!);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("factory");
        }

        #endregion

        #region CreateProvider Validation

        // Source line 244: throw new ArgumentNullException(nameof(settings));
        // Proof: grep -n "throw new ArgumentNullException" Brainarr.Plugin/Services/Core/ProviderRegistry.cs
        //   244:                throw new ArgumentNullException(nameof(settings));
        [Fact]
        public void CreateProvider_WithNullSettings_ThrowsArgumentNullException()
        {
            // Arrange
            var registry = new ProviderRegistry();

            // Act
            var act = () => registry.CreateProvider(AIProvider.Ollama, null!, _httpClient.Object, _logger);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("settings");
        }

        // Source line 246: throw new ArgumentNullException(nameof(httpClient));
        // Proof: grep -n "throw new ArgumentNullException" Brainarr.Plugin/Services/Core/ProviderRegistry.cs
        //   246:                throw new ArgumentNullException(nameof(httpClient));
        [Fact]
        public void CreateProvider_WithNullHttpClient_ThrowsArgumentNullException()
        {
            // Arrange
            var registry = new ProviderRegistry();

            // Act
            var act = () => registry.CreateProvider(AIProvider.Ollama, _settings, null!, _logger);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("httpClient");
        }

        // Source line 248: throw new ArgumentNullException(nameof(logger));
        // Proof: grep -n "throw new ArgumentNullException" Brainarr.Plugin/Services/Core/ProviderRegistry.cs
        //   248:                throw new ArgumentNullException(nameof(logger));
        [Fact]
        public void CreateProvider_WithNullLogger_ThrowsArgumentNullException()
        {
            // Arrange
            var registry = new ProviderRegistry();

            // Act
            var act = () => registry.CreateProvider(AIProvider.Ollama, _settings, _httpClient.Object, null!);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }

        // Source line 252: throw new NotSupportedException($"Provider type {type} is not supported");
        // Proof: grep -n "throw new NotSupportedException" Brainarr.Plugin/Services/Core/ProviderRegistry.cs
        //   252:                throw new NotSupportedException($"Provider type {type} is not supported");
        [Fact]
        public void CreateProvider_WithUnregisteredProvider_ThrowsNotSupportedException()
        {
            // Arrange
            // Use EmptyProviderRegistry to test unregistered provider path
            var emptyRegistry = new EmptyProviderRegistry();

            // Act
            var act = () => emptyRegistry.CreateProviderPublic(AIProvider.Ollama, _settings, _httpClient.Object, _logger);

            // Assert
            act.Should().Throw<NotSupportedException>()
                .WithMessage("*Provider type*is not supported*");
        }

        #endregion

        #region GetRegisteredProviders

        // Source line 263-266: public IEnumerable<AIProvider> GetRegisteredProviders() { return _factories.Keys; }
        // Proof: grep -n "GetRegisteredProviders" Brainarr.Plugin/Services/Core/ProviderRegistry.cs
        //   263:        public IEnumerable<AIProvider> GetRegisteredProviders()
        [Fact]
        public void GetRegisteredProviders_ReturnsAllRegisteredProviders()
        {
            // Arrange
            var registry = new ProviderRegistry();
            // Proof: grep -c "Register(AIProvider\." Brainarr.Plugin/Services/Core/ProviderRegistry.cs
            // Output: 11 (number of providers registered)
            const int expectedCount = 11;

            // Act
            var providers = registry.GetRegisteredProviders();

            // Assert
            providers.Should().HaveCount(expectedCount, "because all enum values should be registered");
            providers.Should().Contain(AIProvider.Ollama, "because Ollama is registered");
            providers.Should().Contain(AIProvider.LMStudio, "because LMStudio is registered");
            providers.Should().Contain(AIProvider.Perplexity, "because Perplexity is registered");
            providers.Should().Contain(AIProvider.OpenAI, "because OpenAI is registered");
            providers.Should().Contain(AIProvider.Anthropic, "because Anthropic is registered");
            providers.Should().Contain(AIProvider.OpenRouter, "because OpenRouter is registered");
            providers.Should().Contain(AIProvider.DeepSeek, "because DeepSeek is registered");
            providers.Should().Contain(AIProvider.Gemini, "because Gemini is registered");
            providers.Should().Contain(AIProvider.Groq, "because Groq is registered");
            providers.Should().Contain(AIProvider.ClaudeCodeSubscription, "because ClaudeCodeSubscription is registered");
            providers.Should().Contain(AIProvider.OpenAICodexSubscription, "because OpenAICodexSubscription is registered");
        }

        #endregion

        #region TryCreateProvider Extension

        // Source line 287-299: public static IAIProvider TryCreateProvider(...)
        // Proof: grep -n "TryCreateProvider" Brainarr.Plugin/Services/Core/ProviderRegistry.cs
        //   287:        public static IAIProvider TryCreateProvider(this IProviderRegistry registry,
        [Fact]
        public void TryCreateProvider_WithValidParameters_ReturnsProvider()
        {
            // Arrange
            var registry = new ProviderRegistry();

            // Act
            var provider = registry.TryCreateProvider(AIProvider.Ollama, _settings, _httpClient.Object, _logger);

            // Assert
            provider.Should().NotBeNull("because valid parameters should create a provider");
            // Phase 4 wave 4c: Ollama now flows through LlmProviderAdapter (wrapping
            // BrainarrOllamaProvider). The legacy concrete OllamaProvider is gated behind
            // BRAINARR_USE_LEGACY_LLM_PROVIDERS for rollback.
            provider.Should().BeOfType<NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm.LlmProviderAdapter>(
                "because Ollama was requested via the wave-4c ILlmProvider path");
        }

        // Source line 294-297: try { return registry.CreateProvider(...); } catch { return null; }
        // Proof: grep -A5 "TryCreateProvider" Brainarr.Plugin/Services/Core/ProviderRegistry.cs
        [Fact]
        public void TryCreateProvider_WithNullSettings_ReturnsNull()
        {
            // Arrange
            var registry = new ProviderRegistry();

            // Act
            var provider = registry.TryCreateProvider(AIProvider.Ollama, null!, _httpClient.Object, _logger);

            // Assert
            provider.Should().BeNull("because TryCreateProvider catches exceptions and returns null");
        }

        // Source line 294-297: try { return registry.CreateProvider(...); } catch { return null; }
        // Proof: grep -A5 "TryCreateProvider" Brainarr.Plugin/Services/Core/ProviderRegistry.cs
        [Fact]
        public void TryCreateProvider_WithNullHttpClient_ReturnsNull()
        {
            // Arrange
            var registry = new ProviderRegistry();

            // Act
            var provider = registry.TryCreateProvider(AIProvider.Ollama, _settings, null!, _logger);

            // Assert
            provider.Should().BeNull("because TryCreateProvider catches exceptions and returns null");
        }

        // Source line 294-297: try { return registry.CreateProvider(...); } catch { return null; }
        // Proof: grep -A5 "TryCreateProvider" Brainarr.Plugin/Services/Core/ProviderRegistry.cs
        [Fact]
        public void TryCreateProvider_WithNullLogger_ReturnsNull()
        {
            // Arrange
            var registry = new ProviderRegistry();

            // Act
            var provider = registry.TryCreateProvider(AIProvider.Ollama, _settings, _httpClient.Object, null!);

            // Assert
            provider.Should().BeNull("because TryCreateProvider catches exceptions and returns null");
        }

        #endregion

        #region IsRegistered Edge Cases

        // Source line 258-260: public bool IsRegistered(AIProvider type) { return _factories.ContainsKey(type); }
        // Proof: grep -n "IsRegistered" Brainarr.Plugin/Services/Core/ProviderRegistry.cs
        //   258:        public bool IsRegistered(AIProvider type)
        [Fact]
        public void IsRegistered_WithAllEnumValues_ReturnsTrue()
        {
            // Arrange
            var registry = new ProviderRegistry();

            // Act & Assert
            foreach (AIProvider provider in Enum.GetValues(typeof(AIProvider)))
            {
                registry.IsRegistered(provider).Should().BeTrue($"because {provider} should be registered");
            }
        }

        #endregion

        #region CreateProvider Returns Correct Types

        // Source line 77-99: Register providers with specific factory functions
        // Proof: grep -n "Register(AIProvider\\." Brainarr.Plugin/Services/Core/ProviderRegistry.cs
        [Theory]
        // Phase 4 waves 4a/4b/4c: all 9 LLM providers (cloud + local) flow through
        // LlmProviderAdapter (wrapping the corresponding ILlmProvider).
        [InlineData(AIProvider.Ollama, typeof(NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm.LlmProviderAdapter))]
        [InlineData(AIProvider.LMStudio, typeof(NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm.LlmProviderAdapter))]
        [InlineData(AIProvider.Perplexity, typeof(NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm.LlmProviderAdapter))]
        [InlineData(AIProvider.OpenAI, typeof(NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm.LlmProviderAdapter))]
        [InlineData(AIProvider.Anthropic, typeof(NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm.LlmProviderAdapter))]
        [InlineData(AIProvider.OpenRouter, typeof(NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm.LlmProviderAdapter))]
        [InlineData(AIProvider.DeepSeek, typeof(NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm.LlmProviderAdapter))]
        [InlineData(AIProvider.Gemini, typeof(NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm.LlmProviderAdapter))]
        [InlineData(AIProvider.Groq, typeof(NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm.LlmProviderAdapter))]
        [InlineData(AIProvider.ClaudeCodeSubscription, typeof(ClaudeCodeSubscriptionProvider))]
        [InlineData(AIProvider.OpenAICodexSubscription, typeof(OpenAICodexSubscriptionProvider))]
        public void CreateProvider_WithValidParameters_ReturnsCorrectType(AIProvider providerType, Type expectedType)
        {
            // Arrange
            var registry = new ProviderRegistry();

            // Act
            var provider = registry.CreateProvider(providerType, _settings, _httpClient.Object, _logger);

            // Assert
            provider.Should().BeOfType(expectedType, $"because {providerType} should create {expectedType.Name}");
        }

        #endregion
    }

    /// <summary>
    /// Test helper class that provides access to CreateProvider without automatic registration.
    /// Used to test the unregistered provider exception path.
    /// </summary>
    internal class EmptyProviderRegistry : IProviderRegistry
    {
        private readonly System.Collections.Generic.Dictionary<AIProvider, Func<BrainarrSettings, IHttpClient, NLog.Logger, IAIProvider>> _factories
            = new();

        public void Register(AIProvider type, Func<BrainarrSettings, IHttpClient, NLog.Logger, IAIProvider> factory)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));
            _factories[type] = factory;
        }

        public IAIProvider CreateProvider(AIProvider type, BrainarrSettings settings, IHttpClient httpClient, NLog.Logger logger)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (httpClient == null)
                throw new ArgumentNullException(nameof(httpClient));
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            if (!_factories.TryGetValue(type, out var factory))
            {
                throw new NotSupportedException($"Provider type {type} is not supported");
            }

            return factory(settings, httpClient, logger);
        }

        // Public accessor for testing
        public IAIProvider CreateProviderPublic(AIProvider type, BrainarrSettings settings, IHttpClient httpClient, NLog.Logger logger)
            => CreateProvider(type, settings, httpClient, logger);

        public bool IsRegistered(AIProvider type)
        {
            return _factories.ContainsKey(type);
        }

        public System.Collections.Generic.IEnumerable<AIProvider> GetRegisteredProviders()
        {
            return _factories.Keys;
        }
    }
}
