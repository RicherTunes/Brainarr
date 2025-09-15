using FluentAssertions;
using Xunit;

namespace NzbDrone.Core.ImportLists.Brainarr.Tests.Configuration
{
    [Trait("Category", "Unit")]
    public class ModelIdMapperTests
    {
        [Fact]
        public void OpenAI_Maps_Known_Labels()
        {
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("openai", "GPT4o_Mini").Should().Be("gpt-4o-mini");
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("openai", "GPT4o").Should().Be("gpt-4o");
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("openai", "GPT4_Turbo").Should().Be("gpt-4-turbo");
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("openai", "GPT35_Turbo").Should().Be("gpt-3.5-turbo");
        }

        [Fact]
        public void Perplexity_Maps_Sonar_And_Llama()
        {
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("perplexity", "Sonar_Large").Should().Be("llama-3.1-sonar-large-128k-online");
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("perplexity", "Sonar_Small").Should().Be("llama-3.1-sonar-small-128k-online");
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("perplexity", "Llama31_70B_Instruct").Should().Be("llama-3.1-70b-instruct");
            // Accept popular slugs
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("perplexity", "llama-3.1-sonar-large").Should().Be("llama-3.1-sonar-large-128k-online");
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("perplexity", "sonar-large").Should().Be("llama-3.1-sonar-large-128k-online");
        }

        [Fact]
        public void Anthropic_Maps_Known_Labels()
        {
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("anthropic", "Claude35_Haiku").Should().Be("claude-3-5-haiku-latest");
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("anthropic", "Claude35_Sonnet").Should().Be("claude-3-5-sonnet-latest");
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("anthropic", "Claude3_Opus").Should().Be("claude-3-opus-latest");
        }

        [Fact]
        public void OpenRouter_Maps_Known_Labels()
        {
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("openrouter", "Claude35_Sonnet").Should().Be("anthropic/claude-3.5-sonnet");
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("openrouter", "GPT4o_Mini").Should().Be("openai/gpt-4o-mini");
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("openrouter", "Llama3_70B").Should().Be("meta-llama/llama-3.1-70b-instruct");
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("openrouter", "Gemini15_Flash").Should().Be("google/gemini-1.5-flash");
        }

        [Fact]
        public void DeepSeek_Maps_Known_Labels()
        {
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("deepseek", "DeepSeek_Chat").Should().Be("deepseek-chat");
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("deepseek", "DeepSeek_Reasoner").Should().Be("deepseek-reasoner");
        }

        [Fact]
        public void Gemini_Maps_Known_Labels()
        {
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("gemini", "Gemini_15_Flash").Should().Be("gemini-1.5-flash");
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("gemini", "Gemini_15_Pro").Should().Be("gemini-1.5-pro");
        }

        [Fact]
        public void Groq_Maps_Known_Labels()
        {
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("groq", "Llama31_70B_Versatile").Should().Be("llama-3.1-70b-versatile");
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("groq", "Mixtral_8x7B").Should().Be("mixtral-8x7b-32768");
        }

        [Fact]
        public void Unknown_Provider_Or_Label_Returns_Input()
        {
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("unknown", "X").Should().Be("X");
            NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("openai", "SomethingElse").Should().Be("SomethingElse");
        }
    }
}
