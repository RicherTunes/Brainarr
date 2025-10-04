using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.ConfigurationValidation;
using Brainarr.Tests.Helpers;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class ConfigurationValidationServiceTests
    {
        private readonly ConfigurationValidationService _service;

        public ConfigurationValidationServiceTests()
        {
            _service = new ConfigurationValidationService(TestLogger.CreateNullLogger(), new BrainarrSettingsValidator());
        }

        [Fact]
        public void ValidateSettings_NullSettings_ReturnsFailure()
        {
            var summary = _service.ValidateSettings(null);

            summary.IsValid.Should().BeFalse();
            summary.ValidationResult.Errors.Should().Contain(e => e.PropertyName == nameof(BrainarrSettings));
        }

        [Fact]
        public void ValidateSettings_InvalidUrl_ReturnsFailure()
        {
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                OllamaUrl = "not-a-valid-url",
                MaxRecommendations = 20
            };

            var summary = _service.ValidateSettings(settings);

            summary.IsValid.Should().BeFalse();
            summary.ValidationResult.Errors.Should().Contain(e => e.PropertyName == nameof(BrainarrSettings.OllamaUrl));
        }

        [Fact]
        public void ValidateSettings_MissingCloudApiKey_ReturnsFailure()
        {
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Perplexity,
                PerplexityApiKey = string.Empty,
                MaxRecommendations = 20
            };

            var summary = _service.ValidateSettings(settings);

            summary.IsValid.Should().BeFalse();
            summary.ValidationResult.Errors.Should().Contain(e => e.PropertyName == nameof(BrainarrSettings.PerplexityApiKey));
        }

        [Fact]
        public void ValidateProviderConfiguration_NullConfig_ReturnsFailure()
        {
            var summary = _service.ValidateProviderConfiguration(null);

            summary.IsValid.Should().BeFalse();
            summary.ValidationResult.Errors.Should().Contain(e => e.PropertyName == nameof(ProviderConfiguration));
        }

        [Fact]
        public void ValidateProviderConfiguration_OllamaMissingUrl_ReturnsFailure()
        {
            var config = new OllamaProviderConfiguration { Url = string.Empty, Model = "llama2" };

            var summary = _service.ValidateProviderConfiguration(config);

            summary.IsValid.Should().BeFalse();
            summary.ValidationResult.Errors.Should().Contain(e => e.ErrorMessage.Contains("URL"));
        }

        [Fact]
        public void ValidateProviderConfiguration_OllamaValid_ReturnsSuccess()
        {
            var config = new OllamaProviderConfiguration { Url = "http://localhost:11434", Model = "llama2" };

            var summary = _service.ValidateProviderConfiguration(config);

            summary.IsValid.Should().BeTrue();
            summary.ValidationResult.Errors.Should().BeEmpty();
        }

        [Fact]
        public async Task ValidateWithProviderConnection_NullProvider_AddsWarning()
        {
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, MaxRecommendations = 20 };

            var summary = await _service.ValidateWithConnectionTestAsync(settings, null, CancellationToken.None);

            summary.IsValid.Should().BeTrue();
            summary.Warnings.Should().Contain(w => w.Contains("No provider instance"));
            summary.Metadata["connectionTest"].Should().Be("skipped");
        }

        [Fact]
        public async Task ValidateWithProviderConnection_WorkingProvider_ReturnsSuccess()
        {
            var providerMock = new Mock<IAIProvider>();
            providerMock.Setup(p => p.TestConnectionAsync()).ReturnsAsync(true);

            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, MaxRecommendations = 20 };

            var summary = await _service.ValidateWithConnectionTestAsync(settings, providerMock.Object);

            summary.IsValid.Should().BeTrue();
            summary.Metadata.Should().ContainKey("connectionTest");
            summary.Metadata["connectionTest"].Should().Be("success");
            summary.Warnings.Should().BeEmpty();
        }

        [Fact]
        public async Task ValidateWithProviderConnection_FailingProvider_AddsWarning()
        {
            var providerMock = new Mock<IAIProvider>();
            providerMock.Setup(p => p.TestConnectionAsync()).ReturnsAsync(false);

            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, MaxRecommendations = 20 };

            var summary = await _service.ValidateWithConnectionTestAsync(settings, providerMock.Object);

            summary.IsValid.Should().BeTrue();
            summary.Warnings.Should().Contain(w => w.Contains("connection test failed"));
            summary.Metadata["connectionTest"].Should().Be("failed");
        }

        [Fact]
        public async Task ValidateWithProviderConnection_WhenProviderThrows_AddsWarning()
        {
            var providerMock = new Mock<IAIProvider>();
            providerMock.Setup(p => p.TestConnectionAsync()).ThrowsAsync(new System.Exception("boom"));

            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, MaxRecommendations = 20 };

            var summary = await _service.ValidateWithConnectionTestAsync(settings, providerMock.Object);

            summary.IsValid.Should().BeTrue();
            summary.Warnings.Should().Contain(w => w.Contains("connection test failed"));
            summary.Metadata["connectionTest"].Should().Be("failed");
        }

        [Fact]
        public async Task ValidateWithProviderConnection_CancelledToken_Throws()
        {
            var providerMock = new Mock<IAIProvider>();
            var settings = new BrainarrSettings { Provider = AIProvider.Ollama, MaxRecommendations = 20 };
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                _service.ValidateWithConnectionTestAsync(settings, providerMock.Object, cts.Token));
        }
    }
}
