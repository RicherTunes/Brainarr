using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class BrainarrSettingsMappingTests
    {
        [Fact]
        public void GetApiKeyForProvider_returns_correct_mapping()
        {
            var s = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                OpenAIApiKey = "oa"
            };
            s.GetApiKeyForProvider().Should().Be("oa");

            s.Provider = AIProvider.Groq;
            s.GroqApiKey = "g";
            s.GetApiKeyForProvider().Should().Be("g");
        }

        [Fact]
        public void GetBaseUrlForProvider_returns_local_urls()
        {
            var s = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                ConfigurationUrl = "http://localhost:11434"
            };
            s.GetBaseUrlForProvider().Should().Contain("11434");

            s.Provider = AIProvider.LMStudio;
            s.ConfigurationUrl = "http://localhost:1234";
            s.GetBaseUrlForProvider().Should().Contain("1234");
        }

        [Fact]
        public void GetIterationProfile_maps_backfill_strategy()
        {
            var s = new BrainarrSettings
            {
                BackfillStrategy = BackfillStrategy.Aggressive,
                IterativeMaxIterations = 5,
                TopUpStopSensitivity = StopSensitivity.Strict
            };

            var p = s.GetIterationProfile();
            p.EnableRefinement.Should().BeTrue();
            p.MaxIterations.Should().Be(5);
            p.ZeroStop.Should().BeGreaterThan(0);
            p.LowStop.Should().BeGreaterThan(0);
        }
    }
}
