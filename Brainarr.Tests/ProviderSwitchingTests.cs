using System.Collections.Generic;
using Xunit;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using FluentAssertions;

namespace Brainarr.Tests
{
    public class ProviderSwitchingTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void Should_PreserveModelSettings_WhenProviderChanges()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Set up LM Studio with a specific model
            settings.Provider = AIProvider.LMStudio;
            settings.LMStudioModel = "custom-lm-studio-model";
            settings.ModelSelection = "custom-lm-studio-model";

            // Act - Switch to Ollama
            settings.Provider = AIProvider.Ollama;

            // Assert - Model should reset to Ollama default for current provider
            var modelSelection = settings.ModelSelection;
            modelSelection.Should().Be(BrainarrConstants.DefaultOllamaModel);

            // Verify LM Studio model is preserved (not reset to default)
            settings.LMStudioModel.Should().Be("custom-lm-studio-model");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Should_PreserveOtherProviderSettings_WhenSwitchingProviders()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Set up multiple providers with models
            settings.Provider = AIProvider.Ollama;
            settings.OllamaModel = "llama2:latest";

            settings.Provider = AIProvider.OpenAI;
            settings.OpenAIModel = "GPT4o";
            settings.OpenAIApiKey = "test-api-key";

            // Act - Switch back to Ollama
            settings.Provider = AIProvider.Ollama;

            // Assert - Each provider's settings are preserved independently
            settings.OllamaModel.Should().Be("llama2:latest"); // Preserved when switching back

            // OpenAI settings should still be preserved
            settings.OpenAIApiKey.Should().Be("test-api-key");
            settings.OpenAIModel.Should().Be("GPT4o"); // Preserved, not cleared
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Should_ReturnCorrectDefaultModel_ForEachProvider()
        {
            // Arrange
            var settings = new BrainarrSettings();
            var providerDefaults = new Dictionary<AIProvider, string>
            {
                { AIProvider.Ollama, BrainarrConstants.DefaultOllamaModel },
                { AIProvider.LMStudio, BrainarrConstants.DefaultLMStudioModel },
                { AIProvider.Perplexity, "Sonar_Large" },
                { AIProvider.OpenAI, "GPT4o_Mini" },
                { AIProvider.Anthropic, "Claude35_Haiku" },
                { AIProvider.OpenRouter, "Claude35_Haiku" },
                { AIProvider.DeepSeek, "DeepSeek_Chat" },
                { AIProvider.Gemini, "Gemini_15_Flash" },
                { AIProvider.Groq, "Llama33_70B" }
            };

            // Act & Assert
            foreach (var kvp in providerDefaults)
            {
                settings.Provider = kvp.Key;
                var modelSelection = settings.ModelSelection;
                modelSelection.Should().Be(kvp.Value, $"Provider {kvp.Key} should return default model {kvp.Value}");
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Should_ResetModel_WhenSameProviderSelected()
        {
            // Arrange
            var settings = new BrainarrSettings();
            settings.Provider = AIProvider.Ollama;
            settings.OllamaModel = "custom-model";
            settings.ModelSelection = "custom-model";

            // Act - Select same provider again (triggers reset)
            settings.Provider = AIProvider.Ollama;

            // Assert - Model should reset to default for same provider
            settings.ModelSelection.Should().Be(BrainarrConstants.DefaultOllamaModel);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Should_HandleMultipleProviderSwitches_Correctly()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act - Switch through multiple providers
            settings.Provider = AIProvider.LMStudio;
            settings.LMStudioModel = "lm-studio-model";

            settings.Provider = AIProvider.OpenAI;
            settings.OpenAIModel = "gpt-4";

            settings.Provider = AIProvider.Anthropic;
            settings.AnthropicModel = "claude-3";

            settings.Provider = AIProvider.Ollama;

            // Assert - Each provider's settings are preserved independently
            settings.LMStudioModel.Should().Be("lm-studio-model"); // Preserved
            settings.OpenAIModel.Should().Be("gpt-4"); // Preserved
            settings.AnthropicModel.Should().Be("claude-3"); // Preserved
            settings.ModelSelection.Should().Be(BrainarrConstants.DefaultOllamaModel);
        }

        [Fact]
        [Trait("Category", "EdgeCase")]
        public void Should_HandleNullModelGracefully_WhenProviderChanges()
        {
            // Arrange
            var settings = new BrainarrSettings();
            settings.Provider = AIProvider.LMStudio;
            // Don't set any model

            // Act - Switch provider
            settings.Provider = AIProvider.OpenAI;

            // Assert - Should use default for new provider
            settings.ModelSelection.Should().Be("GPT4o_Mini");
        }
    }
}
