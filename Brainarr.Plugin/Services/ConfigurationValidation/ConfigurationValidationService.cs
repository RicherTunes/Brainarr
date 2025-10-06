using System;
using System.Threading;
using System.Threading.Tasks;
using FluentValidationFailure = FluentValidation.Results.ValidationFailure;
using FluentValidationResult = FluentValidation.Results.ValidationResult;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.ConfigurationValidation
{
    public interface IConfigurationValidationService
    {
        ConfigurationValidationSummary ValidateSettings(BrainarrSettings settings);
        ConfigurationValidationSummary ValidateProviderConfiguration(ProviderConfiguration configuration);
        Task<ConfigurationValidationSummary> ValidateWithConnectionTestAsync(BrainarrSettings settings, IAIProvider provider, CancellationToken cancellationToken = default);
    }

    public sealed class ConfigurationValidationService : IConfigurationValidationService
    {
        private readonly Logger _logger;
        private readonly BrainarrSettingsValidator _settingsValidator;

        public ConfigurationValidationService(Logger logger, BrainarrSettingsValidator settingsValidator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settingsValidator = settingsValidator ?? throw new ArgumentNullException(nameof(settingsValidator));
        }

        public ConfigurationValidationSummary ValidateSettings(BrainarrSettings settings)
        {
            if (settings == null)
            {
                var failure = new FluentValidationResult(new[] { new FluentValidationFailure(nameof(BrainarrSettings), "Settings cannot be null.") });
                return new ConfigurationValidationSummary(failure);
            }

            var validationResult = _settingsValidator.Validate(settings);
            return new ConfigurationValidationSummary(validationResult);
        }

        public ConfigurationValidationSummary ValidateProviderConfiguration(ProviderConfiguration configuration)
        {
            if (configuration == null)
            {
                var failure = new FluentValidationResult(new[] { new FluentValidationFailure(nameof(ProviderConfiguration), "Provider configuration cannot be null.") });
                return new ConfigurationValidationSummary(failure);
            }

            var result = configuration.Validate();
            return new ConfigurationValidationSummary(result);
        }

        public async Task<ConfigurationValidationSummary> ValidateWithConnectionTestAsync(BrainarrSettings settings, IAIProvider provider, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var summary = ValidateSettings(settings);

            if (provider == null)
            {
                summary.AddWarning("No provider instance supplied for connection test.");
                summary.Metadata["connectionTest"] = "skipped";
                return summary;
            }

            bool connected;
            try
            {
                connected = await provider.TestConnectionAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Provider connection test threw an exception.");
                connected = false;
            }

            summary.Metadata["connectionTest"] = connected ? "success" : "failed";
            if (!connected)
            {
                summary.AddWarning("Provider connection test failed");
            }

            return summary;
        }
    }
}
