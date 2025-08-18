using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Defines the contract for AI providers that generate music recommendations.
    /// </summary>
    public interface IAIProvider
    {
        /// <summary>
        /// Gets music recommendations based on the provided prompt.
        /// </summary>
        /// <param name="prompt">The prompt describing the user's music library and preferences.</param>
        /// <returns>A list of recommended albums with metadata.</returns>
        Task<List<Recommendation>> GetRecommendationsAsync(string prompt);
        
        /// <summary>
        /// Tests the connection to the AI provider.
        /// </summary>
        /// <returns>True if the connection is successful; otherwise, false.</returns>
        Task<bool> TestConnectionAsync();
        
        /// <summary>
        /// Gets the display name of the provider.
        /// </summary>
        string ProviderName { get; }
    }

    /// <summary>
    /// Ollama provider implementation for local, privacy-focused music recommendations.
    /// Runs entirely on your local machine with no data sent to external services.
    /// </summary>
    /// <remarks>
    /// Requires Ollama to be installed and running locally (https://ollama.ai).
    /// Supports various open-source models like Llama 3, Mistral, Phi, and more.
    /// Perfect for users prioritizing data privacy and offline operation.
    /// Default URL: http://localhost:11434
    /// </remarks>
    public class OllamaProvider : IAIProvider
    {
        private readonly string _baseUrl;
        private readonly string _model;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        /// <summary>
        /// Gets the display name of this provider.
        /// </summary>
        public string ProviderName => "Ollama";

        public OllamaProvider(string baseUrl, string model, IHttpClient httpClient, Logger logger)
        {
            _baseUrl = baseUrl?.TrimEnd('/') ?? BrainarrConstants.DefaultOllamaUrl;
            _model = model ?? BrainarrConstants.DefaultOllamaModel;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger;
        }

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            try
            {
                var request = new HttpRequestBuilder($"{_baseUrl}/api/generate")
                    .Accept(HttpAccept.Json)
                    .SetHeader("Content-Type", "application/json")
                    .Post()
                    .Build();
                
                // Set timeout for AI request
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.DefaultAITimeout);

                var payload = new
                {
                    model = _model,
                    prompt = prompt,
                    stream = false,
                    options = new
                    {
                        temperature = 0.7,
                        top_p = 0.9,
                        max_tokens = 2000
                    }
                };

                request.SetContent(JsonConvert.SerializeObject(payload));
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.MaxAITimeout);

                var response = await _httpClient.ExecuteAsync(request);
                
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    var json = JObject.Parse(response.Content);
                    if (json["response"] != null)
                    {
                        return ParseRecommendations(json["response"].ToString());
                    }
                }
                
                _logger.Error($"Failed to get recommendations from Ollama: {response.StatusCode}");
                return new List<Recommendation>();
            }
            catch (HttpRequestException ex)
            {
                _logger.Error(ex, $"HTTP error calling Ollama at {_baseUrl}: {ex.Message}");
                return new List<Recommendation>();
            }
            catch (TaskCanceledException ex)
            {
                _logger.Error(ex, $"Request to Ollama timed out after {BrainarrConstants.MaxAITimeout} seconds");
                return new List<Recommendation>();
            }
            catch (JsonException ex)
            {
                _logger.Error(ex, "Failed to parse Ollama response as JSON");
                return new List<Recommendation>();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unexpected error calling Ollama");
                return new List<Recommendation>();
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var request = new HttpRequestBuilder($"{_baseUrl}/api/tags").Build();
                var response = await _httpClient.ExecuteAsync(request);
                return response.StatusCode == System.Net.HttpStatusCode.OK;
            }
            catch
            {
                return false;
            }
        }

        protected List<Recommendation> ParseRecommendations(string response)
        {
            var recommendations = new List<Recommendation>();
            
            try
            {
                // Try to parse as JSON first
                if (response.Contains("[") && response.Contains("]"))
                {
                    var startIndex = response.IndexOf('[');
                    var endIndex = response.LastIndexOf(']') + 1;
                    if (startIndex >= 0 && endIndex > startIndex)
                    {
                        var jsonStr = response.Substring(startIndex, endIndex - startIndex);
                        var items = JsonConvert.DeserializeObject<JArray>(jsonStr);
                        
                        foreach (var item in items)
                        {
                            recommendations.Add(new Recommendation
                            {
                                Artist = item["artist"]?.ToString() ?? "Unknown",
                                Album = item["album"]?.ToString() ?? "Unknown",
                                Genre = item["genre"]?.ToString() ?? "Unknown",
                                Confidence = item["confidence"]?.Value<double>() ?? 0.7,
                                Reason = item["reason"]?.ToString() ?? ""
                            });
                        }
                    }
                }
                else
                {
                    // Fallback to simple text parsing
                    var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.Contains('-') || line.Contains('–'))
                        {
                            var parts = line.Split(new[] { '-', '–' }, 2);
                            if (parts.Length == 2)
                            {
                                recommendations.Add(new Recommendation
                                {
                                    Artist = parts[0].Trim().TrimStart('•', '*', '1', '2', '3', '4', '5', '6', '7', '8', '9', '0', '.', ' '),
                                    Album = parts[1].Trim(),
                                    Confidence = 0.7,
                                    Genre = "Unknown"
                                });
                            }
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.Warn($"Failed to parse recommendations as JSON, using fallback parser: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.Warn($"Unexpected error parsing recommendations, using fallback: {ex.Message}");
            }
            
            return recommendations;
        }
    }

    /// <summary>
    /// LM Studio provider implementation for local music recommendations with GUI interface.
    /// Provides an easy-to-use desktop application for running local AI models.
    /// </summary>
    /// <remarks>
    /// Requires LM Studio to be installed and running (https://lmstudio.ai).
    /// Supports a wide variety of models from Hugging Face with automatic downloading.
    /// Offers a user-friendly GUI for model management and configuration.
    /// Default URL: http://localhost:1234
    /// </remarks>
    public class LMStudioProvider : IAIProvider
    {
        private readonly string _baseUrl;
        private readonly string _model;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        /// <summary>
        /// Gets the display name of this provider.
        /// </summary>
        public string ProviderName => "LM Studio";

        public LMStudioProvider(string baseUrl, string model, IHttpClient httpClient, Logger logger)
        {
            _baseUrl = baseUrl?.TrimEnd('/') ?? BrainarrConstants.DefaultLMStudioUrl;
            _model = model ?? BrainarrConstants.DefaultLMStudioModel;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger;
            
            _logger.Info($"LMStudioProvider initialized: URL={_baseUrl}, Model={_model}");
        }

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            try
            {
                var request = new HttpRequestBuilder($"{_baseUrl}/v1/chat/completions")
                    .Accept(HttpAccept.Json)
                    .SetHeader("Content-Type", "application/json")
                    .Post()
                    .Build();
                
                // Set timeout for AI request
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.DefaultAITimeout);

                var payload = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "system", content = "You are a music recommendation expert. Always respond with valid JSON array." },
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.7,
                    max_tokens = 2000,
                    stream = false
                };

                request.SetContent(JsonConvert.SerializeObject(payload));
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.MaxAITimeout);

                var response = await _httpClient.ExecuteAsync(request);
                
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    _logger.Info($"LM Studio response received, content length: {response.Content?.Length ?? 0}");
                    _logger.Info($"LM Studio raw response: {response.Content}");
                    
                    var json = JObject.Parse(response.Content);
                    _logger.Info($"LM Studio parsed JSON keys: {string.Join(", ", json.Properties().Select(p => p.Name))}");
                    
                    if (json["choices"] is JArray choices && choices.Count > 0)
                    {
                        var firstChoice = choices[0] as JObject;
                        if (firstChoice?["message"]?["content"] != null)
                        {
                            var content = firstChoice["message"]["content"].ToString();
                            _logger.Info($"LM Studio content extracted, length: {content.Length}");
                            _logger.Debug($"LM Studio content: {content}");
                            
                            var recommendations = ParseRecommendations(content);
                            _logger.Info($"Parsed {recommendations.Count} recommendations from LM Studio");
                            return recommendations;
                        }
                        else
                        {
                            _logger.Warn("LM Studio response missing message content");
                        }
                    }
                    else
                    {
                        _logger.Warn("LM Studio response missing choices array or empty");
                    }
                }
                
                _logger.Error($"Failed to get recommendations from LM Studio: {response.StatusCode}");
                return new List<Recommendation>();
            }
            catch (HttpRequestException ex)
            {
                _logger.Error(ex, $"HTTP error calling LM Studio at {_baseUrl}: {ex.Message}");
                return new List<Recommendation>();
            }
            catch (TaskCanceledException ex)
            {
                _logger.Error(ex, $"Request to LM Studio timed out after {BrainarrConstants.MaxAITimeout} seconds");
                return new List<Recommendation>();
            }
            catch (JsonException ex)
            {
                _logger.Error(ex, "Failed to parse LM Studio response as JSON");
                return new List<Recommendation>();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unexpected error calling LM Studio");
                return new List<Recommendation>();
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var request = new HttpRequestBuilder($"{_baseUrl}/v1/models").Build();
                var response = await _httpClient.ExecuteAsync(request);
                return response.StatusCode == System.Net.HttpStatusCode.OK;
            }
            catch
            {
                return false;
            }
        }

        protected List<Recommendation> ParseRecommendations(string response)
        {
            var recommendations = new List<Recommendation>();
            
            try
            {
                _logger.Info($"[LM Studio] Parsing recommendations from response: {response?.Substring(0, Math.Min(200, response?.Length ?? 0))}...");
                
                // Try to parse as JSON first
                if (response.Contains("[") && response.Contains("]"))
                {
                    var startIndex = response.IndexOf('[');
                    var endIndex = response.LastIndexOf(']') + 1;
                    if (startIndex >= 0 && endIndex > startIndex)
                    {
                        var jsonStr = response.Substring(startIndex, endIndex - startIndex);
                        _logger.Info($"[LM Studio] Extracted JSON: {jsonStr.Substring(0, Math.Min(300, jsonStr.Length))}...");
                        
                        var items = JsonConvert.DeserializeObject<JArray>(jsonStr);
                        _logger.Info($"[LM Studio] Deserialized {items.Count} items from JSON");
                        
                        foreach (var item in items)
                        {
                            var rec = new Recommendation
                            {
                                Artist = item["artist"]?.ToString() ?? "Unknown",
                                Album = item["album"]?.ToString() ?? "Unknown",
                                Genre = item["genre"]?.ToString() ?? "Unknown",
                                Confidence = item["confidence"]?.Value<double>() ?? 0.7,
                                Reason = item["reason"]?.ToString() ?? ""
                            };
                            
                            _logger.Debug($"[LM Studio] Parsed recommendation: {rec.Artist} - {rec.Album}");
                            recommendations.Add(rec);
                        }
                    }
                }
                else
                {
                    _logger.Info("[LM Studio] No JSON array found, trying text parsing");
                    // Fallback to simple text parsing
                    var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.Contains('-') || line.Contains('–'))
                        {
                            var parts = line.Split(new[] { '-', '–' }, 2);
                            if (parts.Length == 2)
                            {
                                var rec = new Recommendation
                                {
                                    Artist = parts[0].Trim().Trim('"', '\'', '*', '•', '1', '2', '3', '4', '5', '6', '7', '8', '9', '0', '.'),
                                    Album = parts[1].Trim().Trim('"', '\''),
                                    Genre = "Unknown",
                                    Confidence = 0.7,
                                    Reason = ""
                                };
                                
                                _logger.Debug($"[LM Studio] Text parsed recommendation: {rec.Artist} - {rec.Album}");
                                recommendations.Add(rec);
                            }
                        }
                    }
                }
                
                _logger.Info($"[LM Studio] Final recommendation count: {recommendations.Count}");
            }
            catch (JsonException ex)
            {
                _logger.Error(ex, "[LM Studio] Failed to parse recommendations as JSON");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[LM Studio] Unexpected error parsing recommendations");
            }
            
            return recommendations;
        }
    }

    /// <summary>
    /// Represents a music recommendation with metadata.
    /// </summary>
    public class Recommendation
    {
        /// <summary>
        /// Gets or sets the artist name.
        /// </summary>
        public string Artist { get; set; }
        
        /// <summary>
        /// Gets or sets the album title.
        /// </summary>
        public string Album { get; set; }
        
        /// <summary>
        /// Gets or sets the music genre.
        /// </summary>
        public string Genre { get; set; }
        
        /// <summary>
        /// Gets or sets the confidence score (0.0 to 1.0).
        /// Higher values indicate stronger recommendation confidence.
        /// </summary>
        public double Confidence { get; set; }
        
        /// <summary>
        /// Gets or sets the reasoning behind this recommendation.
        /// Explains why this album was suggested based on the user's library.
        /// </summary>
        public string Reason { get; set; }
    }
}