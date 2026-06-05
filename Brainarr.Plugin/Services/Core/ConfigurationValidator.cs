using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation.Results;
using Lidarr.Plugin.Common.Validation;
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
        // Step 1 — upfront credential/URL field validation via Common's TestValidationBuilder.
        //
        // Before this gate, an empty API key surfaced as a generic "Unable to connect to AI
        // provider" via the connection probe, which leaks no actionable signal to the user.
        // The builder gives the user a clear field-level "OpenAI API key is required" hint
        // BEFORE the provider construction races — same pattern apple/tidalarr use in their
        // indexer/download-client Test() overrides (TidalLidarrDownloadClient.cs:307).
        var builder = new TestValidationBuilder();
        AppendCredentialRequirements(builder, settings);
        builder.ApplyTo(failures);
        if (builder.HasFailures)
        {
            // Skip the connection probe — there's no way it can succeed when a required
            // credential is empty, and the probe would otherwise emit the generic "Unable
            // to connect" failure on top of the actionable one we just added.
            return;
        }

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
                    // Wave 76 UX: tell the user EXACTLY what to do — pull a model. The
                    // "no models" state is almost always a fresh Ollama install with no
                    // models pulled yet, which doesn't surface clearly otherwise.
                    failures.Add(new ValidationFailure(
                        "Model",
                        $"No models found at {settings.OllamaUrl}. Pull a model first: `ollama pull qwen2.5` (or any model from https://ollama.com/library), then click Test again."));
                }
            }
            else if (settings.Provider == AIProvider.LMStudio)
            {
                var models = SafeAsyncHelper.RunSafeSync(() => _modelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl));
                if (!models.Any())
                {
                    failures.Add(new ValidationFailure(
                        "Model",
                        $"No models loaded at {settings.LMStudioUrl}. In LM Studio, go to the Local Server tab and click 'Start Server' with a model loaded, then click Test again."));
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add(new ValidationFailure("Configuration", $"Validation error: {ex.Message}"));
        }
    }

    /// <summary>
    /// Per-provider field-level requirement mapping. Mirrors the availability switch in
    /// <c>AIProviderFactory.CheckProviderAvailability</c> but surfaces UX-friendly hints
    /// pointing the user at where to get the credential. ClaudeCodeCli has no field to
    /// validate (binary is auto-detected from PATH); subscription providers require the
    /// credentials file path to be set so the loader can attempt to read it (existence is
    /// verified inside <c>CheckProviderAvailability</c> via the corresponding
    /// <c>SubscriptionCredentialLoader</c>; the field gate here is just the path string).
    /// </summary>
    private static void AppendCredentialRequirements(TestValidationBuilder builder, BrainarrSettings settings)
    {
        switch (settings.Provider)
        {
            case AIProvider.Ollama:
                builder.RequireNonEmpty(nameof(BrainarrSettings.OllamaUrl), settings.OllamaUrl,
                    "Ollama URL is required. Default is http://localhost:11434 — point this at your local Ollama server.");
                break;
            case AIProvider.LMStudio:
                builder.RequireNonEmpty(nameof(BrainarrSettings.LMStudioUrl), settings.LMStudioUrl,
                    "LM Studio URL is required. Default is http://localhost:1234 — start the LM Studio local server with a model loaded first.");
                break;
            case AIProvider.Perplexity:
                builder.RequireNonEmpty(nameof(BrainarrSettings.PerplexityApiKey), settings.PerplexityApiKey,
                    "Perplexity API key is required. Get yours at https://www.perplexity.ai/settings/api.");
                break;
            case AIProvider.OpenAI:
                builder.RequireNonEmpty(nameof(BrainarrSettings.OpenAIApiKey), settings.OpenAIApiKey,
                    "OpenAI API key is required. Get yours at https://platform.openai.com/api-keys.");
                break;
            case AIProvider.Anthropic:
                builder.RequireNonEmpty(nameof(BrainarrSettings.AnthropicApiKey), settings.AnthropicApiKey,
                    "Anthropic API key is required. Get yours at https://console.anthropic.com/settings/keys.");
                break;
            case AIProvider.OpenRouter:
                builder.RequireNonEmpty(nameof(BrainarrSettings.OpenRouterApiKey), settings.OpenRouterApiKey,
                    "OpenRouter API key is required. Get yours at https://openrouter.ai/keys.");
                break;
            case AIProvider.DeepSeek:
                builder.RequireNonEmpty(nameof(BrainarrSettings.DeepSeekApiKey), settings.DeepSeekApiKey,
                    "DeepSeek API key is required. Get yours at https://platform.deepseek.com/api_keys.");
                break;
            case AIProvider.Gemini:
                builder.RequireNonEmpty(nameof(BrainarrSettings.GeminiApiKey), settings.GeminiApiKey,
                    "Gemini API key is required. Get yours at https://aistudio.google.com/apikey.");
                break;
            case AIProvider.Groq:
                builder.RequireNonEmpty(nameof(BrainarrSettings.GroqApiKey), settings.GroqApiKey,
                    "Groq API key is required. Get yours at https://console.groq.com/keys.");
                break;
            case AIProvider.ZaiGlm:
            case AIProvider.ZaiCoding:
                // ZaiCoding shares ZaiGlmApiKey — same Z.AI account credential, different endpoint.
                builder.RequireNonEmpty(nameof(BrainarrSettings.ZaiGlmApiKey), settings.ZaiGlmApiKey,
                    "Z.AI (GLM) API key is required. Sign up at https://open.bigmodel.cn/ — the same key powers both GLM-4 and coding-tuned models.");
                break;
            case AIProvider.ClaudeCodeSubscription:
                builder.RequireNonEmpty(nameof(BrainarrSettings.ClaudeCodeCredentialsPath), settings.ClaudeCodeCredentialsPath,
                    "Claude Code credentials path is required. Point this at ~/.claude/.credentials.json (or wherever the Claude Code CLI stored its session JSON).");
                break;
            case AIProvider.OpenAICodexSubscription:
                builder.RequireNonEmpty(nameof(BrainarrSettings.OpenAICodexCredentialsPath), settings.OpenAICodexCredentialsPath,
                    "OpenAI Codex credentials path is required. Point this at the JSON file the Codex CLI stored your subscription session in.");
                break;
            case AIProvider.ClaudeCodeCli:
                // CLI provider: the binary is auto-detected from PATH; no settings field
                // gates availability. Connection probe verifies the binary actually runs.
                break;
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
        // F-06: also reject SSRF/exfil vectors (cloud-metadata endpoints, path traversal); any host allowed.
        return NzbDrone.Core.ImportLists.Brainarr.Services.Security.SecureUrlValidator.IsSafeProviderUrl(url);
    }
}
