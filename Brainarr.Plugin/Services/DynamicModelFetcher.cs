using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public interface IDynamicModelFetcher
    {
        Task<List<ModelInfo>> FetchAvailableModelsAsync(AIProvider provider, string apiKeyOrUrl);
        List<ModelInfo> GetCachedModels(AIProvider provider);
        void ClearCache();
    }

    public class DynamicModelFetcher : IDynamicModelFetcher
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly Dictionary<AIProvider, List<ModelInfo>> _modelCache;
        private readonly Dictionary<AIProvider, DateTime> _cacheExpiry;
        private readonly TimeSpan _cacheLifetime = TimeSpan.FromHours(24);

        public DynamicModelFetcher(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _modelCache = new Dictionary<AIProvider, List<ModelInfo>>();
            _cacheExpiry = new Dictionary<AIProvider, DateTime>();
        }

        public async Task<List<ModelInfo>> FetchAvailableModelsAsync(AIProvider provider, string apiKeyOrUrl)
        {
            try
            {
                // Check cache first
                if (_modelCache.ContainsKey(provider) && 
                    _cacheExpiry.ContainsKey(provider) && 
                    _cacheExpiry[provider] > DateTime.UtcNow)
                {
                    return _modelCache[provider];
                }

                List<ModelInfo> models = provider switch
                {
                    AIProvider.Ollama => await FetchOllamaModels(apiKeyOrUrl),
                    AIProvider.LMStudio => await FetchLMStudioModels(apiKeyOrUrl),
                    AIProvider.OpenRouter => await FetchOpenRouterModels(apiKeyOrUrl),
                    AIProvider.Perplexity => await FetchPerplexityModels(apiKeyOrUrl),
                    AIProvider.OpenAI => await FetchOpenAIModels(apiKeyOrUrl),
                    AIProvider.Anthropic => GetAnthropicModels(), // Static list
                    AIProvider.Gemini => GetGeminiModels(), // Static list
                    AIProvider.Groq => await FetchGroqModels(apiKeyOrUrl),
                    AIProvider.DeepSeek => GetDeepSeekModels(), // Static list
                    _ => new List<ModelInfo>()
                };

                // Cache the results
                if (models.Any())
                {
                    _modelCache[provider] = models;
                    _cacheExpiry[provider] = DateTime.UtcNow.Add(_cacheLifetime);
                }

                return models;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to fetch models for {provider}");
                return GetFallbackModels(provider);
            }
        }

        private async Task<List<ModelInfo>> FetchOllamaModels(string baseUrl)
        {
            try
            {
                var url = $"{baseUrl}/api/tags";
                var request = new HttpRequestBuilder(url).Build();
                var response = await _httpClient.ExecuteAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    var data = JObject.Parse(response.Content);
                    var models = data["models"]?.ToObject<List<OllamaModel>>() ?? new List<OllamaModel>();
                    
                    return models.Select(m => new ModelInfo
                    {
                        Id = m.Name,
                        Name = m.Name,
                        Description = $"{m.Name} ({FormatSize(m.Size)})",
                        Context = m.Details?.ParameterSize ?? "Unknown"
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to fetch Ollama models");
            }

            return GetFallbackModels(AIProvider.Ollama);
        }

        private async Task<List<ModelInfo>> FetchLMStudioModels(string baseUrl)
        {
            try
            {
                var url = $"{baseUrl}/v1/models";
                var request = new HttpRequestBuilder(url).Build();
                var response = await _httpClient.ExecuteAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    var data = JObject.Parse(response.Content);
                    var models = data["data"]?.ToObject<List<LMStudioModel>>() ?? new List<LMStudioModel>();
                    
                    return models.Select(m => new ModelInfo
                    {
                        Id = m.Id,
                        Name = m.Id,
                        Description = m.Id,
                        Context = "Variable"
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to fetch LM Studio models");
            }

            return GetFallbackModels(AIProvider.LMStudio);
        }

        private async Task<List<ModelInfo>> FetchOpenRouterModels(string apiKey)
        {
            try
            {
                var url = "https://openrouter.ai/api/v1/models";
                var request = new HttpRequestBuilder(url)
                    .SetHeader("Authorization", $"Bearer {apiKey}")
                    .Build();
                
                var response = await _httpClient.ExecuteAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    var data = JObject.Parse(response.Content);
                    var models = data["data"]?.ToObject<List<OpenRouterModel>>() ?? new List<OpenRouterModel>();
                    
                    // Filter for recommended models
                    var recommendedModels = models
                        .Where(m => m.TopProvider?.MaxCompletionTokens > 1000)
                        .OrderBy(m => m.Pricing?.Completion ?? 999)
                        .Take(20)
                        .Select(m => new ModelInfo
                        {
                            Id = m.Id,
                            Name = m.Name ?? m.Id,
                            Description = $"{m.Name} - ${(m.Pricing?.Completion ?? 0) * 1000000:F2}/M tokens",
                            Context = $"{m.ContextLength ?? 0} tokens"
                        }).ToList();

                    return recommendedModels.Any() ? recommendedModels : GetFallbackModels(AIProvider.OpenRouter);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to fetch OpenRouter models");
            }

            return GetFallbackModels(AIProvider.OpenRouter);
        }

        private async Task<List<ModelInfo>> FetchPerplexityModels(string apiKey)
        {
            // Perplexity doesn't have a models endpoint, so we return a curated list
            await Task.CompletedTask; // Satisfy async signature
            return new List<ModelInfo>
            {
                // Sonar models (with online search)
                new ModelInfo { Id = "llama-3.1-sonar-large-128k-online", Name = "Sonar Large (Online)", Description = "Best for music discovery with web search", Context = "128k" },
                new ModelInfo { Id = "llama-3.1-sonar-small-128k-online", Name = "Sonar Small (Online)", Description = "Faster with web search", Context = "128k" },
                new ModelInfo { Id = "llama-3.1-sonar-huge-128k-online", Name = "Sonar Huge (Online)", Description = "Most powerful with web search", Context = "128k" },
                
                // Chat models
                new ModelInfo { Id = "llama-3.1-70b-instruct", Name = "Llama 3.1 70B", Description = "Large language model", Context = "128k" },
                new ModelInfo { Id = "llama-3.1-8b-instruct", Name = "Llama 3.1 8B", Description = "Smaller, faster model", Context = "128k" },
                
                // Premium models available through Perplexity
                new ModelInfo { Id = "claude-3.5-sonnet", Name = "Claude 3.5 Sonnet", Description = "Anthropic's balanced model", Context = "200k" },
                new ModelInfo { Id = "claude-3.5-haiku", Name = "Claude 3.5 Haiku", Description = "Anthropic's fast model", Context = "200k" },
                new ModelInfo { Id = "gpt-4o-mini", Name = "GPT-4o Mini", Description = "OpenAI's efficient model", Context = "128k" },
                new ModelInfo { Id = "gemini-1.5-pro-latest", Name = "Gemini 1.5 Pro", Description = "Google's advanced model", Context = "2M" },
                new ModelInfo { Id = "gemini-1.5-flash-latest", Name = "Gemini 1.5 Flash", Description = "Google's fast model", Context = "1M" },
                new ModelInfo { Id = "mistral-large-latest", Name = "Mistral Large", Description = "Mistral's best model", Context = "128k" }
            };
        }

        private async Task<List<ModelInfo>> FetchOpenAIModels(string apiKey)
        {
            try
            {
                var url = "https://api.openai.com/v1/models";
                var request = new HttpRequestBuilder(url)
                    .SetHeader("Authorization", $"Bearer {apiKey}")
                    .Build();
                
                var response = await _httpClient.ExecuteAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    var data = JObject.Parse(response.Content);
                    var models = data["data"]?.ToObject<List<OpenAIModel>>() ?? new List<OpenAIModel>();
                    
                    // Filter for GPT models only
                    var gptModels = models
                        .Where(m => m.Id.StartsWith("gpt"))
                        .OrderByDescending(m => m.Created)
                        .Select(m => new ModelInfo
                        {
                            Id = m.Id,
                            Name = m.Id,
                            Description = GetOpenAIModelDescription(m.Id),
                            Context = GetOpenAIModelContext(m.Id)
                        }).ToList();

                    return gptModels.Any() ? gptModels : GetFallbackModels(AIProvider.OpenAI);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to fetch OpenAI models");
            }

            return GetFallbackModels(AIProvider.OpenAI);
        }

        private async Task<List<ModelInfo>> FetchGroqModels(string apiKey)
        {
            try
            {
                var url = "https://api.groq.com/openai/v1/models";
                var request = new HttpRequestBuilder(url)
                    .SetHeader("Authorization", $"Bearer {apiKey}")
                    .Build();
                
                var response = await _httpClient.ExecuteAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    var data = JObject.Parse(response.Content);
                    var models = data["data"]?.ToObject<List<GroqModel>>() ?? new List<GroqModel>();
                    
                    return models.Select(m => new ModelInfo
                    {
                        Id = m.Id,
                        Name = m.Id,
                        Description = $"{m.Id} - Ultra-fast inference",
                        Context = m.ContextWindow?.ToString() ?? "32k"
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to fetch Groq models");
            }

            return GetFallbackModels(AIProvider.Groq);
        }

        private List<ModelInfo> GetAnthropicModels()
        {
            return new List<ModelInfo>
            {
                new ModelInfo { Id = "claude-3-5-haiku-latest", Name = "Claude 3.5 Haiku", Description = "Fast and cost-effective", Context = "200k" },
                new ModelInfo { Id = "claude-3-5-sonnet-latest", Name = "Claude 3.5 Sonnet", Description = "Best balance of speed and capability", Context = "200k" },
                new ModelInfo { Id = "claude-3-opus-20240229", Name = "Claude 3 Opus", Description = "Most capable model", Context = "200k" }
            };
        }

        private List<ModelInfo> GetGeminiModels()
        {
            return new List<ModelInfo>
            {
                new ModelInfo { Id = "gemini-1.5-flash", Name = "Gemini 1.5 Flash", Description = "Fast with 1M context", Context = "1M" },
                new ModelInfo { Id = "gemini-1.5-flash-8b", Name = "Gemini 1.5 Flash 8B", Description = "Smaller and faster", Context = "1M" },
                new ModelInfo { Id = "gemini-1.5-pro", Name = "Gemini 1.5 Pro", Description = "Most capable with 2M context", Context = "2M" },
                new ModelInfo { Id = "gemini-2.0-flash-exp", Name = "Gemini 2.0 Flash", Description = "Latest experimental model", Context = "1M" }
            };
        }

        private List<ModelInfo> GetDeepSeekModels()
        {
            return new List<ModelInfo>
            {
                new ModelInfo { Id = "deepseek-chat", Name = "DeepSeek Chat V3", Description = "Latest V3 model, best overall", Context = "128k" },
                new ModelInfo { Id = "deepseek-coder", Name = "DeepSeek Coder", Description = "Optimized for code generation", Context = "128k" },
                new ModelInfo { Id = "deepseek-reasoner", Name = "DeepSeek Reasoner R1", Description = "Advanced reasoning model", Context = "128k" }
            };
        }

        private List<ModelInfo> GetFallbackModels(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.Ollama => new List<ModelInfo>
                {
                    new ModelInfo { Id = "llama3.2", Name = "Llama 3.2", Description = "Latest Llama model" },
                    new ModelInfo { Id = "mistral", Name = "Mistral", Description = "Efficient 7B model" },
                    new ModelInfo { Id = "qwen2.5", Name = "Qwen 2.5", Description = "Alibaba's latest model" }
                },
                AIProvider.LMStudio => new List<ModelInfo>
                {
                    new ModelInfo { Id = "local-model", Name = "Local Model", Description = "Currently loaded model" }
                },
                AIProvider.OpenRouter => new List<ModelInfo>
                {
                    new ModelInfo { Id = "anthropic/claude-3.5-sonnet", Name = "Claude 3.5 Sonnet", Description = "Best overall" },
                    new ModelInfo { Id = "openai/gpt-4o-mini", Name = "GPT-4o Mini", Description = "Cost-effective" },
                    new ModelInfo { Id = "google/gemini-flash-1.5", Name = "Gemini Flash", Description = "Fast" }
                },
                _ => new List<ModelInfo>()
            };
        }

        public List<ModelInfo> GetCachedModels(AIProvider provider)
        {
            return _modelCache.ContainsKey(provider) ? _modelCache[provider] : new List<ModelInfo>();
        }

        public void ClearCache()
        {
            _modelCache.Clear();
            _cacheExpiry.Clear();
        }

        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        private string GetOpenAIModelDescription(string modelId)
        {
            return modelId switch
            {
                var id when id.Contains("gpt-4o-mini") => "Most cost-effective GPT-4 model",
                var id when id.Contains("gpt-4o") => "Latest multimodal GPT-4",
                var id when id.Contains("gpt-4-turbo") => "Previous generation GPT-4",
                var id when id.Contains("gpt-3.5") => "Legacy model, lowest cost",
                _ => "OpenAI model"
            };
        }

        private string GetOpenAIModelContext(string modelId)
        {
            return modelId switch
            {
                var id when id.Contains("gpt-4o") => "128k",
                var id when id.Contains("gpt-4-turbo") => "128k",
                var id when id.Contains("gpt-3.5-turbo-16k") => "16k",
                var id when id.Contains("gpt-3.5") => "4k",
                _ => "Variable"
            };
        }
    }

    public class ModelInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Context { get; set; }
    }

    // Model response classes
    public class OllamaModel
    {
        public string Name { get; set; }
        public long Size { get; set; }
        public OllamaModelDetails Details { get; set; }
    }

    public class OllamaModelDetails
    {
        [JsonProperty("parameter_size")]
        public string ParameterSize { get; set; }
    }

    public class LMStudioModel
    {
        public string Id { get; set; }
        public string Object { get; set; }
    }

    public class OpenRouterModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public OpenRouterPricing Pricing { get; set; }
        [JsonProperty("context_length")]
        public int? ContextLength { get; set; }
        [JsonProperty("top_provider")]
        public OpenRouterTopProvider TopProvider { get; set; }
    }

    public class OpenRouterPricing
    {
        public decimal? Prompt { get; set; }
        public decimal? Completion { get; set; }
    }

    public class OpenRouterTopProvider
    {
        [JsonProperty("max_completion_tokens")]
        public int? MaxCompletionTokens { get; set; }
    }

    public class OpenAIModel
    {
        public string Id { get; set; }
        public long Created { get; set; }
    }

    public class GroqModel
    {
        public string Id { get; set; }
        [JsonProperty("context_window")]
        public int? ContextWindow { get; set; }
    }
}