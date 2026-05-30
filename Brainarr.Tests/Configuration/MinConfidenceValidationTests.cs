using FluentAssertions;
using FluentValidation.Results;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    /// <summary>
    /// MinConfidence is now a user-visible advanced setting (the "confidence floor": items below it
    /// are dropped or sent to the review queue). It must reject out-of-range values with a clear
    /// message rather than silently clamping, so the user knows what they typed was invalid.
    /// </summary>
    public class MinConfidenceValidationTests
    {
        private static BrainarrSettings Base() => new BrainarrSettings
        {
            Provider = AIProvider.Ollama,
            OllamaUrl = "http://localhost:11434",
            MaxRecommendations = BrainarrConstants.MinRecommendations
        };

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.5)]
        [InlineData(0.7)]
        [InlineData(1.0)]
        public void MinConfidence_InRange_Passes(double value)
        {
            var settings = Base();
            settings.MinConfidence = value;
            var result = new BrainarrSettingsValidator().Validate(settings);
            result.IsValid.Should().BeTrue($"{value} is a valid confidence floor");
        }

        [Theory]
        [InlineData(-0.1)]
        [InlineData(1.1)]
        [InlineData(2.0)]
        [InlineData(-5.0)]
        public void MinConfidence_OutOfRange_Fails(double value)
        {
            var settings = Base();
            settings.MinConfidence = value;
            var result = new BrainarrSettingsValidator().Validate(settings);
            result.IsValid.Should().BeFalse($"{value} is outside [0,1]");
            result.Errors.Should().Contain(e => e.PropertyName == nameof(BrainarrSettings.MinConfidence));
        }

        [Fact]
        public void MinConfidence_Default_IsValid()
        {
            // The shipped default (0.7) must pass its own validator.
            var result = new BrainarrSettingsValidator().Validate(Base());
            result.IsValid.Should().BeTrue();
        }
    }
}
