using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests.Characterization
{
    /// <summary>
    /// M6-1: Characterization tests locking BrainarrSettings behavior at public seams.
    /// BrainarrSettings (1197 lines) has complex provider-dependent property routing —
    /// these tests lock that behavior before any extraction in M6.
    /// </summary>
    [Trait("Category", "Characterization")]
    [Trait("Area", "Settings")]
    public class SettingsCharacterizationTests
    {
        // ─── Defaults: constructor produces sane defaults ─────────────────────

        [Fact]
        public void Constructor_DefaultProvider_IsOllama()
        {
            var settings = new BrainarrSettings();
            settings.Provider.Should().Be(AIProvider.Ollama);
        }

        [Fact]
        public void Constructor_DefaultSamplingStrategy_IsBalanced()
        {
            var settings = new BrainarrSettings();
            settings.SamplingStrategy.Should().Be(SamplingStrategy.Balanced);
        }

        [Fact]
        public void Constructor_DefaultDiscoveryMode_IsAdjacent()
        {
            var settings = new BrainarrSettings();
            settings.DiscoveryMode.Should().Be(DiscoveryMode.Adjacent);
        }

        [Fact]
        public void Constructor_DefaultRecommendationMode_IsSpecificAlbums()
        {
            var settings = new BrainarrSettings();
            settings.RecommendationMode.Should().Be(RecommendationMode.SpecificAlbums);
        }

        [Fact]
        public void Constructor_DefaultMaxRecommendations_IsPositive()
        {
            var settings = new BrainarrSettings();
            settings.MaxRecommendations.Should().BeGreaterThan(0);
        }

        // ─── ConfigurationUrl: provider-dependent routing ─────────────────────

        [Fact]
        public void ConfigurationUrl_Ollama_ReturnsOllamaUrl()
        {
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama };
            settings.ConfigurationUrl.Should().Contain("11434", "Ollama default port is 11434");
        }

        [Fact]
        public void ConfigurationUrl_LMStudio_ReturnsLMStudioUrl()
        {
            var settings = new BrainarrSettings { Provider = AIProvider.LMStudio };
            settings.ConfigurationUrl.Should().Contain("1234", "LM Studio default port is 1234");
        }

        [Theory]
        [InlineData(AIProvider.OpenAI)]
        [InlineData(AIProvider.Anthropic)]
        [InlineData(AIProvider.DeepSeek)]
        [InlineData(AIProvider.Gemini)]
        [InlineData(AIProvider.Groq)]
        [InlineData(AIProvider.Perplexity)]
        [InlineData(AIProvider.OpenRouter)]
        public void ConfigurationUrl_CloudProviders_ReturnsNAMessage(AIProvider provider)
        {
            var settings = new BrainarrSettings { Provider = provider };
            settings.ConfigurationUrl.Should().Contain("N/A",
                $"cloud provider {provider} should show N/A for ConfigurationUrl");
        }

        // ─── ModelSelection: each provider returns a valid model ──────────────

        [Theory]
        [InlineData(AIProvider.Ollama)]
        [InlineData(AIProvider.LMStudio)]
        [InlineData(AIProvider.OpenAI)]
        [InlineData(AIProvider.Anthropic)]
        [InlineData(AIProvider.Perplexity)]
        [InlineData(AIProvider.OpenRouter)]
        [InlineData(AIProvider.DeepSeek)]
        [InlineData(AIProvider.Gemini)]
        [InlineData(AIProvider.Groq)]
        public void ModelSelection_EachProvider_ReturnsNonEmpty(AIProvider provider)
        {
            var settings = new BrainarrSettings { Provider = provider };
            settings.ModelSelection.Should().NotBeNullOrWhiteSpace(
                $"default ModelSelection for {provider} should not be empty");
        }

        [Fact]
        public void EffectiveModel_EqualsModelSelection()
        {
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            settings.EffectiveModel.Should().Be(settings.ModelSelection,
                "EffectiveModel should be the same as ModelSelection");
        }

        // ─── Provider switch: preserves per-provider state ────────────────────

        [Fact]
        public void ProviderSwitch_PreservesOllamaUrl()
        {
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama };
            settings.ConfigurationUrl = "http://custom:11434";
            var savedUrl = settings.ConfigurationUrl;

            settings.Provider = AIProvider.OpenAI;
            settings.Provider = AIProvider.Ollama;

            settings.ConfigurationUrl.Should().Be(savedUrl,
                "switching away and back should preserve the Ollama URL");
        }

        // ─── EffectiveSamplingShape: defaults to non-null ─────────────────────

        [Fact]
        public void EffectiveSamplingShape_Default_IsNotNull()
        {
            var settings = new BrainarrSettings();
            settings.EffectiveSamplingShape.Should().NotBeNull();
        }

        // ─── EffectiveCacheSettings: defaults to non-null ─────────────────────

        [Fact]
        public void EffectiveCacheSettings_Default_IsNotNull()
        {
            var settings = new BrainarrSettings();
            settings.EffectiveCacheSettings.Should().NotBeNull();
        }

        // ─── Provider switch: auto-enables iterative refinement for local ────

        [Theory]
        [InlineData(AIProvider.Ollama)]
        [InlineData(AIProvider.LMStudio)]
        public void ProviderSwitch_LocalProvider_EnablesIterativeRefinement(AIProvider local)
        {
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            settings.EnableIterativeRefinement = false;

            settings.Provider = local;

            settings.EnableIterativeRefinement.Should().BeTrue(
                $"switching to local provider {local} should auto-enable iterative refinement");
        }
    }
}
