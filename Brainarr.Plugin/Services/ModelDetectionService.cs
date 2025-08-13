using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using Newtonsoft.Json.Linq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public class ModelDetectionService
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public ModelDetectionService(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<List<string>> GetOllamaModelsAsync(string baseUrl)
        {
            try
            {
                // Validate URL to prevent SSRF attacks
                if (!IsValidLocalUrl(baseUrl))
                {
                    _logger.Warn($"Invalid or non-local URL provided: {baseUrl}");
                    return GetDefaultOllamaModels();
                }
                
                var url = baseUrl.TrimEnd('/') + "/api/tags";
                var request = new HttpRequestBuilder(url).Build();
                request.WithModelDetectionTimeout();
                var response = await _httpClient.ExecuteAsync(request);
                
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    var json = JObject.Parse(response.Content);
                    var models = new List<string>();
                    
                    if (json["models"] is JArray modelsArray)
                    {
                        foreach (var model in modelsArray)
                        {
                            var modelName = model["name"]?.ToString();
                            if (!string.IsNullOrEmpty(modelName))
                            {
                                // Popular models for music recommendations
                                if (IsGoodForRecommendations(modelName))
                                {
                                    models.Add(modelName);
                                }
                            }
                        }
                    }
                    
                    _logger.Info($"Found {models.Count} Ollama models: {string.Join(", ", models)}");
                    return models.Any() ? models : GetDefaultOllamaModels();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to auto-detect Ollama models: {ex.Message}");
            }
            
            return GetDefaultOllamaModels();
        }

        public async Task<List<string>> GetLMStudioModelsAsync(string baseUrl)
        {
            try
            {
                // Validate URL to prevent SSRF attacks
                if (!IsValidLocalUrl(baseUrl))
                {
                    _logger.Warn($"Invalid or non-local URL provided: {baseUrl}");
                    return GetDefaultLMStudioModels();
                }
                
                var url = baseUrl.TrimEnd('/') + "/v1/models";
                var request = new HttpRequestBuilder(url).Build();
                request.WithModelDetectionTimeout();
                var response = await _httpClient.ExecuteAsync(request);
                
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    var json = JObject.Parse(response.Content);
                    var models = new List<string>();
                    
                    if (json["data"] is JArray dataArray)
                    {
                        foreach (var model in dataArray)
                        {
                            var modelId = model["id"]?.ToString();
                            if (!string.IsNullOrEmpty(modelId))
                            {
                                models.Add(modelId);
                            }
                        }
                    }
                    
                    _logger.Info($"Found {models.Count} LM Studio models");
                    return models.Any() ? models : GetDefaultLMStudioModels();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to auto-detect LM Studio models: {ex.Message}");
            }
            
            return GetDefaultLMStudioModels();
        }

        private bool IsGoodForRecommendations(string modelName)
        {
            // Models that work well for recommendations
            var goodModels = new[] 
            { 
                "qwen", "llama", "mistral", "mixtral", "phi", 
                "neural", "vicuna", "wizard", "openhermes", "dolphin",
                "yi", "deepseek", "gemma", "stablelm"
            };
            
            var lowerName = modelName.ToLower();
            return goodModels.Any(m => lowerName.Contains(m));
        }

        private List<string> GetDefaultOllamaModels()
        {
            // Common models that work well for music recommendations
            return new List<string>
            {
                "qwen2.5:latest",
                "qwen2.5:7b",
                "llama3.2:latest",
                "llama3.2:3b",
                "mistral:latest",
                "phi3:latest",
                "gemma2:2b"
            };
        }

        private List<string> GetDefaultLMStudioModels()
        {
            return new List<string>
            {
                "local-model",
                "TheBloke/Mistral-7B-Instruct-v0.2-GGUF",
                "TheBloke/Llama-2-7B-Chat-GGUF"
            };
        }

        public string GetRecommendedModel(List<string> availableModels, int librarySize)
        {
            if (!availableModels.Any())
                return "qwen2.5:latest";

            // For large libraries, prefer smaller/faster models
            if (librarySize > 1000)
            {
                var fastModels = new[] { "qwen2.5:3b", "llama3.2:3b", "phi3", "gemma2:2b" };
                var fast = availableModels.FirstOrDefault(m => 
                    fastModels.Any(f => m.Contains(f, StringComparison.OrdinalIgnoreCase)));
                if (fast != null) return fast;
            }

            // For smaller libraries, we can use larger models for better quality
            var qualityModels = new[] { "qwen2.5:7b", "llama3.2:7b", "mistral:7b", "mixtral" };
            var quality = availableModels.FirstOrDefault(m => 
                qualityModels.Any(q => m.Contains(q, StringComparison.OrdinalIgnoreCase)));
            if (quality != null) return quality;

            // Default to first available
            return availableModels.First();
        }
        
        private bool IsValidLocalUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;
                
            try
            {
                var uri = new Uri(url);
                
                // Only allow HTTP/HTTPS schemes
                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                    return false;
                
                // Check for local addresses only (security: prevent SSRF)
                var host = uri.Host.ToLower();
                
                // Allow localhost, 127.0.0.1, ::1, and private IP ranges
                if (host == "localhost" || host == "127.0.0.1" || host == "::1")
                    return true;
                    
                // Check for private IP ranges (10.x.x.x, 172.16-31.x.x, 192.168.x.x)
                if (System.Net.IPAddress.TryParse(host, out var ipAddress))
                {
                    var bytes = ipAddress.GetAddressBytes();
                    if (bytes.Length == 4) // IPv4
                    {
                        // 10.0.0.0/8
                        if (bytes[0] == 10)
                            return true;
                        // 172.16.0.0/12
                        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                            return true;
                        // 192.168.0.0/16
                        if (bytes[0] == 192 && bytes[1] == 168)
                            return true;
                    }
                }
                
                // Reject all other addresses
                return false;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Invalid URL format: {ex.Message}");
                return false;
            }
        }
    }
}