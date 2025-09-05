using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers
{
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
        private string _model;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly IRecommendationValidator _validator;
        private readonly bool _allowArtistOnly;

        /// <summary>
        /// Gets the display name of this provider.
        /// </summary>
        public string ProviderName => "LM Studio";

        public LMStudioProvider(string baseUrl, string model, IHttpClient httpClient, Logger logger, IRecommendationValidator? validator = null, bool allowArtistOnly = false)
        {
            _baseUrl = baseUrl?.TrimEnd('/') ?? BrainarrConstants.DefaultLMStudioUrl;
            _model = model ?? BrainarrConstants.DefaultLMStudioModel;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger;
            _validator = validator ?? new RecommendationValidator(logger);
            _allowArtistOnly = allowArtistOnly;
            
            _logger.Info($"LMStudioProvider initialized: URL={_baseUrl}, Model={_model}");
        }

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            _logger.Debug($"LM Studio: Attempting connection to {_baseUrl} with model {_model}");
            
            try
            {
                var request = new HttpRequestBuilder($"{_baseUrl}/v1/chat/completions")
                    .Accept(HttpAccept.Json)
                    .SetHeader("Content-Type", "application/json")
                    .Post()
                    .Build();
                
                // Set timeout for AI request
                var seconds = TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout);
                request.RequestTimeout = TimeSpan.FromSeconds(seconds);

                var systemContent = _allowArtistOnly
                    ? (
                        "You are a music recommendation engine. Return ONLY a valid JSON array. " +
                        "Each item must be an object with keys: artist (string), genre (string), confidence (0..1), reason (string). " +
                        "Recommend artists only (not specific albums). Do NOT include an 'album' or 'year' field. " +
                        "Only include real, existing artists whose albums can be found on MusicBrainz or Qobuz. " +
                        "Follow the user's instructions for the exact number of items. " +
                        "No prose, no markdown, no extra keys."
                      )
                    : (
                        "You are a music recommendation engine. Return ONLY a valid JSON array. " +
                        "Each item must be an object with keys: artist (string), album (string), genre (string), year (int), confidence (0..1), reason (string). " +
                        "Only include real, existing studio albums that can be found on MusicBrainz or Qobuz. " +
                        "Do NOT invent special editions, imaginary remasters, or speculative releases. " +
                        "Follow the user's instructions for the exact number of items. " +
                        "If you are uncertain an album exists, exclude it. No prose, no markdown, no extra keys."
                      );

                // Elevate optional SYSTEM_AVOID marker in the prompt into system instructions
                string userContent = prompt ?? string.Empty;
                try
                {
                    if (!string.IsNullOrWhiteSpace(userContent) && userContent.StartsWith("[[SYSTEM_AVOID:"))
                    {
                        var endIdx = userContent.IndexOf("]]", StringComparison.Ordinal);
                        if (endIdx > 0)
                        {
                            var marker = userContent.Substring(0, endIdx + 2);
                            var inner = marker.Substring("[[SYSTEM_AVOID:".Length, marker.Length - "[[SYSTEM_AVOID:".Length - 2);
                            if (!string.IsNullOrWhiteSpace(inner))
                            {
                                var names = inner.Split('|').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                                if (names.Length > 0)
                                {
                                    var avoidSentence = " Additionally, do not recommend these entities under any circumstances: " + string.Join(", ", names) + ".";
                                    systemContent += avoidSentence;
                                    try { _logger.Info("[Brainarr Debug] Applied system avoid list (LM Studio): " + names.Length + " names"); } catch { }
                                }
                            }
                            // Remove marker from user content
                            userContent = userContent.Substring(endIdx + 2).TrimStart();
                        }
                    }
                }
                catch { }

                object bodyWithFormat = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "system", content = systemContent },
                        new { role = "user", content = userContent }
                    },
                    response_format = new { type = "json_object" },
                    temperature = 0.5,
                    max_tokens = 1200,
                    stream = false
                };
                object bodyWithoutFormat = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "system", content = systemContent },
                        new { role = "user", content = userContent }
                    },
                    temperature = 0.5,
                    max_tokens = 1200,
                    stream = false
                };

                async Task<NzbDrone.Common.Http.HttpResponse> SendAsync(object body)
                {
                    var json = JsonConvert.SerializeObject(body);
                    request.SetContent(json);
                    request.RequestTimeout = TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.MaxAITimeout));
                    var response = await _httpClient.ExecuteAsync(request);
                    // request JSON already logged here when debug is enabled
                    return response;
                }

                var response = await SendAsync(bodyWithFormat);
                if (response.StatusCode == System.Net.HttpStatusCode.BadRequest || (int)response.StatusCode == 422)
                {
                    _logger.Warn("LM Studio response_format not supported; retrying without structured JSON request");
                    response = await SendAsync(bodyWithoutFormat);
                }
                _logger.Debug($"LM Studio: Connection response - Status: {response.StatusCode}, Content Length: {response.Content?.Length ?? 0}");
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    // Strip BOM if present
                    var content = response.Content?.TrimStart('\ufeff') ?? "";
                    
                    _logger.Info($"LM Studio response received, content length: {content.Length}");
                    _logger.Debug($"LM Studio response structure validated");
                    
                    var jsonObj = JObject.Parse(content);
                    _logger.Debug($"LM Studio response contains {jsonObj.Properties().Count()} properties");
                    
                    if (jsonObj["choices"] is JArray choices && choices.Count > 0)
                    {
                        var firstChoice = choices[0] as JObject;
                        if (firstChoice?["message"]?["content"] != null)
                        {
                            var messageContent = firstChoice["message"]["content"].ToString();
                            _logger.Debug($"LM Studio content extracted, length: {messageContent.Length}");
                            
                            var recommendations = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing.RecommendationJsonParser.Parse(messageContent, _logger);
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
                _logger.Error(ex, $"Request to LM Studio timed out after {TimeoutContext.GetSecondsOrDefault(BrainarrConstants.MaxAITimeout)} seconds");
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
                var request = new HttpRequestBuilder($"{_baseUrl}/v1/models").Build();
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

        protected List<Recommendation> ParseRecommendations(string response)
        {
            var recommendations = new List<Recommendation>();
            
            try
            {
                _logger.Debug($"[LM Studio] Parsing recommendations from response: {response?.Substring(0, Math.Min(200, response?.Length ?? 0))}...");
                
                // Try to parse as JSON first
                if (response.Contains("[") && response.Contains("]"))
                {
                    var startIndex = response.IndexOf('[');
                    var endIndex = response.LastIndexOf(']') + 1;
                    if (startIndex >= 0 && endIndex > startIndex)
                    {
                        var jsonStr = response.Substring(startIndex, endIndex - startIndex);
                        _logger.Debug($"[LM Studio] Extracted JSON: {jsonStr.Substring(0, Math.Min(300, jsonStr.Length))}...");
                        
                        var items = JsonConvert.DeserializeObject<JArray>(jsonStr);
                        _logger.Info($"[LM Studio] Deserialized {items.Count} items from JSON");
                        
                        foreach (var item in items)
                        {
                            var rec = new Recommendation
                            {
                                Artist = item["artist"]?.ToString() ?? "Unknown",
                                Album = item["album"]?.ToString() ?? string.Empty,
                                Genre = item["genre"]?.ToString() ?? "Unknown",
                                Confidence = item["confidence"]?.Value<double>() ?? 0.7,
                                Reason = item["reason"]?.ToString() ?? "",
                                Year = item["year"]?.Value<int?>()
                            };
                            
                            if (_validator.ValidateRecommendation(rec, _allowArtistOnly))
                            {
                                _logger.Debug($"[LM Studio] Parsed recommendation: {rec.Artist} - {rec.Album}");
                                recommendations.Add(rec);
                            }
                            else
                            {
                                _logger.Debug($"[LM Studio] Filtered out invalid recommendation: {rec.Artist} - {rec.Album}");
                            }
                        }
                    }
                }
                else
                {
                    _logger.Debug("[LM Studio] No JSON array found, trying text parsing");
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
                                
                                if (_validator.ValidateRecommendation(rec))
                                {
                                    _logger.Debug($"[LM Studio] Text parsed recommendation: {rec.Artist} - {rec.Album}");
                                    recommendations.Add(rec);
                                }
                                else
                                {
                                    _logger.Debug($"[LM Studio] Filtered out invalid text recommendation: {rec.Artist} - {rec.Album}");
                                }
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

        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _logger.Info($"LM Studio model updated to: {modelName}");
            }
        }
    }
}

