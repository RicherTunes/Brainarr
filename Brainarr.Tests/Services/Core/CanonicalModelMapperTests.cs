using System;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    public class CanonicalModelMapperTests
    {
        #region Null and Empty Input Tests

        [Fact]
        public void ToCanonical_returns_empty_for_null_registryId()
        {
            var result = CanonicalModelMapper.ToCanonical(AIProvider.OpenAI, null);
            result.Should().Be(string.Empty);
        }

        [Fact]
        public void ToCanonical_returns_empty_for_whitespace_registryId()
        {
            var result = CanonicalModelMapper.ToCanonical(AIProvider.OpenAI, "   ");
            result.Should().Be(string.Empty);
        }

        [Fact]
        public void ToCanonical_string_overload_returns_empty_for_null_registryId()
        {
            var result = CanonicalModelMapper.ToCanonical("openai", null);
            result.Should().Be(string.Empty);
        }

        [Fact]
        public void ToCanonical_handles_null_provider_string()
        {
            var result = CanonicalModelMapper.ToCanonical((string?)null, "some-model");
            result.Should().NotBeNull();
        }

        #endregion

        #region OpenAI Model Tests

        [Theory]
        [InlineData("gpt-4.1", "GPT41")]
        [InlineData("gpt-4.1-mini", "GPT41_Mini")]
        [InlineData("gpt-4.1-nano", "GPT41_Nano")]
        [InlineData("gpt-4o", "GPT4o")]
        [InlineData("gpt-4o-mini", "GPT4o_Mini")]
        [InlineData("o4-mini", "O4_Mini")]
        public void ToCanonical_maps_openai_models(string rawId, string expectedCanonical)
        {
            var result = CanonicalModelMapper.ToCanonical(AIProvider.OpenAI, rawId);
            result.Should().Be(expectedCanonical);
        }

        [Fact]
        public void ToCanonical_handles_unknown_openai_model()
        {
            var result = CanonicalModelMapper.ToCanonical(AIProvider.OpenAI, "unknown-model");
            result.Should().NotBeNullOrEmpty();
            // Unknown models should be sanitized (slashes/dashes replaced with underscores and uppercased)
            result.Should().Be("UNKNOWN_MODEL");
        }

        #endregion

        #region Anthropic Model Tests

        [Theory]
        [InlineData("claude-sonnet-4-20250514", "ClaudeSonnet4")]
        [InlineData("claude-3-7-sonnet-20250219", "Claude37_Sonnet")]
        [InlineData("claude-3-5-haiku-20241022", "Claude35_Haiku")]
        [InlineData("claude-3-opus-latest", "Claude3_Opus")]
        [InlineData("claude-3-5-sonnet-latest", "Claude35_Sonnet")]
        public void ToCanonical_maps_anthropic_models(string rawId, string expectedCanonical)
        {
            var result = CanonicalModelMapper.ToCanonical(AIProvider.Anthropic, rawId);
            result.Should().Be(expectedCanonical);
        }

        #endregion

        #region Perplexity Model Tests

        [Theory]
        [InlineData("sonar-pro", "Sonar_Pro")]
        [InlineData("sonar-reasoning-pro", "Sonar_Reasoning_Pro")]
        [InlineData("sonar-reasoning", "Sonar_Reasoning")]
        [InlineData("sonar", "Sonar")]
        [InlineData("llama-3.1-sonar-large-128k-online", "Sonar_Large")]
        [InlineData("llama-3.1-sonar-small-128k-online", "Sonar_Small")]
        public void ToCanonical_maps_perplexity_models(string rawId, string expectedCanonical)
        {
            var result = CanonicalModelMapper.ToCanonical(AIProvider.Perplexity, rawId);
            result.Should().Be(expectedCanonical);
        }

        #endregion

        #region OpenRouter Model Tests

        [Theory]
        [InlineData("openrouter/auto", "Auto")]
        [InlineData("anthropic/claude-sonnet-4-20250514", "ClaudeSonnet4")]
        [InlineData("openai/gpt-4.1-mini", "GPT41_Mini")]
        [InlineData("google/gemini-2.5-flash", "Gemini25_Flash")]
        [InlineData("meta-llama/llama-3.3-70b-versatile", "Llama33_70B")]
        [InlineData("deepseek/deepseek-chat", "DeepSeekV3")]
        public void ToCanonical_maps_openrouter_models(string rawId, string expectedCanonical)
        {
            var result = CanonicalModelMapper.ToCanonical(AIProvider.OpenRouter, rawId);
            result.Should().Be(expectedCanonical);
        }

        #endregion

        #region DeepSeek Model Tests

        [Theory]
        [InlineData("deepseek-chat", "DeepSeek_Chat")]
        [InlineData("deepseek-reasoner", "DeepSeek_Reasoner")]
        [InlineData("deepseek-r1", "DeepSeek_R1")]
        [InlineData("deepseek-search", "DeepSeek_Search")]
        public void ToCanonical_maps_deepseek_models(string rawId, string expectedCanonical)
        {
            var result = CanonicalModelMapper.ToCanonical(AIProvider.DeepSeek, rawId);
            result.Should().Be(expectedCanonical);
        }

        #endregion

        #region Gemini Model Tests

        [Theory]
        [InlineData("gemini-2.5-pro", "Gemini_25_Pro")]
        [InlineData("gemini-2.5-flash", "Gemini_25_Flash")]
        [InlineData("gemini-2.5-flash-lite", "Gemini_25_Flash_Lite")]
        [InlineData("gemini-2.0-flash", "Gemini_20_Flash")]
        [InlineData("gemini-1.5-flash", "Gemini_15_Flash")]
        [InlineData("gemini-1.5-flash-8b", "Gemini_15_Flash_8B")]
        [InlineData("gemini-1.5-pro", "Gemini_15_Pro")]
        public void ToCanonical_maps_gemini_models(string rawId, string expectedCanonical)
        {
            var result = CanonicalModelMapper.ToCanonical(AIProvider.Gemini, rawId);
            result.Should().Be(expectedCanonical);
        }

        #endregion

        #region Groq Model Tests

        [Theory]
        [InlineData("llama-3.3-70b-versatile", "Llama33_70B_Versatile")]
        [InlineData("llama-3.3-70b-specdec", "Llama33_70B_SpecDec")]
        [InlineData("deepseek-r1-distill-llama-70b", "DeepSeek_R1_Distill_L70B")]
        [InlineData("llama-3.1-8b-instant", "Llama31_8B_Instant")]
        [InlineData("llama-3.1-70b-versatile", "Llama31_70B_Versatile")]
        [InlineData("mixtral-8x7b-32768", "Mixtral_8x7B")]
        public void ToCanonical_maps_groq_models(string rawId, string expectedCanonical)
        {
            var result = CanonicalModelMapper.ToCanonical(AIProvider.Groq, rawId);
            result.Should().Be(expectedCanonical);
        }

        #endregion

        #region Case Insensitivity Tests

        [Fact]
        public void ToCanonical_is_case_insensitive_for_provider()
        {
            var result1 = CanonicalModelMapper.ToCanonical("OpenAI", "gpt-4o");
            var result2 = CanonicalModelMapper.ToCanonical("openai", "gpt-4o");
            var result3 = CanonicalModelMapper.ToCanonical("OPENAI", "gpt-4o");

            result1.Should().Be(result2);
            result2.Should().Be(result3);
        }

        [Fact]
        public void ToCanonical_is_case_insensitive_for_model_lookup()
        {
            var result1 = CanonicalModelMapper.ToCanonical("openai", "gpt-4o");
            var result2 = CanonicalModelMapper.ToCanonical("openai", "GPT-4O");

            // Both should map to the canonical form
            result1.Should().Be("GPT4o");
            result2.Should().Be("GPT4o");
        }

        #endregion

        #region Sanitization Tests

        [Fact]
        public void ToCanonical_sanitizes_unknown_models_with_slashes()
        {
            var result = CanonicalModelMapper.ToCanonical(AIProvider.OpenAI, "some/unknown/model");
            result.Should().Be("SOME_UNKNOWN_MODEL");
        }

        [Fact]
        public void ToCanonical_sanitizes_unknown_models_with_dashes()
        {
            var result = CanonicalModelMapper.ToCanonical(AIProvider.OpenAI, "some-unknown-model");
            result.Should().Be("SOME_UNKNOWN_MODEL");
        }

        [Fact]
        public void ToCanonical_sanitizes_mixed_special_characters()
        {
            var result = CanonicalModelMapper.ToCanonical(AIProvider.OpenAI, "model/v1-beta");
            result.Should().Be("MODEL_V1_BETA");
        }

        #endregion

        #region Local Provider Tests

        [Fact]
        public void ToCanonical_returns_sanitized_for_ollama()
        {
            // Ollama uses custom model names that won't be in the map
            var result = CanonicalModelMapper.ToCanonical(AIProvider.Ollama, "llama3:8b");
            result.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void ToCanonical_returns_sanitized_for_lmstudio()
        {
            // LM Studio uses custom model names
            var result = CanonicalModelMapper.ToCanonical(AIProvider.LMStudio, "TheBloke/Llama-2-7B-GGUF");
            result.Should().NotBeNullOrEmpty();
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void ToCanonical_trims_whitespace_from_registryId()
        {
            var result = CanonicalModelMapper.ToCanonical(AIProvider.OpenAI, "  gpt-4o  ");
            result.Should().Be("GPT4o");
        }

        [Fact]
        public void ToCanonical_trims_whitespace_from_provider()
        {
            var result = CanonicalModelMapper.ToCanonical("  openai  ", "gpt-4o");
            result.Should().Be("GPT4o");
        }

        [Fact]
        public void ToCanonical_handles_empty_string_provider()
        {
            var result = CanonicalModelMapper.ToCanonical("", "some-model");
            result.Should().NotBeNull();
        }

        #endregion
    }
}
