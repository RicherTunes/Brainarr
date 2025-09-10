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
        private readonly double? _temperatureOverride;
        private readonly int? _maxTokensOverride;

        /// <summary>
        /// Gets the display name of this provider.
        /// </summary>
        public string ProviderName => "LM Studio";

        public LMStudioProvider(string baseUrl, string model, IHttpClient httpClient, Logger logger, IRecommendationValidator? validator = null, bool allowArtistOnly = false, double? temperature = null, int? maxTokens = null)
        {
            _baseUrl = baseUrl?.TrimEnd('/') ?? BrainarrConstants.DefaultLMStudioUrl;
            _model = model ?? BrainarrConstants.DefaultLMStudioModel;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger;
            _validator = validator ?? new RecommendationValidator(logger);
            _allowArtistOnly = allowArtistOnly;
            _temperatureOverride = temperature;
            _maxTokensOverride = maxTokens;

            _logger.Info($"LMStudioProvider initialized: URL={_baseUrl}, Model={_model}");
        }

        private async Task<List<Recommendation>> GetRecommendationsInternalAsync(string prompt, System.Threading.CancellationToken cancellationToken)
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

                var temp = _temperatureOverride ?? NzbDrone.Core.ImportLists.Brainarr.Services.Providers.TemperaturePolicy.FromPrompt(userContent, 0.5);
                var outTokens = _maxTokensOverride ?? 1200;

                var attempts = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared.ChatRequestFactory.BuildBodies(
                    NzbDrone.Core.ImportLists.Brainarr.AIProvider.LMStudio,
                    _model,
                    systemContent,
                    userContent,
                    temp,
                    outTokens,
                    preferStructured: false);

                async Task<NzbDrone.Common.Http.HttpResponse> SendAsync(object body, System.Threading.CancellationToken ct)
                {
                    var json = JsonConvert.SerializeObject(body);
                    request.SetContent(json);
                    request.RequestTimeout = TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.MaxAITimeout));
                    try
                    {
                        // Execute directly to preserve non-2xx responses for fallback handling
                        var response = await _httpClient.ExecuteAsync(request);
                        return response;
                    }
                    catch (NzbDrone.Common.Http.HttpException ex)
                    {
                        // Surface the HttpResponse (e.g., 400 BadRequest) so callers can inspect error text
                        return ex.Response;
                    }
                }

                bool IsTransientReload(NzbDrone.Common.Http.HttpResponse resp)
                {
                    if (resp == null) return false;
                    var code = (int)resp.StatusCode;
                    if (resp.StatusCode != System.Net.HttpStatusCode.BadRequest &&
                        resp.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable &&
                        code != 409 && // Conflict
                        code != 425)    // Too Early
                    {
                        return false;
                    }
                    var text = (resp.Content ?? string.Empty).ToLowerInvariant();
                    // Common LM Studio transient messages during (re)load/warmup
                    string[] markers = new[]
                    {
                        "model reloaded",
                        "model loading",
                        "loading model",
                        "warming up",
                        "initializing",
                        "downloading",
                        "preloading",
                        "starting model",
                        "loading"
                    };
                    foreach (var m in markers)
                    {
                        if (text.Contains(m)) return true;
                    }
                    return false;
                }

                NzbDrone.Common.Http.HttpResponse response = null;
                foreach (var body in attempts)
                {
                    // Retry same body a few times if LM Studio reports model (re)load/warmup in progress
                    var reloadAttempts = 0;
                    do
                    {
                        response = await SendAsync(body, cancellationToken);
                        if (response == null) break;
                        if (IsTransientReload(response) && reloadAttempts < 5)
                        {
                            reloadAttempts++;
                            var baseDelay = 250 * (int)Math.Pow(2, reloadAttempts - 1);
                            var jitter = new Random().Next(0, 120);
                            var delay = Math.Min(2500, baseDelay + jitter);
                            _logger.Warn($"LM Studio transient state detected (model loading/warmup). Retrying {reloadAttempts}/5 after {delay}ms");
                            try { await Task.Delay(delay, cancellationToken); } catch { }
                            continue;
                        }
                        break;
                    } while (reloadAttempts < 5);

                    if (response == null)
                    {
                        continue;
                    }
                    var code = (int)response.StatusCode;
                    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest || code == 422)
                    {
                        // Try next attempt (e.g., different response_format)
                        continue;
                    }
                    break;
                }
                if (response == null)
                {
                    _logger.Error("LM Studio request failed with no HTTP response");
                    return new List<Recommendation>();
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
                            if (recommendations.Count == 0)
                            {
                                recommendations = ParseRecommendations(messageContent);
                            }
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
                        // Fallback: try to parse entire body as either JSON array or text list
                        var fallback = ParseRecommendations(content);
                        if (fallback.Count > 0) return fallback;
                    }
                }

                // Final fallback: if status OK but structure unexpected, try to parse raw body
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    var lastAttempt = ParseRecommendations(response.Content ?? string.Empty);
                    if (lastAttempt.Count > 0) return lastAttempt;
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

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(NzbDrone.Core.ImportLists.Brainarr.Services.TimeoutContext.GetSecondsOrDefault(NzbDrone.Core.ImportLists.Brainarr.Configuration.BrainarrConstants.DefaultAITimeout)));
            return await GetRecommendationsInternalAsync(prompt, cts.Token);
        }

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt, System.Threading.CancellationToken cancellationToken)
            => await GetRecommendationsInternalAsync(prompt, cancellationToken);



        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var request = new HttpRequestBuilder($"{_baseUrl}/v1/models").Build();
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)));
                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "lmstudio",
                    logger: _logger,
                    cancellationToken: cts.Token,
                    timeoutSeconds: TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout),
                    maxRetries: 2);
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
            try
            {
                var request = new HttpRequestBuilder($"{_baseUrl}/v1/models").Build();
                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "lmstudio",
                    logger: _logger,
                    cancellationToken: cancellationToken,
                    timeoutSeconds: TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout),
                    maxRetries: 2);
                return response.StatusCode == System.Net.HttpStatusCode.OK;
            }
            finally
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
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
