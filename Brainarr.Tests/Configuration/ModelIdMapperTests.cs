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
            var mapper = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId;
            mapper("openai", "GPT41").Should().Be("gpt-4.1");
            mapper("openai", "GPT41_Mini").Should().Be("gpt-4.1-mini");
            mapper("openai", "GPT41_Nano").Should().Be("gpt-4.1-nano");
            mapper("openai", "GPT4o").Should().Be("gpt-4o");
            mapper("openai", "GPT4o_Mini").Should().Be("gpt-4o-mini");
            mapper("openai", "O4_Mini").Should().Be("o4-mini");
            mapper("openai", "GPT4_Turbo").Should().Be("gpt-4.1");
        }

        [Fact]
        public void Perplexity_Maps_Modern_Sonar_Family()
        {
            var mapper = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId;
            mapper("perplexity", "Sonar_Pro").Should().Be("sonar-pro");
            mapper("perplexity", "Sonar_Reasoning_Pro").Should().Be("sonar-reasoning-pro");
            mapper("perplexity", "Sonar_Reasoning").Should().Be("sonar-reasoning");
            mapper("perplexity", "Sonar").Should().Be("sonar");
            mapper("perplexity", "Sonar_Large").Should().Be("llama-3.1-sonar-large-128k-online");
        }

        [Fact]
        public void Anthropic_Maps_Known_Labels()
        {
            var mapper = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId;
            mapper("anthropic", "ClaudeSonnet4").Should().Be("claude-sonnet-4-20250514");
            mapper("anthropic", "Claude37_Sonnet").Should().Be("claude-3-7-sonnet-20250219");
            mapper("anthropic", "Claude35_Haiku").Should().Be("claude-3-5-haiku-20241022");
            mapper("anthropic", "Claude3_Opus").Should().Be("claude-3-opus-latest");
            mapper("anthropic", "Claude35_Sonnet").Should().Be("claude-3-5-sonnet-latest");
        }

        [Fact]
        public void OpenRouter_Maps_Known_Labels()
        {
            var mapper = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId;
            mapper("openrouter", "Auto").Should().Be("openrouter/auto");
            mapper("openrouter", "ClaudeSonnet4").Should().Be("anthropic/claude-sonnet-4-20250514");
            mapper("openrouter", "GPT41_Mini").Should().Be("openai/gpt-4.1-mini");
            mapper("openrouter", "Llama33_70B").Should().Be("meta-llama/llama-3.3-70b-versatile");
            mapper("openrouter", "Gemini25_Flash").Should().Be("google/gemini-2.5-flash");
        }

        [Fact]
        public void DeepSeek_Maps_Known_Labels()
        {
            var mapper = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId;
            mapper("deepseek", "DeepSeek_Chat").Should().Be("deepseek-chat");
            mapper("deepseek", "DeepSeek_Reasoner").Should().Be("deepseek-reasoner");
            mapper("deepseek", "DeepSeek_R1").Should().Be("deepseek-r1");
            mapper("deepseek", "DeepSeek_Search").Should().Be("deepseek-search");
        }

        [Fact]
        public void Gemini_Maps_Known_Labels()
        {
            var mapper = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId;
            mapper("gemini", "Gemini_25_Pro").Should().Be("gemini-2.5-pro-latest");
            mapper("gemini", "Gemini_25_Flash").Should().Be("gemini-2.5-flash");
            mapper("gemini", "Gemini_25_Flash_Lite").Should().Be("gemini-2.5-flash-lite");
            mapper("gemini", "Gemini_20_Flash").Should().Be("gemini-2.0-flash");
            mapper("gemini", "Gemini_15_Flash").Should().Be("gemini-1.5-flash");
            mapper("gemini", "Gemini_15_Flash_8B").Should().Be("gemini-1.5-flash-8b");
            mapper("gemini", "Gemini_15_Pro").Should().Be("gemini-1.5-pro");
        }

        [Fact]
        public void Groq_Maps_Known_Labels()
        {
            var mapper = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId;
            mapper("groq", "Llama33_70B_Versatile").Should().Be("llama-3.3-70b-versatile");
            mapper("groq", "Llama33_70B_SpecDec").Should().Be("llama-3.3-70b-specdec");
            mapper("groq", "DeepSeek_R1_Distill_L70B").Should().Be("deepseek-r1-distill-llama-70b");
            mapper("groq", "Llama31_8B_Instant").Should().Be("llama-3.1-8b-instant");
        }

        [Fact]
        public void Unknown_Provider_Or_Label_Returns_Input()
        {
            var mapper = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId;
            mapper("unknown", "X").Should().Be("X");
            mapper("openai", "SomethingElse").Should().Be("SomethingElse");
        }
    }
}
