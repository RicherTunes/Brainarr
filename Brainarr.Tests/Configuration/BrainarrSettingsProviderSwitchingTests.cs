using System;
using System.Collections.Generic;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    /// <summary>
    /// Comprehensive tests for the intricate provider switching logic in BrainarrSettings.
    /// Ensures settings are properly preserved, mapped, and validated as users switch between providers.
    /// </summary>
    public class BrainarrSettingsProviderSwitchingTests
    {
        #region State Preservation Tests

        [Fact]
        public void ProviderSwitch_StatePreservation_OllamaToOpenAIToOllama_PreservesOriginalSettings()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://custom-ollama:11434",
                OllamaModel = "custom-llama-model"
            };

            // Act - Switch to OpenAI
            settings.Provider = AIProvider.OpenAI;
            settings.OpenAIApiKey = "sk-test-key";
            settings.OpenAIModelId = "GPT41";

            // Switch back to Ollama
            settings.Provider = AIProvider.Ollama;

            // Assert - Original Ollama settings preserved
            settings.OllamaUrl.Should().Be("http://custom-ollama:11434");
            settings.OllamaModel.Should().Be("custom-llama-model");

            // OpenAI settings should remain intact
            settings.OpenAIApiKey.Should().Be("sk-test-key");
            settings.OpenAIModelId.Should().Be("GPT41");
        }

        [Fact]
        public void ProviderSwitch_StatePreservation_ComplexSwitchingChain_PreservesAllSettings()
        {
            // Arrange - Start with Ollama
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://ollama-server:11434",
                OllamaModel = "qwen2.5:latest"
            };

            // Act - Chain of provider switches
            settings.Provider = AIProvider.Anthropic;
            settings.AnthropicApiKey = "sk-ant-test123";
            settings.AnthropicModelId = "ClaudeSonnet4";

            settings.Provider = AIProvider.Gemini;
            settings.GeminiApiKey = "AIza-gemini-test";
            settings.GeminiModelId = "Gemini_25_Pro";

            settings.Provider = AIProvider.LMStudio;
            settings.LMStudioUrl = "http://lmstudio:1234";
            settings.LMStudioModel = "local-model-v2";

            // Return to each provider
            settings.Provider = AIProvider.Ollama;
            settings.OllamaUrl.Should().Be("http://ollama-server:11434");
            settings.OllamaModel.Should().Be("qwen2.5:latest");

            settings.Provider = AIProvider.Anthropic;
            settings.AnthropicApiKey.Should().Be("sk-ant-test123");
            settings.AnthropicModelId.Should().Be("ClaudeSonnet4");

            settings.Provider = AIProvider.Gemini;
            settings.GeminiApiKey.Should().Be("AIza-gemini-test");
            settings.GeminiModelId.Should().Be("Gemini_25_Pro");

            settings.Provider = AIProvider.LMStudio;
            settings.LMStudioUrl.Should().Be("http://lmstudio:1234");
            settings.LMStudioModel.Should().Be("local-model-v2");
        }

        [Theory]
        [InlineData(AIProvider.Ollama, AIProvider.OpenAI)]
        [InlineData(AIProvider.LMStudio, AIProvider.Anthropic)]
        [InlineData(AIProvider.OpenRouter, AIProvider.Gemini)]
        [InlineData(AIProvider.DeepSeek, AIProvider.Groq)]
        public void ProviderSwitch_StatePreservation_IndependentProviderStates(AIProvider from, AIProvider to)
        {
            // Arrange - Configure first provider
            var settings = new BrainarrSettings { Provider = from };
            ConfigureProvider(settings, from, "test-config-1");

            // Act - Switch to second provider
            settings.Provider = to;
            ConfigureProvider(settings, to, "test-config-2");

            // Switch back to first
            settings.Provider = from;

            // Assert - First provider config preserved
            var firstConfig = GetProviderConfig(settings, from);
            firstConfig.Should().Contain("test-config-1");

            // Second provider config should also be preserved
            var secondConfig = GetProviderConfig(settings, to);
            secondConfig.Should().Contain("test-config-2");
        }

        #endregion

        #region Model Selection Mapping Tests

        [Theory]
        [InlineData(AIProvider.Ollama, "custom-ollama-model")]
        [InlineData(AIProvider.LMStudio, "custom-lmstudio-model")]
        [InlineData(AIProvider.Perplexity, "Sonar_Huge")]
        [InlineData(AIProvider.OpenAI, "GPT4_Turbo")]
        [InlineData(AIProvider.Anthropic, "Claude3_Opus")]
        [InlineData(AIProvider.OpenRouter, "GPT41")]
        [InlineData(AIProvider.DeepSeek, "DeepSeek_Coder")]
        [InlineData(AIProvider.Gemini, "Gemini_20_Flash")]
        [InlineData(AIProvider.Groq, "Llama32_90B_Vision")]
        public void ModelSelection_PropertyMapping_UpdatesCorrectProviderModel(AIProvider provider, string modelName)
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = provider };

            // Act
            settings.ModelSelection = modelName;

            var expectedModel = ProviderModelNormalizer.Normalize(provider, modelName);

            // Assert
            settings.ModelSelection.Should().Be(expectedModel);

            // Verify the specific provider model property was updated
            var providerModel = provider switch
            {
                AIProvider.Ollama => settings.OllamaModel,
                AIProvider.LMStudio => settings.LMStudioModel,
                AIProvider.Perplexity => settings.PerplexityModelId,
                AIProvider.OpenAI => settings.OpenAIModelId,
                AIProvider.Anthropic => settings.AnthropicModelId,
                AIProvider.OpenRouter => settings.OpenRouterModelId,
                AIProvider.DeepSeek => settings.DeepSeekModelId,
                AIProvider.Gemini => settings.GeminiModelId,
                AIProvider.Groq => settings.GroqModelId,
                _ => null
            };

            providerModel.Should().Be(expectedModel);
        }

        [Fact]
        public void ModelSelection_ProviderSwitch_DoesNotAffectOtherProviderModels()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Configure multiple providers with different models
            settings.Provider = AIProvider.OpenAI;
            settings.ModelSelection = "GPT41";

            settings.Provider = AIProvider.Anthropic;
            settings.ModelSelection = "ClaudeSonnet4";

            settings.Provider = AIProvider.Gemini;
            settings.ModelSelection = BrainarrConstants.DefaultGeminiModel;

            // Act & Assert - Each switch preserves other models
            settings.Provider = AIProvider.OpenAI;
            settings.ModelSelection.Should().Be("GPT41");
            settings.AnthropicModel.Should().Be("ClaudeSonnet4"); // Preserved
            settings.GeminiModel.Should().Be(BrainarrConstants.DefaultGeminiModel); // Preserved

            settings.Provider = AIProvider.Anthropic;
            settings.ModelSelection.Should().Be("ClaudeSonnet4");
            settings.OpenAIModelId.Should().Be("GPT41"); // Preserved
            settings.GeminiModelId.Should().Be(BrainarrConstants.DefaultGeminiModel); // Preserved
        }

        #endregion

        #region API Key Mapping Tests

        [Theory]
        [InlineData(AIProvider.Perplexity, "pplx-test-key-123")]
        [InlineData(AIProvider.OpenAI, "sk-test-openai-key")]
        [InlineData(AIProvider.Anthropic, "sk-ant-test-key")]
        [InlineData(AIProvider.OpenRouter, "or-test-key-456")]
        [InlineData(AIProvider.DeepSeek, "ds-test-key-789")]
        [InlineData(AIProvider.Gemini, "AIza-gemini-test-key")]
        [InlineData(AIProvider.Groq, "gsk-groq-test-key")]
        public void ApiKey_PropertyMapping_UpdatesCorrectProviderApiKey(AIProvider provider, string apiKey)
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = provider };

            // Act
            settings.ApiKey = apiKey;

            // Assert
            settings.ApiKey.Should().Be(apiKey);

            // Verify the specific provider API key was updated
            var providerApiKey = provider switch
            {
                AIProvider.Perplexity => settings.PerplexityApiKey,
                AIProvider.OpenAI => settings.OpenAIApiKey,
                AIProvider.Anthropic => settings.AnthropicApiKey,
                AIProvider.OpenRouter => settings.OpenRouterApiKey,
                AIProvider.DeepSeek => settings.DeepSeekApiKey,
                AIProvider.Gemini => settings.GeminiApiKey,
                AIProvider.Groq => settings.GroqApiKey,
                _ => null
            };

            providerApiKey.Should().Be(apiKey);
        }

        [Theory]
        [InlineData(AIProvider.Ollama)]
        [InlineData(AIProvider.LMStudio)]
        public void ApiKey_LocalProviders_ReturnsNull(AIProvider localProvider)
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = localProvider };

            // Act & Assert
            settings.ApiKey.Should().BeNull();

            // Setting API key should not affect local providers
            settings.ApiKey = "some-key";
            settings.ApiKey.Should().BeNull();
        }

        [Fact]
        public void ApiKey_ProviderSwitch_PreservesAllApiKeys()
        {
            // Arrange
            var settings = new BrainarrSettings();
            var testKeys = new Dictionary<AIProvider, string>
            {
                { AIProvider.OpenAI, "sk-openai-test" },
                { AIProvider.Anthropic, "sk-ant-test" },
                { AIProvider.Gemini, "AIza-gemini-test" },
                { AIProvider.Groq, "gsk-groq-test" }
            };

            // Set up all API keys
            foreach (var kvp in testKeys)
            {
                settings.Provider = kvp.Key;
                settings.ApiKey = kvp.Value;
            }

            // Act & Assert - Verify each key is preserved
            foreach (var kvp in testKeys)
            {
                settings.Provider = kvp.Key;
                settings.ApiKey.Should().Be(kvp.Value, $"API key for {kvp.Key} should be preserved");
            }
        }

        #endregion

        #region Validation Scoping Tests

        [Fact]
        public void Validation_ProviderScoping_OnlyValidatesSelectedProvider()
        {
            // Arrange - Set up invalid configurations for all providers
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                OllamaUrl = "invalid-url", // Invalid but shouldn't matter
                LMStudioUrl = "another-invalid-url", // Invalid but shouldn't matter
                OpenAIApiKey = "valid-openai-key", // Valid for selected provider
                MaxRecommendations = 10
            };

            // Act
            var result = settings.Validate();

            // Assert - Should be valid because only OpenAI provider is validated
            result.IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData(AIProvider.Perplexity)]
        [InlineData(AIProvider.OpenAI)]
        [InlineData(AIProvider.Anthropic)]
        [InlineData(AIProvider.OpenRouter)]
        [InlineData(AIProvider.DeepSeek)]
        [InlineData(AIProvider.Gemini)]
        [InlineData(AIProvider.Groq)]
        public void Validation_CloudProviders_RequiresApiKeyOnlyForSelectedProvider(AIProvider cloudProvider)
        {
            // Arrange - No API keys set
            var settings = new BrainarrSettings
            {
                Provider = cloudProvider,
                MaxRecommendations = 10
            };

            // Act
            var result = settings.Validate();

            // Assert - Should be invalid due to missing API key
            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCountGreaterThan(0);
            result.Errors.Should().Contain(e => e.ErrorMessage.Contains("required"));
        }

        [Fact]
        public void Validation_ProviderSwitch_ValidationRulesChange()
        {
            // Arrange - Valid Ollama configuration
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434",
                MaxRecommendations = 10
            };

            // Verify valid state
            settings.Validate().IsValid.Should().BeTrue();

            // Act - Switch to cloud provider without API key
            settings.Provider = AIProvider.OpenAI;

            // Assert - Now invalid due to missing API key
            var result = settings.Validate();
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "OpenAIApiKey");
        }

        #endregion

        #region Provider Change Detection and Model Reset Tests

        [Fact]
        public void ProviderChange_Detection_SetsProviderChangedFlag()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama };

            // Act - Change provider
            settings.Provider = AIProvider.OpenAI;

            // Assert - Provider change should be detected (through ModelSelection behavior)
            // When provider changes, ModelSelection should return default for new provider
            settings.ModelSelection.Should().Be(BrainarrConstants.DefaultOpenAIModel); // Default for OpenAI
        }

        [Theory]
        [InlineData(AIProvider.Ollama, "qwen2.5:latest")]
        [InlineData(AIProvider.LMStudio, "local-model")]
        [InlineData(AIProvider.Perplexity, BrainarrConstants.DefaultPerplexityModel)]
        [InlineData(AIProvider.OpenAI, BrainarrConstants.DefaultOpenAIModel)]
        [InlineData(AIProvider.Anthropic, BrainarrConstants.DefaultAnthropicModel)]
        [InlineData(AIProvider.OpenRouter, BrainarrConstants.DefaultOpenRouterModel)]
        [InlineData(AIProvider.DeepSeek, BrainarrConstants.DefaultDeepSeekModel)]
        [InlineData(AIProvider.Gemini, BrainarrConstants.DefaultGeminiModel)]
        [InlineData(AIProvider.Groq, BrainarrConstants.DefaultGroqModel)]
        public void ProviderChange_ModelReset_ReturnsDefaultForNewProvider(AIProvider provider, string expectedDefault)
        {
            // Arrange - Start with different provider
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama };
            settings.ModelSelection = "custom-model"; // Set custom model

            // Act - Switch provider
            settings.Provider = provider;

            // Assert - Should return default for new provider
            settings.ModelSelection.Should().Be(expectedDefault);
        }

        [Fact]
        public void ProviderChange_ModelReset_DoesNotAffectBackingFields()
        {
            // Arrange - Configure Ollama with custom model
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaModel = "custom-ollama-model"
            };

            // Act - Switch to OpenAI and back
            settings.Provider = AIProvider.OpenAI;
            settings.Provider = AIProvider.Ollama;

            // Assert - Ollama model should still be preserved
            settings.OllamaModel.Should().Be("custom-ollama-model");
        }

        #endregion

        #region Default Model Selection Tests

        [Fact]
        public void DefaultModelSelection_NewSettings_ReturnsOllamaDefault()
        {
            // Arrange & Act
            var settings = new BrainarrSettings();

            // Assert
            settings.ModelSelection.Should().Be(BrainarrConstants.DefaultOllamaModel);
        }

        [Theory]
        [InlineData(AIProvider.Ollama, BrainarrConstants.DefaultOllamaModel)]
        [InlineData(AIProvider.LMStudio, BrainarrConstants.DefaultLMStudioModel)]
        public void DefaultModelSelection_LocalProviders_UsesConstants(AIProvider provider, string expectedDefault)
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = provider };

            // Act & Assert
            settings.ModelSelection.Should().Be(expectedDefault);
        }

        [Theory]
        [InlineData(AIProvider.Perplexity, BrainarrConstants.DefaultPerplexityModel)]
        [InlineData(AIProvider.OpenAI, BrainarrConstants.DefaultOpenAIModel)]
        [InlineData(AIProvider.Anthropic, BrainarrConstants.DefaultAnthropicModel)]
        [InlineData(AIProvider.OpenRouter, BrainarrConstants.DefaultOpenRouterModel)]
        [InlineData(AIProvider.DeepSeek, BrainarrConstants.DefaultDeepSeekModel)]
        [InlineData(AIProvider.Gemini, BrainarrConstants.DefaultGeminiModel)]
        [InlineData(AIProvider.Groq, BrainarrConstants.DefaultGroqModel)]
        public void DefaultModelSelection_CloudProviders_UsesReasonableDefaults(AIProvider provider, string expectedDefault)
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = provider };

            // Act & Assert
            settings.ModelSelection.Should().Be(expectedDefault);
        }

        #endregion

        #region Configuration URL Behavior Tests

        [Theory]
        [InlineData(AIProvider.Ollama, BrainarrConstants.DefaultOllamaUrl)]
        [InlineData(AIProvider.LMStudio, BrainarrConstants.DefaultLMStudioUrl)]
        public void ConfigurationUrl_LocalProviders_ReturnsCorrectUrl(AIProvider provider, string expectedUrl)
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = provider };

            // Act & Assert
            settings.ConfigurationUrl.Should().Be(expectedUrl);
        }

        [Theory]
        [InlineData(AIProvider.Perplexity)]
        [InlineData(AIProvider.OpenAI)]
        [InlineData(AIProvider.Anthropic)]
        [InlineData(AIProvider.OpenRouter)]
        [InlineData(AIProvider.DeepSeek)]
        [InlineData(AIProvider.Gemini)]
        [InlineData(AIProvider.Groq)]
        public void ConfigurationUrl_CloudProviders_ReturnsNotApplicable(AIProvider cloudProvider)
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = cloudProvider };

            // Act & Assert
            settings.ConfigurationUrl.Should().Be("N/A - API Key based provider");
        }

        [Fact]
        public void ConfigurationUrl_Setting_UpdatesCorrectProvider()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama };

            // Act
            settings.ConfigurationUrl = "http://custom:11434";

            // Assert
            settings.OllamaUrl.Should().Be("http://custom:11434");
            settings.ConfigurationUrl.Should().Be("http://custom:11434");
        }

        #endregion

        #region Backing Field Preservation Tests

        [Fact]
        public void BackingFieldPreservation_AllProviders_MaintainsIndependentState()
        {
            // Arrange
            var settings = new BrainarrSettings();
            var testData = new Dictionary<AIProvider, (string url, string model, string apiKey)>
            {
                { AIProvider.Ollama, ("http://ollama:11434", "ollama-model", null) },
                { AIProvider.LMStudio, ("http://lmstudio:1234", "lm-model", null) },
                { AIProvider.OpenAI, (null, "GPT41", "sk-openai-key") },
                { AIProvider.Anthropic, (null, "ClaudeSonnet4", "sk-ant-key") }
            };

            // Act - Configure all providers
            foreach (var kvp in testData)
            {
                settings.Provider = kvp.Key;
                if (kvp.Value.url != null)
                    settings.ConfigurationUrl = kvp.Value.url;
                if (kvp.Value.model != null)
                    settings.ModelSelection = kvp.Value.model;
                if (kvp.Value.apiKey != null)
                    settings.ApiKey = kvp.Value.apiKey;
            }

            // Assert - All configurations preserved
            foreach (var kvp in testData)
            {
                settings.Provider = kvp.Key;

                if (kvp.Value.url != null)
                    settings.ConfigurationUrl.Should().Be(kvp.Value.url);

                if (kvp.Value.model != null)
                    settings.ModelSelection.Should().Be(kvp.Value.model);

                if (kvp.Value.apiKey != null)
                    settings.ApiKey.Should().Be(kvp.Value.apiKey);
            }
        }

        #endregion

        #region Complex Switching Scenarios Tests

        [Fact]
        public void ComplexSwitching_RapidProviderChanges_MaintainsConsistency()
        {
            // Arrange
            var settings = new BrainarrSettings();
            var providers = new[]
            {
                AIProvider.Ollama, AIProvider.OpenAI, AIProvider.Anthropic,
                AIProvider.Gemini, AIProvider.LMStudio, AIProvider.Groq
            };

            // Act - Rapid switching with partial configuration
            foreach (var provider in providers)
            {
                settings.Provider = provider;
                // Partially configure each
                if (IsCloudProvider(provider))
                {
                    settings.ApiKey = $"test-key-{provider}";
                }
                settings.MaxRecommendations = 15; // Common setting
            }

            // Final switch back to first provider
            settings.Provider = providers[0];

            // Assert - Common settings preserved, provider-specific logic works
            settings.MaxRecommendations.Should().Be(15);
            settings.Provider.Should().Be(providers[0]);

            // Each cloud provider should have its API key preserved
            foreach (var provider in providers.Where(IsCloudProvider))
            {
                settings.Provider = provider;
                settings.ApiKey.Should().Be($"test-key-{provider}");
            }
        }

        [Fact]
        public void ComplexSwitching_PartialConfigurations_DoesNotCorruptState()
        {
            // Arrange
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };

            // Act - Partial configuration and switching
            settings.ApiKey = "sk-partial-key";
            // Don't set model

            settings.Provider = AIProvider.Ollama;
            settings.ConfigurationUrl = "http://partial-ollama:11434";
            // Don't set model

            settings.Provider = AIProvider.Anthropic;
            // Don't set API key or model

            // Assert - No corruption, defaults work correctly
            settings.ModelSelection.Should().Be(BrainarrConstants.DefaultAnthropicModel); // Default for Anthropic
            settings.ApiKey.Should().BeNull(); // No API key set for Anthropic

            // Previous configurations should be preserved
            settings.Provider = AIProvider.OpenAI;
            settings.ApiKey.Should().Be("sk-partial-key");

            settings.Provider = AIProvider.Ollama;
            settings.ConfigurationUrl.Should().Be("http://partial-ollama:11434");
        }

        #endregion

        [Fact]
        public void ModelSelection_Gemini_IgnoresCrossProviderValues()
        {
            var settings = new BrainarrSettings();
            settings.Provider = AIProvider.OpenRouter;
            settings.ModelSelection = "qwen/qwen3-30b-a3b-2507";

            settings.Provider = AIProvider.Gemini;
            settings.ModelSelection = "qwen/qwen3-30b-a3b-2507";

            settings.GeminiModelId.Should().Be(BrainarrConstants.DefaultGeminiModel);
            settings.ModelSelection.Should().Be(BrainarrConstants.DefaultGeminiModel);
        }

        [Fact]
        public void ModelSelection_Gemini_AllowsRawApiIdentifiers()
        {
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Gemini
            };

            settings.ModelSelection = "gemini-1.5-pro";

            var expectedGeminiModel = ProviderModelNormalizer.Normalize(AIProvider.Gemini, "gemini-1.5-pro");
            settings.GeminiModelId.Should().Be(expectedGeminiModel);
            settings.ModelSelection.Should().Be(expectedGeminiModel);
        }

        #region Edge Cases Tests

        [Fact]
        public void EdgeCases_NullEmptyValues_HandleGracefully()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act & Assert - Test null/empty handling
            settings.Provider = AIProvider.OpenAI;
            settings.ApiKey = null;
            settings.ApiKey.Should().BeNull();

            settings.ApiKey = "";
            settings.ApiKey.Should().Be("");

            settings.ApiKey = "   ";
            settings.ApiKey.Should().Be("   ");

            // Local providers should handle null URLs gracefully
            settings.Provider = AIProvider.Ollama;
            settings.ConfigurationUrl = null;
            settings.ConfigurationUrl.Should().Be(BrainarrConstants.DefaultOllamaUrl);

            settings.ConfigurationUrl = "";
            settings.ConfigurationUrl.Should().Be(BrainarrConstants.DefaultOllamaUrl);
        }

        [Fact]
        public void EdgeCases_VeryLongValues_HandleCorrectly()
        {
            // Arrange
            var settings = new BrainarrSettings();
            var longApiKey = new string('a', 400); // Very long API key
            var longModelName = new string('m', 200); // Very long model name

            // Act & Assert - Should handle long values without corruption
            settings.Provider = AIProvider.OpenAI;
            settings.ApiKey = longApiKey;
            settings.ApiKey.Should().Be(longApiKey);

            settings.ModelSelection = longModelName;
            settings.ModelSelection.Should().Be(longModelName);

            // Switch providers and verify values preserved
            settings.Provider = AIProvider.Anthropic;
            settings.Provider = AIProvider.OpenAI;

            settings.ApiKey.Should().Be(longApiKey);
            settings.ModelSelection.Should().Be(longModelName);
        }

        [Fact]
        public void EdgeCases_SpecialCharacters_PreservesCorrectly()
        {
            // Arrange
            var settings = new BrainarrSettings();
            var specialApiKey = "sk-!@#$%^&*()_+-=[]{}|;':\",./<>?";
            var specialModelName = "model-with-special!@#$%^&*()_characters";

            // Act
            settings.Provider = AIProvider.OpenAI;
            settings.ApiKey = specialApiKey;
            settings.ModelSelection = specialModelName;

            // Switch and return
            settings.Provider = AIProvider.Anthropic;
            settings.Provider = AIProvider.OpenAI;

            // Assert
            settings.ApiKey.Should().Be(specialApiKey);
            settings.ModelSelection.Should().Be(specialModelName);
        }

        #endregion

        #region Helper Methods

        private void ConfigureProvider(BrainarrSettings settings, AIProvider provider, string testValue)
        {
            switch (provider)
            {
                case AIProvider.Ollama:
                    settings.OllamaUrl = $"http://test-{testValue}:11434";
                    settings.OllamaModel = $"model-{testValue}";
                    break;
                case AIProvider.LMStudio:
                    settings.LMStudioUrl = $"http://test-{testValue}:1234";
                    settings.LMStudioModel = $"model-{testValue}";
                    break;
                default:
                    settings.ApiKey = $"key-{testValue}";
                    settings.ModelSelection = $"model-{testValue}";
                    break;
            }
        }

        private string GetProviderConfig(BrainarrSettings settings, AIProvider provider)
        {
            return provider switch
            {
                AIProvider.Ollama => $"{settings.OllamaUrl}|{settings.OllamaModel}",
                AIProvider.LMStudio => $"{settings.LMStudioUrl}|{settings.LMStudioModel}",
                AIProvider.OpenAI => $"{settings.OpenAIApiKey}|{settings.OpenAIModel}",
                AIProvider.Anthropic => $"{settings.AnthropicApiKey}|{settings.AnthropicModel}",
                AIProvider.Perplexity => $"{settings.PerplexityApiKey}|{settings.PerplexityModel}",
                AIProvider.OpenRouter => $"{settings.OpenRouterApiKey}|{settings.OpenRouterModel}",
                AIProvider.DeepSeek => $"{settings.DeepSeekApiKey}|{settings.DeepSeekModel}",
                AIProvider.Gemini => $"{settings.GeminiApiKey}|{settings.GeminiModel}",
                AIProvider.Groq => $"{settings.GroqApiKey}|{settings.GroqModel}",
                _ => ""
            };
        }

        private bool IsCloudProvider(AIProvider provider)
        {
            return provider != AIProvider.Ollama && provider != AIProvider.LMStudio;
        }

        #endregion
    }
}
