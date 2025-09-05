using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Brainarr.Plugin.Services.Security;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers
{
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
        private string _model;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly IRecommendationValidator _validator;

        /// <summary>
        /// Gets the display name of this provider.
        /// </summary>
        public string ProviderName => "Ollama";

        public OllamaProvider(string baseUrl, string model, IHttpClient httpClient, Logger logger, IRecommendationValidator? validator = null)
        {
            _baseUrl = baseUrl?.TrimEnd('/') ?? BrainarrConstants.DefaultOllamaUrl;
            _model = model ?? BrainarrConstants.DefaultOllamaModel;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger;
            _validator = validator ?? new RecommendationValidator(logger);
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
                var seconds = TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout);
                request.RequestTimeout = TimeSpan.FromSeconds(seconds);

                // Detect if the prompt asks for artist-only recommendations
                var promptLower = (prompt ?? string.Empty).ToLowerInvariant();
                var allowArtistOnly =
                    promptLower.Contains("artist recommendations") ||
                    promptLower.Contains("focus on artists") ||
                    (promptLower.Contains("provide exactly") && promptLower.Contains("artist"));

                // Elevate optional SYSTEM_AVOID marker via a preface appended to the prompt
                string userPrompt = prompt ?? string.Empty;
                int avoidCount = 0;
                try
                {
                    if (!string.IsNullOrWhiteSpace(userPrompt) && userPrompt.StartsWith("[[SYSTEM_AVOID:"))
                    {
                        var endIdx = userPrompt.IndexOf("]]", StringComparison.Ordinal);
                        if (endIdx > 0)
                        {
                            var marker = userPrompt.Substring(0, endIdx + 2);
                            var inner = marker.Substring("[[SYSTEM_AVOID:".Length, marker.Length - "[[SYSTEM_AVOID:".Length - 2);
                            if (!string.IsNullOrWhiteSpace(inner))
                            {
                                var names = inner.Split('|').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                                if (names.Length > 0)
                                {
                                    var preface = "SYSTEM: Do not recommend these entities under any circumstances: " + string.Join(", ", names) + "." + "\n\n";
                                    userPrompt = preface + userPrompt.Substring(endIdx + 2).TrimStart();
                                    avoidCount = names.Length;
                                }
                            }
                        }
                    }
                }
                catch { }

                var payload = new
                {
                    model = _model,
                    prompt = userPrompt,
                    stream = false,
                    options = new
                    {
                        temperature = 0.7,
                        top_p = 0.9,
                        max_tokens = 2000
                    }
                };

                var json = SecureJsonSerializer.Serialize(payload);
                request.SetContent(json);
                if (avoidCount > 0) { try { _logger.Info("[Brainarr Debug] Applied system avoid list (Ollama): " + avoidCount + " names"); } catch { } }
                request.RequestTimeout = TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.MaxAITimeout));

                var response = await _httpClient.ExecuteAsync(request);
                
                if (DebugFlags.ProviderPayload)
                {
                    try
                    {
                        var url = $"{_baseUrl}/api/generate";
                        var snippet = json?.Length > 4000 ? (json.Substring(0, 4000) + "... [truncated]") : json;
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Ollama endpoint: {url}");
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Ollama request JSON: {snippet}");
                    }
                    catch { }
                }
                
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    // Strip BOM if present
                    var content = response.Content?.TrimStart('\ufeff') ?? "";
                    if (DebugFlags.ProviderPayload)
                    {
                        try
                        {
                            var snippetAll = content?.Length > 4000 ? (content.Substring(0, 4000) + "... [truncated]") : content;
                            _logger.InfoWithCorrelation($"[Brainarr Debug] Ollama raw response: {snippetAll}");
                        }
                        catch { }
                    }
                    var jsonObj = JObject.Parse(content);
                    if (jsonObj["response"] != null)
                    {
                        var contentText = jsonObj["response"].ToString();
                        var parsed = RecommendationJsonParser.Parse(contentText, _logger);
                        var filtered = parsed.Where(r => _validator.ValidateRecommendation(r, allowArtistOnly)).ToList();
                        if (filtered.Any()) return filtered;
                        return ParseRecommendations(contentText, allowArtistOnly);
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
                _logger.Error(ex, $"Request to Ollama timed out after {TimeoutContext.GetSecondsOrDefault(BrainarrConstants.MaxAITimeout)} seconds");
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

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt, System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await GetRecommendationsAsync(prompt);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
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

        public async Task<bool> TestConnectionAsync(System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ok = await TestConnectionAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return ok;
        }

        protected List<Recommendation> ParseRecommendations(string response, bool allowArtistOnly)
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
                            // If artist-only and simple strings are present, treat them as artist names
                            if (item.Type == JTokenType.String && allowArtistOnly)
                            {
                                var name = item.ToString();
                                if (!string.IsNullOrWhiteSpace(name))
                                {
                                    var recStr = new Recommendation
                                    {
                                        Artist = name,
                                        Album = string.Empty,
                                        Genre = "Unknown",
                                        Confidence = 0.7,
                                        Reason = ""
                                    };
                                    if (_validator.ValidateRecommendation(recStr, allowArtistOnly: true))
                                    {
                                        recommendations.Add(recStr);
                                    }
                                }
                                continue;
                            }

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
                                var rec = new Recommendation
                                {
                                    Artist = artistField ?? "Unknown",
                                    Album = albumField ?? "Unknown", 
                                    Genre = genreField ?? "Unknown",
                                    Confidence = confidence,
                                    Reason = reasonField ?? ""
                                };
                                
                                if (_validator.ValidateRecommendation(rec, allowArtistOnly: allowArtistOnly))
                                {
                                    recommendations.Add(rec);
                                }
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
                        // Check dash types; in artist mode accept bare artist lines
                        if (line.Contains('-') || line.Contains('–') || line.Contains('—') || (allowArtistOnly && !(line.Contains('-') || line.Contains('–') || line.Contains('—'))))
                        {
                            if (allowArtistOnly && !(line.Contains('-') || line.Contains('–') || line.Contains('—')))
                            {
                                var artistOnly = System.Text.RegularExpressions.Regex.Replace(line.Trim(), @"^[\d]+\.?\s*", "").TrimStart('•', '*', ' ').Trim();
                                if (!string.IsNullOrWhiteSpace(artistOnly))
                                {
                                    var rec = new Recommendation
                                    {
                                        Artist = artistOnly,
                                        Album = string.Empty,
                                        Confidence = 0.7,
                                        Genre = "Unknown",
                                        Reason = ""
                                    };
                                    if (_validator.ValidateRecommendation(rec, allowArtistOnly: true))
                                    {
                                        recommendations.Add(rec);
                                    }
                                }
                            }
                            else
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
                                    
                                    var rec = new Recommendation
                                    {
                                        Artist = string.IsNullOrWhiteSpace(artistPart) ? "Unknown" : artistPart,
                                        Album = string.IsNullOrWhiteSpace(albumPart) ? "Unknown" : albumPart,
                                        Confidence = 0.7,
                                        Genre = "Unknown",
                                        Reason = ""
                                    };
                                    
                                    if (_validator.ValidateRecommendation(rec, allowArtistOnly: false))
                                    {
                                        recommendations.Add(rec);
                                    }
                                }
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
                        
                        var rec = new Recommendation
                        {
                            Artist = artistPart,
                            Album = albumPart,
                            Confidence = 0.7,
                            Genre = "Unknown",
                            Reason = ""
                        };
                        
                        if (_validator.ValidateRecommendation(rec))
                        {
                            recommendations.Add(rec);
                        }
                    }
                }
            }
            
            return recommendations;
        }

        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _logger.Info($"Ollama model updated to: {modelName}");
            }
        }
    }
}
