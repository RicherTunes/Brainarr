using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Utils;
using Xunit;

namespace Brainarr.Tests.Utils
{
    [Trait("Category", "Unit")]
    public class ModelNameFormatterTests
    {
        [Theory]
        [InlineData("GPT41", "GPT-4.1")]
        [InlineData("GPT4o", "GPT-4o")]
        [InlineData("ClaudeSonnet4", "Claude Sonnet 4")]
        [InlineData("Claude37", "Claude 3.7")]
        [InlineData("Claude35", "Claude 3.5")]
        [InlineData("Claude3", "Claude 3")]
        [InlineData("Llama33", "Llama 3.3")]
        [InlineData("Llama32", "Llama 3.2")]
        [InlineData("Llama31", "Llama 3.1")]
        [InlineData("Gemini25", "Gemini 2.5")]
        [InlineData("Gemini20", "Gemini 2.0")]
        [InlineData("Gemini15", "Gemini 1.5")]
        [InlineData("OPEN_AI_GPT4o", "OPEN AI GPT-4o")]
        public void FormatEnumName_Rewrites_WellKnownTokens(string input, string expected)
        {
            ModelNameFormatter.FormatEnumName(input).Should().Be(expected);
        }

        [Theory]
        [InlineData("gemini-2.5-flash", "Gemini 2.5 Flash")]
        [InlineData("llama3.1:8b", "Llama3.1 (8B)")]
        [InlineData("mistral-7b-instruct:latest", "Mistral 7B Instruct")]
        [InlineData("qwen2.5-coder:7b", "Qwen2.5 Coder (7B)")]
        [InlineData("phi-3-mini:4.2b", "Phi 3 Mini (4.2B)")]
        [InlineData("gemma-2-9b:custom", "Gemma 2 9B (custom)")]
        public void FormatModelName_Normalizes_Id_And_Tag(string input, string expected)
        {
            ModelNameFormatter.FormatModelName(input).Should().Be(expected);
        }

        [Theory]
        [InlineData("  llama-3.1   instruct  ", "Llama 3.1 Instruct")]
        [InlineData("qwen-2.5\t\tCoder", "Qwen 2.5 Coder")]
        [InlineData("MiStRaL-7B", "Mistral 7B")]
        public void CleanModelName_Standardizes_Casing_And_Spacing(string input, string expected)
        {
            ModelNameFormatter.CleanModelName(input).Should().Be(expected);
        }

        [Fact]
        public void FormatModelName_Empty_Returns_Unknown()
        {
            ModelNameFormatter.FormatModelName(string.Empty).Should().Be("Unknown Model");
        }
    }
}
