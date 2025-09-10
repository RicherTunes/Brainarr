using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class ModelIdMapperTests
    {
        [Theory]
        [InlineData("openai", "GPT4o_Mini", "gpt-4o-mini")]
        [InlineData("openrouter", "Claude35_Sonnet", "anthropic/claude-3.5-sonnet")]
        [InlineData("perplexity", "Sonar_Large", "llama-3.1-sonar-large-128k-online")]
        [InlineData("deepseek", "DeepSeek_Chat", "deepseek-chat")]
        [InlineData("gemini", "Gemini_15_Flash", "gemini-1.5-flash")]
        [InlineData("groq", "Mixtral_8x7B", "mixtral-8x7b-32768")]
        public void ToRawId_maps_known_labels(string provider, string label, string expected)
        {
            ModelIdMapper.ToRawId(provider, label).Should().Be(expected);
        }

        [Fact]
        public void ToRawId_passes_through_unknown()
        {
            ModelIdMapper.ToRawId("unknown", "foo").Should().Be("foo");
        }
    }
}
