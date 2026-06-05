using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace NzbDrone.Core.ImportLists.Brainarr.Tests.Configuration
{
    /// <summary>
    /// R2-04: the top-level BrainarrSettingsValidator (the user-facing Ollama/LM Studio URL fields) must run
    /// the SecureUrlValidator SSRF guard, not only the permissive UrlValidator.IsValidLocalProviderUrl (which
    /// accepts any parseable IP — including cloud-metadata endpoints). Closes the F-06 gap the Round-2 review
    /// found: the "wired into all active validators" claim missed this validator.
    /// </summary>
    [Trait("Category", "Unit")]
    public class BrainarrSettingsValidatorSsrfTests
    {
        [Theory]
        [InlineData(AIProvider.Ollama)]
        [InlineData(AIProvider.LMStudio)]
        public void MetadataEndpointUrl_IsRejected(AIProvider provider)
        {
            var settings = new BrainarrSettings { Provider = provider };
            if (provider == AIProvider.Ollama) settings.OllamaUrl = "http://169.254.169.254/latest/meta-data/";
            else settings.LMStudioUrl = "http://169.254.169.254/latest/meta-data/";

            var result = new BrainarrSettingsValidator().Validate(settings);

            result.IsValid.Should().BeFalse("a cloud-metadata endpoint must never pass provider-URL validation");
        }

        [Theory]
        [InlineData(AIProvider.Ollama, "http://localhost:11434")]
        [InlineData(AIProvider.LMStudio, "http://localhost:1234")]
        [InlineData(AIProvider.Ollama, "http://192.168.1.10:11434")]
        public void LegitimateLocalUrl_StillValid(AIProvider provider, string url)
        {
            var settings = new BrainarrSettings { Provider = provider };
            if (provider == AIProvider.Ollama) settings.OllamaUrl = url;
            else settings.LMStudioUrl = url;

            var result = new BrainarrSettingsValidator().Validate(settings);

            // The URL rule itself must not fail for a legit local provider URL (other rules may still apply).
            result.Errors.Should().NotContain(e => e.PropertyName == nameof(BrainarrSettings.OllamaUrl) || e.PropertyName == nameof(BrainarrSettings.LMStudioUrl));
        }
    }
}
