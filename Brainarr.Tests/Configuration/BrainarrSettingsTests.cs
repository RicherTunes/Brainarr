using System;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using Xunit;

namespace NzbDrone.Core.ImportLists.Brainarr.Tests.Configuration
{
    [Trait("Category", "Unit")]
    public class BrainarrSettingsTests
    {
        [Fact]
        public void ApiKey_Is_Trimmed_On_Set()
        {
            var s = new BrainarrSettings { Provider = AIProvider.OpenAI };

            s.ApiKey = "  sk-test-123  ";
            s.OpenAIApiKey.Should().Be("sk-test-123");
        }

        [Fact]
        public void ApiKey_Excessive_Length_Throws()
        {
            var s = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var longKey = new string('a', 501);
            Action act = () => s.OpenAIApiKey = longKey;
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Local_Urls_Normalize_For_Local_Providers()
        {
            var s = new BrainarrSettings { Provider = AIProvider.Ollama };
            s.OllamaUrl = "localhost:11434";
            s.OllamaUrl.Should().Be("http://localhost:11434");

            s.Provider = AIProvider.LMStudio;
            s.LMStudioUrl = "127.0.0.1:1234";
            s.LMStudioUrl.Should().Be("http://127.0.0.1:1234");
        }

        [Fact]
        public void SetModelForProvider_Sets_Provider_Specific_Model()
        {
            var s = new BrainarrSettings { Provider = AIProvider.OpenAI };
            s.SetModelForProvider("gpt-4o-mini");
            s.GetModelForProvider().Should().Be("gpt-4o-mini");

            s.Provider = AIProvider.Perplexity;
            s.SetModelForProvider("llama-3.1-sonar-small-128k-online");
            s.GetModelForProvider().Should().Be("llama-3.1-sonar-small-128k-online");
        }
    }
}
