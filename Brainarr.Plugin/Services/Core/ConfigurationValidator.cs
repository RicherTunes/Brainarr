using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Utils;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core;

/// <summary>
/// Validates plugin configuration by testing provider connections and settings.
/// Extracted from <see cref="BrainarrOrchestrator"/> to isolate validation logic.
/// </summary>
internal sealed class ConfigurationValidator
{
    private readonly Logger _logger;
    private readonly ProviderLifecycleService _providerLifecycle;
    private readonly IModelDetectionService _modelDetection;

    public ConfigurationValidator(
        Logger logger,
        ProviderLifecycleService providerLifecycle,
        IModelDetectionService modelDetection)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _providerLifecycle = providerLifecycle ?? throw new ArgumentNullException(nameof(providerLifecycle));
        _modelDetection = modelDetection ?? throw new ArgumentNullException(nameof(modelDetection));
    }

    public void Validate(BrainarrSettings settings, List<ValidationFailure> failures)
    {
        try
        {
            var connectionTest = SafeAsyncHelper.RunSafeSync(() => _providerLifecycle.TestProviderConnectionAsync(settings));
            if (!connectionTest)
            {
                failures.Add(new ValidationFailure("Provider", "Unable to connect to AI provider"));
                if (_providerLifecycle.CurrentProvider != null)
                {
                    var hint = _providerLifecycle.CurrentProvider.GetLastUserMessage();
                    var docs = _providerLifecycle.CurrentProvider.GetLearnMoreUrl();
                    if (!string.IsNullOrWhiteSpace(hint))
                    {
                        var msg = _providerLifecycle.CurrentProvider.ProviderName + ": " + hint;
                        if (!string.IsNullOrWhiteSpace(docs))
                        {
                            msg += " (Learn more: " + docs + ")";
                        }
                        failures.Add(new ValidationFailure("Provider", msg));
                    }
                }
            }

            if (settings.Provider == AIProvider.Ollama)
            {
                var models = SafeAsyncHelper.RunSafeSync(() => _modelDetection.GetOllamaModelsAsync(settings.OllamaUrl));
                if (!models.Any())
                {
                    failures.Add(new ValidationFailure("Model", "No models detected for Ollama provider"));
                }
            }
            else if (settings.Provider == AIProvider.LMStudio)
            {
                var models = SafeAsyncHelper.RunSafeSync(() => _modelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl));
                if (!models.Any())
                {
                    failures.Add(new ValidationFailure("Model", "No models detected for LM Studio provider"));
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add(new ValidationFailure("Configuration", $"Validation error: {ex.Message}"));
        }
    }

    public static bool IsValidProviderConfiguration(BrainarrSettings settings)
    {
        try
        {
            if (settings.Provider == AIProvider.Ollama)
            {
                return IsValidHttpUrl(settings.OllamaUrl);
            }
            if (settings.Provider == AIProvider.LMStudio)
            {
                return IsValidHttpUrl(settings.LMStudioUrl);
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    public static bool IsValidHttpUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!(uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
              uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))) return false;
        if (!uri.IsDefaultPort && (uri.Port < 1 || uri.Port > 65535)) return false;
        return true;
    }
}
