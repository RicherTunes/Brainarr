using Xunit;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;

namespace Brainarr.Tests.Services.Core
{
    public class ModelNameFormatterTests
    {
        [Theory]
        [Trait("Category", "Unit")]
        [InlineData("GPT4o_Mini", "GPT-4o Mini")]
        [InlineData("Claude35_Sonnet", "Claude 3.5 Sonnet")]
        [InlineData("Claude3_Opus", "Claude 3 Opus")]
        [InlineData("Llama33_70B", "Llama 3.3 70B")]
        [InlineData("Llama32_8B", "Llama 3.2 8B")]
        [InlineData("Llama31_405B", "Llama 3.1 405B")]
        [InlineData("Gemini15_Pro", "Gemini 1.5 Pro")]
        [InlineData("Gemini20_Flash", "Gemini 2.0 Flash")]
        public void FormatEnumName_ShouldFormatCorrectly(string input, string expected)
        {
            var result = ModelNameFormatter.FormatEnumName(input);
            
            Assert.Equal(expected, result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void FormatEnumName_WithNull_ShouldReturnNull()
        {
            var result = ModelNameFormatter.FormatEnumName(null);
            
            Assert.Null(result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void FormatEnumName_WithEmpty_ShouldReturnEmpty()
        {
            var result = ModelNameFormatter.FormatEnumName("");
            
            Assert.Equal("", result);
        }

        [Theory]
        [Trait("Category", "Unit")]
        [InlineData("microsoft/phi-3-mini", "Phi 3 mini (microsoft)")]
        [InlineData("meta/llama-3.2-7b", "Llama 3 2 7b (meta)")]
        [InlineData("google/gemma-2-9b", "Gemma 2 9b (google)")]
        public void FormatModelName_WithSlash_ShouldFormatCorrectly(string input, string expected)
        {
            var result = ModelNameFormatter.FormatModelName(input);
            
            Assert.Equal(expected, result);
        }

        [Theory]
        [Trait("Category", "Unit")]
        [InlineData("qwen2.5:latest", "Qwen 2 5:latest")]
        [InlineData("llama3.2:7b", "Llama 3 2:7b")]
        [InlineData("mistral:latest", "Mistral:latest")]
        [InlineData("phi:3.5", "Phi:3.5")]
        public void FormatModelName_WithColon_ShouldFormatCorrectly(string input, string expected)
        {
            var result = ModelNameFormatter.FormatModelName(input);
            
            Assert.Equal(expected, result);
        }

        [Theory]
        [Trait("Category", "Unit")]
        [InlineData("qwen-2-5-coder", "Qwen 2 5 Coder")]
        [InlineData("llama_3_2_instruct", "Llama 3 2 Instruct")]
        [InlineData("mistral.7b.instruct", "Mistral 7b Instruct")]
        public void FormatModelName_WithVariousSeparators_ShouldFormatCorrectly(string input, string expected)
        {
            var result = ModelNameFormatter.FormatModelName(input);
            
            Assert.Equal(expected, result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void FormatModelName_WithNull_ShouldReturnUnknown()
        {
            var result = ModelNameFormatter.FormatModelName(null);
            
            Assert.Equal("Unknown Model", result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void FormatModelName_WithEmpty_ShouldReturnUnknown()
        {
            var result = ModelNameFormatter.FormatModelName("");
            
            Assert.Equal("Unknown Model", result);
        }

        [Theory]
        [Trait("Category", "EdgeCase")]
        [InlineData("QWEN", "Qwen")]
        [InlineData("llama", "Llama")]
        [InlineData("MISTRAL", "Mistral")]
        [InlineData("GeMmA", "Gemma")]
        [InlineData("PHI", "Phi")]
        [InlineData("CoDer", "Coder")]
        [InlineData("InStRuCt", "Instruct")]
        public void FormatModelName_CaseInsensitive_ShouldCapitalizeCorrectly(string input, string expected)
        {
            var result = ModelNameFormatter.FormatModelName(input);
            
            Assert.Equal(expected, result);
        }

        [Theory]
        [Trait("Category", "EdgeCase")]
        [InlineData("qwen--2.5", "Qwen 2 5")]
        [InlineData("llama___3.2", "Llama 3 2")]
        [InlineData("mistral   7b", "Mistral 7b")]
        public void FormatModelName_WithMultipleSeparators_ShouldCleanUp(string input, string expected)
        {
            var result = ModelNameFormatter.FormatModelName(input);
            
            Assert.Equal(expected, result);
        }
    }
}