using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Utils;
using Brainarr.Plugin.Services.Security;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Concrete implementation of IModelActionHandler that manages model-specific operations
    /// for AI providers including connection testing, model detection, and provider actions.
    /// Handles both local providers (Ollama/LM Studio) and cloud providers with appropriate strategies.
    /// </summary>
    /// <remarks>
    /// This class is responsible for:
    /// - Testing connectivity to AI providers
    /// - Auto-detecting available models on local providers
    /// - Managing model selection and caching
    /// - Providing fallback options when detection fails
    /// - Handling UI interactions for model management
    /// </remarks>
    public class ModelActionHandler : IModelActionHandler
    {
        private readonly IModelDetectionService _modelDetection;
        private readonly IProviderFactory _providerFactory;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        /// <summary>
        /// Initializes a new instance of the ModelActionHandler.
        /// </summary>
        /// <param name="modelDetection">Service for detecting models on local AI providers</param>
        /// <param name="providerFactory">Factory for creating provider instances</param>
        /// <param name="httpClient">HTTP client for provider communications</param>
        /// <param name="logger">Logger instance for debugging and monitoring</param>
        public ModelActionHandler(
            IModelDetectionService modelDetection,
            IProviderFactory providerFactory,
            IHttpClient httpClient,
            Logger logger)
        {
            _modelDetection = modelDetection;
            _providerFactory = providerFactory;
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<string> HandleTestConnectionAsync(BrainarrSettings settings)
        {
            try
            {
                var provider = _providerFactory.CreateProvider(settings, _httpClient, _logger);
                if (provider == null)
                {
                    return "Failed: Provider not configured";
                }

                var connected = await provider.TestConnectionAsync();
                if (!connected)
                {
                    var hint = provider.GetLastUserMessage();
                    if (!string.IsNullOrWhiteSpace(hint))
                    {
                        return $"Failed: Cannot connect to {provider.ProviderName}. {hint}";
                    }
                    return $"Failed: Cannot connect to {provider.ProviderName}";
                }

                // Detect models for local providers
                if (settings.Provider == AIProvider.Ollama || settings.Provider == AIProvider.LMStudio)
                {
                    await DetectAndUpdateModels(settings);
                }

                return $"Success: Connected to {provider.ProviderName}";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Test connection failed");
                return $"Failed: {ex.Message}";
            }
        }

        public async Task<TestConnectionResult> HandleTestConnectionDetailsAsync(BrainarrSettings settings)
        {
            try
            {
                var provider = _providerFactory.CreateProvider(settings, _httpClient, _logger);
                if (provider == null)
                {
                    return new TestConnectionResult { Success = false, Provider = "unknown", Message = "Provider not configured" };
                }

                var connected = await provider.TestConnectionAsync();
                var hint = provider.GetLastUserMessage();

                if (connected && (settings.Provider == AIProvider.Ollama || settings.Provider == AIProvider.LMStudio))
                {
                    await DetectAndUpdateModels(settings);
                }

                return new TestConnectionResult
                {
                    Success = connected,
                    Provider = provider.ProviderName,
                    Hint = string.IsNullOrWhiteSpace(hint) ? null : hint,
                    Message = connected ? ("Connected to " + provider.ProviderName) : ("Cannot connect to " + provider.ProviderName),
                    Docs = provider.GetLearnMoreUrl()
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Test connection details failed");
                return new TestConnectionResult { Success = false, Provider = settings.Provider.ToString(), Message = ex.Message };
            }
        }

        public async Task<List<SelectOption>> HandleGetModelsAsync(BrainarrSettings settings)
        {
            _logger.Info($"Getting model options for provider: {settings.Provider}");

            try
            {
                return settings.Provider switch
                {
                    AIProvider.Ollama => await GetOllamaModelOptions(settings),
                    AIProvider.LMStudio => await GetLMStudioModelOptions(settings),
                    AIProvider.Perplexity => GetStaticModelOptions(typeof(PerplexityModelKind)),
                    AIProvider.OpenAI => GetStaticModelOptions(typeof(OpenAIModelKind)),
                    AIProvider.Anthropic => GetStaticModelOptions(typeof(AnthropicModelKind)),
                    AIProvider.OpenRouter => await GetOpenRouterModelOptions(settings),
                    AIProvider.DeepSeek => GetStaticModelOptions(typeof(DeepSeekModelKind)),
                    AIProvider.Gemini => GetStaticModelOptions(typeof(GeminiModelKind)),
                    AIProvider.Groq => GetStaticModelOptions(typeof(GroqModelKind)),
                    _ => new List<SelectOption>()
                };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to get model options for {settings.Provider}");
                return GetFallbackOptions(settings.Provider);
            }
        }

        public async Task<string> HandleAnalyzeLibraryAsync(BrainarrSettings settings)
        {
            // Placeholder for library analysis functionality
            return "Library analysis complete";
        }

        public object HandleProviderAction(string action, BrainarrSettings settings)
        {
            _logger.Info($"Handling provider action: {action}");

            if (action == "providerChanged")
            {
                settings.DetectedModels?.Clear();
                return new { success = true, message = "Provider changed, model cache cleared" };
            }

            if (action == "getModelOptions")
            {
                var models = SafeAsyncHelper.RunSafeSync(() => HandleGetModelsAsync(settings));
                return new { options = models };
            }

            if (string.Equals(action, "testconnection/details", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(action, "testConnectionDetails", StringComparison.OrdinalIgnoreCase))
            {
                var result = SafeAsyncHelper.RunSafeSync(() => HandleTestConnectionDetailsAsync(settings));
                return new { success = result.Success, provider = result.Provider, hint = result.Hint, message = result.Message, docs = result.Docs };
            }



            if (string.Equals(action, "sanitycheck/commands", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(action, "sanityCheckCommands", StringComparison.OrdinalIgnoreCase))
            {
                var cmds = new List<string>();
                switch (settings.Provider)
                {
                    case AIProvider.Ollama:
                    {
                        var url = string.IsNullOrWhiteSpace(settings.OllamaUrl) ? BrainarrConstants.DefaultOllamaUrl : settings.OllamaUrl;
                        cmds.Add($"curl -s {url}/api/tags | jq '.models[0].name'");
                        break;
                    }
                    case AIProvider.LMStudio:
                    {
                        var url = string.IsNullOrWhiteSpace(settings.LMStudioUrl) ? BrainarrConstants.DefaultLMStudioUrl : settings.LMStudioUrl;
                        cmds.Add($"curl -s {url}/v1/models | jq");
                        break;
                    }
                    case AIProvider.OpenAI:
                        cmds.Add(@"curl -s https://api.openai.com/v1/models -H ""Authorization: Bearer YOUR_OPENAI_API_KEY"" | jq '\.data[0]\.id'");
                        break;
                    case AIProvider.Anthropic:
                        cmds.Add(@"curl -s https://api.anthropic.com/v1/models -H ""x-api-key: YOUR_ANTHROPIC_API_KEY"" -H ""anthropic-version: 2023-06-01"" | jq '\.data[0]\.id'");
                        break;
                    case AIProvider.OpenRouter:
                        cmds.Add(@"curl -s https://openrouter.ai/api/v1/models -H ""Authorization: Bearer YOUR_OPENROUTER_API_KEY"" | jq '\.data[0]\.id'");
                        break;
                    case AIProvider.Gemini:
                        cmds.Add(@"curl -s ""https://generativelanguage.googleapis.com/v1beta/models?key=YOUR_GEMINI_API_KEY"" | jq '\.models[0]\.name'");
                        cmds.Add("# If API is disabled for your GCP project, enable it: gcloud services enable generativelanguage.googleapis.com --project YOUR_PROJECT_ID");
                        break;
                    case AIProvider.Groq:
                        cmds.Add(@"curl -s https://api.groq.com/openai/v1/models -H ""Authorization: Bearer YOUR_GROQ_API_KEY"" | jq '\.data[0]\.id'");
                        break;
                    case AIProvider.DeepSeek:
                        cmds.Add(@"curl -s https://api.deepseek.com/v1/models -H ""Authorization: Bearer YOUR_DEEPSEEK_API_KEY"" | jq '\.data[0]\.id'");
                        break;
                    case AIProvider.Perplexity:
                        cmds.Add("# Perplexity uses chat completions; /v1/models may not be exposed. Test a minimal request in Brainarr or refer to docs.");
                        break;
                }

                return new { provider = settings.Provider.ToString(), commands = cmds };
            }


            return new { };
        }

        private async Task DetectAndUpdateModels(BrainarrSettings settings)
        {
            List<string> models = null;

            if (settings.Provider == AIProvider.Ollama)
            {
                models = await _modelDetection.GetOllamaModelsAsync(settings.OllamaUrl);
            }
            else if (settings.Provider == AIProvider.LMStudio)
            {
                models = await _modelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl);
            }

            if (models?.Any() == true)
            {
                settings.DetectedModels = models;
                _logger.Info($"Detected {models.Count} models for {settings.Provider}");

                if (settings.AutoDetectModel)
                {
                    AutoSelectBestModel(settings, models);
                }
            }
        }

        private void AutoSelectBestModel(BrainarrSettings settings, List<string> models)
        {
            var preferredModels = new[] { "qwen", "llama", "mistral", "phi", "gemma" };

            string selectedModel = null;
            foreach (var preferred in preferredModels)
            {
                selectedModel = models.FirstOrDefault(m => m.ToLower().Contains(preferred));
                if (selectedModel != null) break;
            }

            selectedModel = selectedModel ?? models.First();

            if (settings.Provider == AIProvider.Ollama)
            {
                settings.OllamaModel = selectedModel;
                _logger.Info($"Auto-selected Ollama model: {selectedModel}");
            }
            else
            {
                settings.LMStudioModel = selectedModel;
                _logger.Info($"Auto-selected LM Studio model: {selectedModel}");
            }
        }

        private async Task<List<SelectOption>> GetOllamaModelOptions(BrainarrSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.OllamaUrl))
            {
                return GetFallbackOptions(AIProvider.Ollama);
            }

            var models = await _modelDetection.GetOllamaModelsAsync(settings.OllamaUrl);
            if (models.Any())
            {
                return models.Select(m => new SelectOption
                {
                    Value = m,
                    Name = NzbDrone.Core.ImportLists.Brainarr.Utils.ModelNameFormatter.FormatModelName(m)
                }).ToList();
            }

            return GetFallbackOptions(AIProvider.Ollama);
        }

        private async Task<List<SelectOption>> GetLMStudioModelOptions(BrainarrSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.LMStudioUrl))
            {
                return GetFallbackOptions(AIProvider.LMStudio);
            }

            var models = await _modelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl);
            if (models.Any())
            {
                return models.Select(m => new SelectOption
                {
                    Value = m,
                    Name = NzbDrone.Core.ImportLists.Brainarr.Utils.ModelNameFormatter.FormatModelName(m)
                }).ToList();
            }

            return GetFallbackOptions(AIProvider.LMStudio);
        }

        private List<SelectOption> GetStaticModelOptions(Type enumType)
        {
            return Enum.GetValues(enumType)
                .Cast<Enum>()
                .Select(value => new SelectOption
                {
                    Value = value.ToString(),
                    Name = NzbDrone.Core.ImportLists.Brainarr.Utils.ModelNameFormatter.FormatEnumName(value.ToString())
                }).ToList();
        }

        private async Task<List<SelectOption>> GetOpenRouterModelOptions(BrainarrSettings settings)
        {
            try
            {
                var request = new HttpRequestBuilder("https://openrouter.ai/api/v1/models")
                    .SetHeader("Content-Type", "application/json")
                    .SetHeader("HTTP-Referer", "https://github.com/RicherTunes/Brainarr")
                    .SetHeader("X-Title", "Brainarr Model Discovery")
                    .Build();

                request.Method = System.Net.Http.HttpMethod.Get;
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.ModelDetectionTimeout);

                var response = await _httpClient.ExecuteAsync(request);
                if (response == null || response.StatusCode != System.Net.HttpStatusCode.OK || string.IsNullOrWhiteSpace(response.Content))
                {
                    _logger.Warn($"OpenRouter /models query failed: {response?.StatusCode}");
                    return GetStaticModelOptions(typeof(OpenRouterModelKind));
                }

                using var doc = SecureJsonSerializer.ParseDocument(response.Content);
                var root = doc.RootElement;
                if (root.ValueKind != System.Text.Json.JsonValueKind.Object || !root.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    _logger.Warn("Unexpected /models response shape");
                    return GetStaticModelOptions(typeof(OpenRouterModelKind));
                }

                var options = new List<SelectOption>();
                foreach (var model in dataEl.EnumerateArray())
                {
                    if (!model.TryGetProperty("id", out var idEl) || idEl.ValueKind != System.Text.Json.JsonValueKind.String)
                        continue;
                    var id = idEl.GetString();
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    var name = NzbDrone.Core.ImportLists.Brainarr.Utils.ModelNameFormatter.FormatModelName(id);
                    options.Add(new SelectOption { Value = id, Name = name });
                }

                // De-dup and sort for UX
                options = options
                    .GroupBy(o => o.Value)
                    .Select(g => g.First())
                    .OrderBy(o => o.Name)
                    .ToList();

                if (!options.Any())
                {
                    return GetStaticModelOptions(typeof(OpenRouterModelKind));
                }

                _logger.Info($"Loaded {options.Count} OpenRouter models from /models");
                return options;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed fetching OpenRouter models; falling back to static list");
                return GetStaticModelOptions(typeof(OpenRouterModelKind));
            }
        }

        private List<SelectOption> GetFallbackOptions(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.Ollama => new List<SelectOption>
                {
                    new() { Value = "qwen2.5:latest", Name = "Qwen 2.5 (Recommended)" },
                    new() { Value = "qwen2.5:7b", Name = "Qwen 2.5 7B" },
                    new() { Value = "llama3.2:latest", Name = "Llama 3.2" },
                    new() { Value = "mistral:latest", Name = "Mistral" }
                },
                AIProvider.LMStudio => new List<SelectOption>
                {
                    new() { Value = "local-model", Name = "Currently Loaded Model" }
                },
                _ => new List<SelectOption>()
            };
        }

        // Model/enum name formatting consolidated in Utils/ModelNameFormatter
    }
}
