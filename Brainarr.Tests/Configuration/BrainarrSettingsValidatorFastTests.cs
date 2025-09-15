using FluentAssertions;
using FluentValidation.Results;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class BrainarrSettingsValidatorFastTests
    {
        [Fact]
        public void Ollama_ValidUrl_Passes()
        {
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "http://localhost:11434",
                MaxRecommendations = BrainarrConstants.MinRecommendations
            };
            var validator = new BrainarrSettingsValidator();
            ValidationResult result = validator.Validate(settings);
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void OpenAI_MissingApiKey_Fails()
        {
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                MaxRecommendations = BrainarrConstants.MinRecommendations
            };
            var validator = new BrainarrSettingsValidator();
            var result = validator.Validate(settings);
            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(BrainarrSettings.OpenAIApiKey));
        }
    }
}
