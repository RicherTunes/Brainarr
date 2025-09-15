using System.Linq;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class AdvancedProviderSettingsTests
    {
        [Fact]
        public void GetDefaults_returns_reasonable_values_for_selected_providers()
        {
            var ollama = AdvancedProviderSettings.GetDefaults(AIProvider.Ollama);
            ollama.TimeoutSeconds.Should().BeGreaterThanOrEqualTo(60);
            ollama.MaxTokens.Should().Be(2000);

            var groq = AdvancedProviderSettings.GetDefaults(AIProvider.Groq);
            groq.TimeoutSeconds.Should().Be(20);
            groq.MaxTokens.Should().Be(2000);

            var openai = AdvancedProviderSettings.GetDefaults(AIProvider.OpenAI);
            openai.TimeoutSeconds.Should().Be(30);
            openai.MaxTokens.Should().Be(1500);
        }

        [Fact]
        public void Validate_detects_out_of_range_values_and_provider_specific_rules()
        {
            var s = new AdvancedProviderSettings
            {
                Temperature = 3.0, // invalid
                TopP = 1.5,        // invalid
                MaxTokens = 50,    // invalid
                TimeoutSeconds = 1 // invalid generally and for local providers
            };

            var errorsLocal = s.Validate(AIProvider.Ollama);
            errorsLocal.Should().Contain(e => e.Contains("Temperature"));
            errorsLocal.Should().Contain(e => e.Contains("TopP"));
            errorsLocal.Should().Contain(e => e.Contains("MaxTokens"));
            errorsLocal.Should().Contain(e => e.Contains("Timeout"));
            errorsLocal.Should().Contain(e => e.Contains("Local models"));

            s.TimeoutSeconds = 60; // set to a higher value
            var errorsGroq = s.Validate(AIProvider.Groq);
            errorsGroq.Should().Contain(e => e.Contains("Groq is very fast"));
        }
    }
}
