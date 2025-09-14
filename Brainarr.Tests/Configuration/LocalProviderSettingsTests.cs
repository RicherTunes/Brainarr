using FluentAssertions;
using FluentValidation.Results;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Configuration.Providers;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class LocalProviderSettingsTests
    {
        [Fact]
        public void LMStudioProviderSettings_has_expected_defaults_and_accessors()
        {
            var s = new LMStudioProviderSettings();

            s.Url.Should().Be(BrainarrConstants.DefaultLMStudioUrl);
            s.Model.Should().Be(BrainarrConstants.DefaultLMStudioModel);
            s.GetApiKey().Should().BeNull();
            s.GetModel().Should().Be(s.Model);
            s.GetBaseUrl().Should().Be(s.Url);
            s.ProviderType.Should().Be(AIProvider.LMStudio);

            // Defaults when backing fields are empty
            s.Url = string.Empty;
            s.Model = string.Empty;
            s.Url.Should().Be(BrainarrConstants.DefaultLMStudioUrl);
            s.Model.Should().Be(BrainarrConstants.DefaultLMStudioModel);
        }

        [Fact]
        public void LMStudioProviderSettings_validator_accepts_valid_local_urls_and_rejects_invalid()
        {
            var validator = new LMStudioProviderSettingsValidator();
            var good = new LMStudioProviderSettings { Url = "http://localhost:1234", Model = "local-model" };
            var bad = new LMStudioProviderSettings { Url = "not a url", Model = "x" };

            ValidationResult vr1 = validator.Validate(good);
            vr1.IsValid.Should().BeTrue();

            ValidationResult vr2 = validator.Validate(bad);
            vr2.IsValid.Should().BeFalse();
            vr2.Errors.Should().ContainSingle(e => e.PropertyName == nameof(LMStudioProviderSettings.Url));
        }

        [Fact]
        public void OllamaProviderSettings_has_expected_defaults_and_accessors()
        {
            var s = new OllamaProviderSettings();

            s.Url.Should().Be(BrainarrConstants.DefaultOllamaUrl);
            s.Model.Should().Be(BrainarrConstants.DefaultOllamaModel);
            s.GetApiKey().Should().BeNull();
            s.GetModel().Should().Be(s.Model);
            s.GetBaseUrl().Should().Be(s.Url);
            s.ProviderType.Should().Be(AIProvider.Ollama);

            s.Url = string.Empty;
            s.Model = string.Empty;
            s.Url.Should().Be(BrainarrConstants.DefaultOllamaUrl);
            s.Model.Should().Be(BrainarrConstants.DefaultOllamaModel);
        }

        [Fact]
        public void OllamaProviderSettings_validator_accepts_valid_local_urls_and_rejects_invalid()
        {
            var validator = new OllamaProviderSettingsValidator();
            var good = new OllamaProviderSettings { Url = "http://127.0.0.1:11434", Model = "llama3" };
            var bad = new OllamaProviderSettings { Url = "file:///etc/passwd", Model = "llama3" };

            validator.Validate(good).IsValid.Should().BeTrue();
            validator.Validate(bad).IsValid.Should().BeFalse();
        }
    }
}
