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

    public class OllamaProvider : IAIProvider
    {
        private readonly string _baseUrl;
        private readonly string _model;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

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
                    // Strip BOM if present
                    var content = response.Content?.TrimStart('\ufeff') ?? "";
                    var json = JObject.Parse(content);
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
                        var parsedJson = JsonConvert.DeserializeObject<JToken>(jsonStr);
                        
                        // Handle nested arrays - flatten if needed
                        JArray items;
                        if (parsedJson is JArray directArray)
                        {
                            // Check if it's a nested array [[...]]
                            if (directArray.Count == 1 && directArray[0] is JArray nestedArray)
                            {
                                items = nestedArray;
                            }
                            else
                            {
                                items = directArray;
                            }
                        }
                        else
                        {
                            // Single object, wrap in array
                            items = new JArray { parsedJson };
                        }
                        
                        foreach (var item in items)
                        {
                            // Handle case-insensitive field names and default to "Unknown" for unrecognized fields
                            var artistField = GetFieldValue(item, "artist");
                            var albumField = GetFieldValue(item, "album");
                            var genreField = GetFieldValue(item, "genre");
                            var reasonField = GetFieldValue(item, "reason");
                            
                            // Handle confidence with proper validation
                            double confidence = 0.7; // Default value
                            if (item["confidence"] != null)
                            {
                                try
                                {
                                    confidence = item["confidence"].Value<double>();
                                    if (double.IsNaN(confidence) || double.IsInfinity(confidence))
                                    {
                                        confidence = 0.7;
                                    }
                                    else if (confidence < 0)
                                    {
                                        confidence = 0.0; // Clamp negative values to 0
                                    }
                                }
                                catch
                                {
                                    confidence = 0.7;
                                }
                            }
                            
                            // Add recommendation if we have meaningful data OR if the JSON object has any fields
                            // This handles cases where field names are unrecognized but the object isn't completely empty
                            bool hasAnyData = !string.IsNullOrWhiteSpace(artistField) || !string.IsNullOrWhiteSpace(albumField);
                            bool hasAnyFields = (item as JObject)?.Properties().Any() == true;
                            
                            if (hasAnyData || hasAnyFields)
                            {
                                recommendations.Add(new Recommendation
                                {
                                    Artist = artistField ?? "Unknown",
                                    Album = albumField ?? "Unknown", 
                                    Genre = genreField ?? "Unknown",
                                    Confidence = confidence,
                                    Reason = reasonField ?? ""
                                });
                            }
                        }
                    }
                }
                else
                {
                    // Fallback to simple text parsing - handle different dash types and list formatting
                    var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        // Check for various dash types including em dash and en dash
                        if (line.Contains('-') || line.Contains('–') || line.Contains('—'))
                        {
                            // Split on any of the dash types
                            var parts = line.Split(new[] { '-', '–', '—' }, 2);
                            if (parts.Length == 2)
                            {
                                var artistPart = parts[0].Trim();
                                var albumPart = parts[1].Trim();
                                
                                // Remove common list prefixes (numbers, bullets, asterisks)
                                artistPart = System.Text.RegularExpressions.Regex.Replace(artistPart, @"^[\d]+\.?\s*", "");
                                artistPart = artistPart.TrimStart('•', '*', ' ').Trim();
                                
                                // Add all text recommendations - don't filter here
                                recommendations.Add(new Recommendation
                                {
                                    Artist = string.IsNullOrWhiteSpace(artistPart) ? "Unknown" : artistPart,
                                    Album = string.IsNullOrWhiteSpace(albumPart) ? "Unknown" : albumPart,
                                    Confidence = 0.7,
                                    Genre = "Unknown",
                                    Reason = ""
                                });
                            }
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.Warn($"Failed to parse recommendations as JSON, using fallback parser: {ex.Message}");
                // Try text fallback on JSON failure
                return ParseTextFallback(response);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Unexpected error parsing recommendations, using fallback: {ex.Message}");
                return ParseTextFallback(response);
            }
            
            return recommendations;
        }
        
        private string GetFieldValue(JToken item, string fieldName)
        {
            var jObject = item as JObject;
            if (jObject == null) return null;
            
            // Try exact match first, then case-insensitive match
            var property = jObject.Property(fieldName) ?? 
                          jObject.Properties().FirstOrDefault(p => 
                              string.Equals(p.Name, fieldName, StringComparison.OrdinalIgnoreCase));
            
            return property?.Value?.ToString();
        }
        
        private List<Recommendation> ParseTextFallback(string response)
        {
            var recommendations = new List<Recommendation>();
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                if (line.Contains('-') || line.Contains('–') || line.Contains('—'))
                {
                    var parts = line.Split(new[] { '-', '–', '—' }, 2);
                    if (parts.Length == 2)
                    {
                        var artistPart = parts[0].Trim();
                        var albumPart = parts[1].Trim();
                        
                        // Remove common list prefixes
                        artistPart = System.Text.RegularExpressions.Regex.Replace(artistPart, @"^[\d]+\.?\s*", "");
                        artistPart = artistPart.TrimStart('•', '*', ' ').Trim();
                        
                        recommendations.Add(new Recommendation
                        {
                            Artist = artistPart,
                            Album = albumPart,
                            Confidence = 0.7,
                            Genre = "Unknown",
                            Reason = ""
                        });
                    }
                }
            }
            
            return recommendations;
        }
    }

    public class LMStudioProvider : IAIProvider
    {
        private readonly string _baseUrl;
        private readonly string _model;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

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
                    // Strip BOM if present
                    var content = response.Content?.TrimStart('\ufeff') ?? "";
                    
                    _logger.Info($"LM Studio response received, content length: {content.Length}");
                    _logger.Info($"LM Studio raw response: {content}");
                    
                    var json = JObject.Parse(content);
                    _logger.Info($"LM Studio parsed JSON keys: {string.Join(", ", json.Properties().Select(p => p.Name))}");
                    
                    if (json["choices"] is JArray choices && choices.Count > 0)
                    {
                        var firstChoice = choices[0] as JObject;
                        if (firstChoice?["message"]?["content"] != null)
                        {
                            var messageContent = firstChoice["message"]["content"].ToString();
                            _logger.Info($"LM Studio content extracted, length: {messageContent.Length}");
                            _logger.Debug($"LM Studio content: {messageContent}");
                            
                            var recommendations = ParseRecommendations(messageContent);
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

    public class Recommendation
    {
        public string Artist { get; set; }
        public string Album { get; set; }
        public string Genre { get; set; }
        public double Confidence { get; set; }
        public string Reason { get; set; }
    }
}