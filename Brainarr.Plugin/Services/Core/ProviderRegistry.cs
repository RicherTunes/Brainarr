using System;
using System.Collections.Generic;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Providers.ClaudeCode;
using Lidarr.Plugin.Common.Subprocess;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Registry pattern for managing AI provider creation.
    /// Eliminates switch statements and enables extensibility.
    /// </summary>
    public interface IProviderRegistry
    {
        /// <summary>
        /// Registers a provider factory function.
        /// </summary>
        void Register(AIProvider type, Func<BrainarrSettings, IHttpClient, Logger, IAIProvider> factory);

        /// <summary>
        /// Creates a provider instance.
        /// </summary>
        IAIProvider CreateProvider(AIProvider type, BrainarrSettings settings, IHttpClient httpClient, Logger logger);

        /// <summary>
        /// Checks if a provider type is registered.
        /// </summary>
        bool IsRegistered(AIProvider type);

        /// <summary>
        /// Gets all registered provider types.
        /// </summary>
        IEnumerable<AIProvider> GetRegisteredProviders();
    }

    public class ProviderRegistry : IProviderRegistry
    {
        private readonly Dictionary<AIProvider, Func<BrainarrSettings, IHttpClient, Logger, IAIProvider>> _factories;
        private readonly Dictionary<AIProvider, Func<string, string>> _modelMappers;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.IHttpResilience? _httpExec;

        public ProviderRegistry()
        {
            _factories = new Dictionary<AIProvider, Func<BrainarrSettings, IHttpClient, Logger, IAIProvider>>();
            _modelMappers = new Dictionary<AIProvider, Func<string, string>>();
            _httpExec = null; // Providers will fall back to static resilience when null

            // Register all providers
            RegisterProviders();

            // Sanity-check model mappings on startup (warn-only by default)
            try { ModelIdMappingValidator.AssertValid(false, LogManager.GetCurrentClassLogger()); } catch (Exception) { /* Non-critical */ }
        }

        public ProviderRegistry(NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.IHttpResilience httpExec)
        {
            _factories = new Dictionary<AIProvider, Func<BrainarrSettings, IHttpClient, Logger, IAIProvider>>();
            _modelMappers = new Dictionary<AIProvider, Func<string, string>>();
            _httpExec = httpExec ?? throw new ArgumentNullException(nameof(httpExec));

            // Register all providers
            RegisterProviders();

            // Sanity-check model mappings on startup (warn-only by default)
            try { ModelIdMappingValidator.AssertValid(false, LogManager.GetCurrentClassLogger()); } catch (Exception) { /* Non-critical */ }
        }

        private void RegisterProviders()
        {
            // All providers route through LlmProviderAdapter + ILlmProvider (Phase 4 wave 4a-4d).
            // Legacy concrete providers and the BRAINARR_USE_LEGACY_LLM_PROVIDERS rollback gate
            // were removed in Phase 6 cleanup after stabilization.

            Register(AIProvider.Ollama, (settings, http, logger) =>
            {
                ILlmProvider llm = new BrainarrOllamaProvider(
                    http,
                    logger,
                    baseUrl: settings.OllamaUrl ?? BrainarrConstants.DefaultOllamaUrl,
                    model: settings.OllamaModel ?? BrainarrConstants.DefaultOllamaModel);
                return new LlmProviderAdapter(llm, logger);
            });

            Register(AIProvider.LMStudio, (settings, http, logger) =>
            {
                ILlmProvider llm = new BrainarrLmStudioProvider(
                    http,
                    logger,
                    baseUrl: settings.LMStudioUrl ?? BrainarrConstants.DefaultLMStudioUrl,
                    model: settings.LMStudioModel ?? BrainarrConstants.DefaultLMStudioModel);
                return new LlmProviderAdapter(llm, logger);
            });

            Register(AIProvider.Perplexity, (settings, http, logger) =>
            {
                var model = !string.IsNullOrWhiteSpace(settings.ManualModelId)
                    ? settings.ManualModelId
                    : MapPerplexityModel(settings.PerplexityModelId);

                ILlmProvider llm = new BrainarrPerplexityProvider(http, logger, settings.PerplexityApiKey, model);
                return new LlmProviderAdapter(llm, logger);
            });

            Register(AIProvider.OpenAI, (settings, http, logger) =>
            {
                var model = !string.IsNullOrWhiteSpace(settings.ManualModelId)
                    ? settings.ManualModelId
                    : MapOpenAIModel(settings.OpenAIModelId);

                ILlmProvider llm = new BrainarrOpenAiProvider(http, logger, settings.OpenAIApiKey, model);
                return new LlmProviderAdapter(llm, logger);
            });

            Register(AIProvider.Anthropic, (settings, http, logger) =>
            {
                var model = !string.IsNullOrWhiteSpace(settings.ManualModelId)
                    ? settings.ManualModelId
                    : MapAnthropicModel(settings.AnthropicModelId);
                // If thinking mode requests thinking, append sentinel so provider can include the proper param
                if (settings.ThinkingMode != NzbDrone.Core.ImportLists.Brainarr.ThinkingMode.Off && !string.IsNullOrWhiteSpace(model) && !model.Contains("#thinking"))
                {
                    model += "#thinking";
                    if (settings.ThinkingBudgetTokens > 0)
                    {
                        model += $"(tokens={settings.ThinkingBudgetTokens})";
                    }
                }

                ILlmProvider llm = new BrainarrAnthropicProvider(http, logger, settings.AnthropicApiKey, model);
                return new LlmProviderAdapter(llm, logger);
            });

            Register(AIProvider.OpenRouter, (settings, http, logger) =>
            {
                // For OpenRouter, allow direct pass-through IDs (e.g., "anthropic/claude-3.5-sonnet")
                var model = !string.IsNullOrWhiteSpace(settings.ManualModelId)
                    ? settings.ManualModelId
                    : MapOpenRouterModel(settings.OpenRouterModelId);
                // Convenience: auto-switch Anthropic routes to :thinking variant if thinking mode is not Off
                if (settings.ThinkingMode != NzbDrone.Core.ImportLists.Brainarr.ThinkingMode.Off &&
                    !string.IsNullOrWhiteSpace(model) &&
                    model.StartsWith("anthropic/claude-", StringComparison.OrdinalIgnoreCase) &&
                    !model.Contains(":thinking", StringComparison.OrdinalIgnoreCase))
                {
                    model += ":thinking";
                }

                ILlmProvider llm = new BrainarrOpenRouterProvider(http, logger, settings.OpenRouterApiKey, model);
                return new LlmProviderAdapter(llm, logger);
            });

            Register(AIProvider.DeepSeek, (settings, http, logger) =>
            {
                var model = !string.IsNullOrWhiteSpace(settings.ManualModelId)
                    ? settings.ManualModelId
                    : MapDeepSeekModel(settings.DeepSeekModelId);

                ILlmProvider llm = new BrainarrDeepSeekProvider(http, logger, settings.DeepSeekApiKey, model);
                return new LlmProviderAdapter(llm, logger);
            });

            Register(AIProvider.Gemini, (settings, http, logger) =>
            {
                var model = !string.IsNullOrWhiteSpace(settings.ManualModelId)
                    ? settings.ManualModelId
                    : MapGeminiModel(settings.GeminiModelId);

                ILlmProvider llm = new BrainarrGeminiProvider(http, logger, settings.GeminiApiKey, model);
                return new LlmProviderAdapter(llm, logger);
            });

            Register(AIProvider.Groq, (settings, http, logger) =>
            {
                var model = !string.IsNullOrWhiteSpace(settings.ManualModelId)
                    ? settings.ManualModelId
                    : MapGroqModel(settings.GroqModelId);

                ILlmProvider llm = new BrainarrGroqProvider(http, logger, settings.GroqApiKey, model);
                return new LlmProviderAdapter(llm, logger);
            });

            // Z.AI (Zhipu) GLM — OpenAI-compatible PaaS endpoint, pay-per-token.
            Register(AIProvider.ZaiGlm, (settings, http, logger) =>
            {
                var model = !string.IsNullOrWhiteSpace(settings.ManualModelId)
                    ? settings.ManualModelId
                    : MapZaiGlmModel(settings.ZaiGlmModelId);

                ILlmProvider llm = new BrainarrZaiGlmProvider(http, logger, settings.ZaiGlmApiKey, model);
                return new LlmProviderAdapter(llm, logger);
            });

            // Z.AI Coding Plan — Anthropic-Messages-compatible endpoint for subscribers.
            // Uses the same ZaiGlmApiKey field (one credential, two endpoints). Default model
            // is the Coding Plan flagship since subscribers paid specifically for premium access.
            Register(AIProvider.ZaiCoding, (settings, http, logger) =>
            {
                var model = !string.IsNullOrWhiteSpace(settings.ManualModelId)
                    ? settings.ManualModelId
                    : MapZaiCodingModel(settings.ZaiCodingModelId);

                ILlmProvider llm = new BrainarrZaiCodingProvider(http, logger, settings.ZaiGlmApiKey, model);
                return new LlmProviderAdapter(llm, logger);
            });

            // Subscription-based providers (use credential files instead of API keys).
            Register(AIProvider.ClaudeCodeSubscription, (settings, http, logger) =>
            {
                var model = !string.IsNullOrWhiteSpace(settings.ManualModelId)
                    ? settings.ManualModelId
                    : settings.ClaudeCodeModelId ?? BrainarrConstants.DefaultClaudeCodeModel;

                ILlmProvider llm = new BrainarrClaudeCodeSubscriptionProvider(
                    http, logger, settings.ClaudeCodeCredentialsPath, model);
                return new LlmProviderAdapter(llm, logger);
            });

            Register(AIProvider.OpenAICodexSubscription, (settings, http, logger) =>
            {
                var model = !string.IsNullOrWhiteSpace(settings.ManualModelId)
                    ? settings.ManualModelId
                    : settings.OpenAICodexModelId ?? BrainarrConstants.DefaultOpenAICodexModel;

                ILlmProvider llm = new BrainarrOpenAiCodexSubscriptionProvider(
                    http, logger, settings.OpenAICodexCredentialsPath, model);
                return new LlmProviderAdapter(llm, logger);
            });

            // Wave-4d: adopt common's ClaudeCodeProvider as a SECOND Claude option.
            //
            // ClaudeCodeSubscription (above) reads OAuth tokens from the CLI's credentials file
            // and hits Anthropic REST directly — no CLI required at runtime.
            //
            // ClaudeCodeCli (this registration) shells out to the `claude` binary itself and
            // lets the CLI handle authentication, transport, streaming, and capability probing.
            // No web-OAuth flow is required from inside Lidarr; the user runs `claude login`
            // once on the host. Detection covers npm-global, native installer, and Homebrew
            // installations via ClaudeCodeDetector.
            Register(AIProvider.ClaudeCodeCli, (settings, http, logger) =>
            {
                var model = !string.IsNullOrWhiteSpace(settings.ManualModelId)
                    ? settings.ManualModelId
                    : settings.ClaudeCodeModelId ?? "sonnet";

                var cliRunner = new CliRunner();
                var detector = new ClaudeCodeDetector(cliRunner);
                var ccSettings = new ClaudeCodeSettings { Model = model };
                ILlmProvider llm = new ClaudeCodeProvider(cliRunner, detector, ccSettings);
                return new LlmProviderAdapter(llm, logger);
            });
        }

        public void Register(AIProvider type, Func<BrainarrSettings, IHttpClient, Logger, IAIProvider> factory)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            _factories[type] = factory;
        }

        public IAIProvider CreateProvider(AIProvider type, BrainarrSettings settings, IHttpClient httpClient, Logger logger)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (httpClient == null)
                throw new ArgumentNullException(nameof(httpClient));
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            if (!_factories.TryGetValue(type, out var factory))
            {
                throw new NotSupportedException($"Provider type {type} is not supported");
            }

            return factory(settings, httpClient, logger);
        }

        public bool IsRegistered(AIProvider type)
        {
            return _factories.ContainsKey(type);
        }

        public IEnumerable<AIProvider> GetRegisteredProviders()
        {
            return _factories.Keys;
        }

        #region Model Mapping Methods

        private string MapPerplexityModel(string modelEnum)
        {
            // Delegate to centralized mapper to avoid drift between code paths
            return NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("perplexity", modelEnum);
        }

        private string MapOpenAIModel(string modelEnum)
        {
            return modelEnum switch
            {
                "GPT5" => "gpt-5",
                "GPT4_1" => "gpt-4.1",
                "GPT4_1_Mini" => "gpt-4.1-mini",
                "GPT4o" => "gpt-4o",
                "GPT4o_Mini" => "gpt-4o-mini",
                "GPT4_Turbo" => "gpt-4-turbo",
                "GPT35_Turbo" => "gpt-3.5-turbo",
                _ => "gpt-4o-mini"
            };
        }

        private string MapAnthropicModel(string modelEnum)
        {
            return modelEnum switch
            {
                "Claude41_Opus" => "claude-4.1-opus-latest",
                "Claude40_Sonnet" => "claude-4.0-sonnet-latest",
                "Claude37_Sonnet" => "claude-3-7-sonnet-latest",
                // Special handling for extended thinking via sentinel
                "Claude37_Sonnet_Thinking" => "claude-3-7-sonnet-latest#thinking",
                "Claude35_Sonnet" => "claude-3-5-sonnet-latest",
                "Claude35_Haiku" => "claude-3-5-haiku-latest",
                "Claude3_Opus" => "claude-3-opus-20240229",
                _ => "claude-3-5-haiku-latest"
            };
        }

        private string MapOpenRouterModel(string modelEnum)
        {
            if (string.IsNullOrWhiteSpace(modelEnum)) return "anthropic/claude-3.5-haiku";

            // Pass-through if already a full model id like "vendor/model"
            if (modelEnum.Contains("/")) return modelEnum;

            return modelEnum switch
            {
                "Claude35_Haiku" => "anthropic/claude-3.5-haiku",
                "DeepSeekV3" => "deepseek/deepseek-chat",
                "Gemini_Flash" => "google/gemini-flash-1.5",
                "Claude35_Sonnet" => "anthropic/claude-3.5-sonnet",
                "Claude37_Sonnet" => "anthropic/claude-3.7-sonnet",
                "Claude37_Sonnet_Thinking" => "anthropic/claude-3.7-sonnet:thinking",
                "GPT4o_Mini" => "openai/gpt-4o-mini",
                // Update Llama slug to 3.1
                "Llama3_70B" => "meta-llama/llama-3.1-70b-instruct",
                "GPT4o" => "openai/gpt-4o",
                "Claude3_Opus" => "anthropic/claude-3-opus",
                "Gemini_Pro" => "google/gemini-pro-1.5",
                "Mistral_Large" => "mistral/mistral-large",
                // Update Qwen to 2.5 instruct
                "Qwen_72B" => "qwen/qwen-2.5-72b-instruct",
                _ => "anthropic/claude-3.5-haiku"
            };
        }

        private string MapDeepSeekModel(string modelEnum)
        {
            return modelEnum switch
            {
                "DeepSeek_Chat" => "deepseek-chat",
                "DeepSeek_Coder" => "deepseek-coder",
                "DeepSeek_Reasoner" => "deepseek-reasoner",
                _ => "deepseek-chat"
            };
        }

        private string MapGeminiModel(string modelEnum)
        {
            return modelEnum switch
            {
                "Gemini_15_Flash" => "gemini-1.5-flash",
                "Gemini_15_Flash_8B" => "gemini-1.5-flash-8b",
                "Gemini_15_Pro" => "gemini-1.5-pro",
                "Gemini_20_Flash" => "gemini-2.0-flash-exp",
                _ => "gemini-1.5-flash"
            };
        }

        private string MapGroqModel(string modelEnum)
        {
            return modelEnum switch
            {
                "Llama33_70B" => "llama-3.3-70b-versatile",
                "Llama32_90B_Vision" => "llama-3.2-90b-vision-preview",
                "Llama31_70B" => "llama-3.1-70b-versatile",
                "Mixtral_8x7B" => "mixtral-8x7b-32768",
                "Gemma2_9B" => "gemma2-9b-it",
                _ => "llama-3.3-70b-versatile"
            };
        }

        // Z.AI / Zhipu GLM model ID mapping. Enum -> canonical API id as documented
        // at docs.z.ai. Default falls through to glm-4.5-air which is the best
        // cost/quality balance for music recommendation prompt sizes.
        // Z.AI / Zhipu GLM model ID mapping. Catalog from z.ai/manage-apikey/rate-limits.
        // Default falls through to glm-4.5-air (broadest availability on PaaS / pay-per-token).
        private string MapZaiGlmModel(string? modelEnum)
        {
            return modelEnum switch
            {
                "GLM_5_1" => "glm-5.1",
                "GLM_5" => "glm-5",
                "GLM_5_Turbo" => "glm-5-turbo",
                "GLM_4_7" => "glm-4.7",
                "GLM_4_7_Flash" => "glm-4.7-flash",
                "GLM_4_7_FlashX" => "glm-4.7-flashx",
                "GLM_4_6" => "glm-4.6",
                "GLM_4_5" => "glm-4.5",
                "GLM_4_5_Air" => "glm-4.5-air",
                "GLM_4_5_AirX" => "glm-4.5-airx",
                "GLM_4_5_Flash" => "glm-4.5-flash",
                "GLM_4_Plus" => "glm-4-plus",
                "GLM_4_32B" => "glm-4-32b-0414-128k",
                _ => "glm-4.5-air"
            };
        }

        // Z.AI Coding Plan model ID mapping. Same raw-id catalog as MapZaiGlmModel, but the
        // default is glm-5.1 — the Coding Plan flagship — because subscribers paid specifically
        // for premium model access. Basic-tier users whose package doesn't cover GLM-5.1 see a
        // clear QuotaExceeded hint that points them at GLM-4.5-Air.
        private string MapZaiCodingModel(string? modelEnum)
        {
            return modelEnum switch
            {
                "GLM_5_1" => "glm-5.1",
                "GLM_5" => "glm-5",
                "GLM_5_Turbo" => "glm-5-turbo",
                "GLM_4_7" => "glm-4.7",
                "GLM_4_7_Flash" => "glm-4.7-flash",
                "GLM_4_7_FlashX" => "glm-4.7-flashx",
                "GLM_4_6" => "glm-4.6",
                "GLM_4_5" => "glm-4.5",
                "GLM_4_5_Air" => "glm-4.5-air",
                "GLM_4_5_AirX" => "glm-4.5-airx",
                "GLM_4_5_Flash" => "glm-4.5-flash",
                "GLM_4_Plus" => "glm-4-plus",
                "GLM_4_32B" => "glm-4-32b-0414-128k",
                _ => "glm-5.1"
            };
        }

        #endregion
    }

    /// <summary>
    /// Extension methods for provider registry.
    /// </summary>
    public static class ProviderRegistryExtensions
    {
        /// <summary>
        /// Registers a provider with automatic parameter extraction.
        /// </summary>
        public static void RegisterAuto<TProvider>(this IProviderRegistry registry, AIProvider type)
            where TProvider : IAIProvider
        {
            // This could use reflection to auto-wire providers
            // For now, manual registration is clearer
        }

        /// <summary>
        /// Tries to create a provider, returning null on failure.
        /// </summary>
        public static IAIProvider TryCreateProvider(this IProviderRegistry registry,
            AIProvider type, BrainarrSettings settings, IHttpClient httpClient, Logger logger)
        {
            try
            {
                return registry.CreateProvider(type, settings, httpClient, logger);
            }
            catch
            {
                return null;
            }
        }
    }
}
