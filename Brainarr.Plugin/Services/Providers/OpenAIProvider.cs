using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using Brainarr.Plugin.Models;
using Brainarr.Plugin.Services.Security;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.StructuredOutputs;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// OpenAI provider implementation for music recommendations using GPT models.
    /// Supports GPT-4, GPT-4 Turbo, GPT-3.5 Turbo, and other OpenAI models.
    /// </summary>
    /// <remarks>
    /// This provider requires an OpenAI API key from https://platform.openai.com/api-keys
    /// Pricing varies by model: GPT-4 is more expensive but higher quality,
    /// while GPT-3.5-turbo is more cost-effective for basic recommendations.
    /// </remarks>
    public class OpenAIProvider : IAIProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.IHttpResilience? _httpExec;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private string _model;
        private const string API_URL = BrainarrConstants.OpenAIChatCompletionsUrl;
        private readonly bool _preferStructured;
        private string? _lastUserMessage;
        private string? _lastUserLearnMoreUrl;

        /// <summary>
        /// Gets the display name of this provider.
        /// </summary>
        public string ProviderName => "OpenAI";

        /// <summary>
        /// Initializes a new instance of the OpenAIProvider class.
        /// </summary>
        /// <param name="httpClient">HTTP client for API communication</param>
        /// <param name="logger">Logger for diagnostic information</param>
        /// <param name="apiKey">OpenAI API key (required)</param>
        /// <param name="model">Model to use (defaults to gpt-4o-mini for cost efficiency)</param>
        /// <exception cref="ArgumentNullException">Thrown when httpClient or logger is null</exception>
        /// <exception cref="ArgumentException">Thrown when apiKey is null or empty</exception>
        public OpenAIProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null, bool preferStructured = true, NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.IHttpResilience? httpExec = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpExec = httpExec;

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("OpenAI API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _model = model ?? BrainarrConstants.DefaultOpenAIModel; // UI label; mapped to raw id on request
            _preferStructured = preferStructured;

            _logger.Info($"Initialized OpenAI provider with model: {_model}");
            if (_httpExec == null)
            {
                try { _logger.WarnOnceWithEvent(12001, "OpenAIProvider", "OpenAIProvider: IHttpResilience not injected; using static resilience fallback"); } catch { }
            }
        }

        /// <summary>
        /// Gets music recommendations from OpenAI based on the provided prompt.
        /// </summary>
        /// <param name="prompt">The prompt containing user's music library and preferences</param>
        /// <returns>List of music recommendations with confidence scores and reasoning</returns>
        /// <remarks>
        /// Uses the Chat Completions API with a system message to ensure JSON formatted responses.
        /// Implements retry logic and comprehensive error handling for reliability.
        /// Token usage is optimized through prompt engineering and temperature settings.
        /// </remarks>
        private async Task<List<Recommendation>> GetRecommendationsInternalAsync(string prompt, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                // SECURITY: Validate and sanitize prompt
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    throw new ArgumentException("Prompt cannot be empty");
                }

                if (prompt.Length > 10000)
                {
                    _logger.Warn("Prompt exceeds recommended length, truncating");
                    prompt = prompt.Substring(0, 10000);
                }

                string userContent = prompt;
                string avoidAppendix = string.Empty;
                int avoidCount = 0;
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
                                    avoidAppendix = " Additionally, do not recommend these entities under any circumstances: " + string.Join(", ", names) + ".";
                                    avoidCount = names.Length;
                                }
                            }
                            userContent = userContent.Substring(endIdx + 2).TrimStart();
                        }
                    }
                }
                catch { }

                var artistOnly = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing.PromptShapeHelper.IsArtistOnly(userContent);
                var systemContent = artistOnly
                    ? "You are a music recommendation expert. Always return recommendations in JSON format with fields: artist, genre, confidence (0-1), and reason. Provide diverse, high-quality artist recommendations based on the user's music taste. Do not include album or year fields."
                    : "You are a music recommendation expert. Always return recommendations in JSON format with fields: artist, album, genre, confidence (0-1), and reason. Provide diverse, high-quality recommendations based on the user's music taste.";
                if (avoidCount > 0) { try { _logger.Info("[Brainarr Debug] Applied system avoid list (OpenAI): " + avoidCount + " names"); } catch { } }

                var temp = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.TemperaturePolicy.FromPrompt(userContent, 0.8);

                var modelRaw = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("openai", _model);
                var cacheKey = $"OpenAI:{modelRaw}";
                var preferStructuredNow = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared.FormatPreferenceCache.GetPreferStructuredOrDefault(cacheKey, _preferStructured);
                var attempts = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared.ChatRequestFactory.BuildBodies(
                    NzbDrone.Core.ImportLists.Brainarr.AIProvider.OpenAI,
                    modelRaw,
                    systemContent + avoidAppendix,
                    userContent,
                    temp,
                    2000,
                    preferStructured: preferStructuredNow);

                async Task<NzbDrone.Common.Http.HttpResponse> SendAsync(object body, System.Threading.CancellationToken ct)
                {
                    var request = new HttpRequestBuilder(API_URL)
                        .SetHeader("Authorization", $"Bearer {_apiKey}")
                        .SetHeader("Content-Type", "application/json")
                        .Build();

                    request.Method = HttpMethod.Post;
                    var json = SecureJsonSerializer.Serialize(body);
                    request.SetContent(json);

                    var seconds = TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout);
                    request.RequestTimeout = TimeSpan.FromSeconds(seconds);
                    var response = _httpExec != null
                        ? await _httpExec.SendAsync(
                            templateRequest: request,
                            send: (req, token) => _httpClient.ExecuteAsync(req),
                            origin: $"openai:{modelRaw}",
                            logger: _logger,
                            cancellationToken: ct,
                            maxRetries: 3,
                            maxConcurrencyPerHost: 2,
                            retryBudget: null,
                            perRequestTimeout: TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)))
                        : await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithHttpResilienceAsync(
                            request,
                            (req, token) => _httpClient.ExecuteAsync(req),
                            origin: $"openai:{modelRaw}",
                            logger: _logger,
                            cancellationToken: ct,
                            maxRetries: 3,
                            shouldRetry: resp =>
                            {
                                var code = (int)resp.StatusCode;
                                return resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                                       resp.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
                                       (code >= 500 && code <= 504);
                            });

                    if (DebugFlags.ProviderPayload)
                    {
                        try
                        {
                            var snippet = json?.Length > 4000 ? (json.Substring(0, 4000) + "... [truncated]") : json;
                            _logger.InfoWithCorrelation($"[Brainarr Debug] OpenAI endpoint: {API_URL}");
                            _logger.InfoWithCorrelation($"[Brainarr Debug] OpenAI request JSON: {snippet}");
                        }
                        catch { }
                    }

                    return response;
                }

                NzbDrone.Common.Http.HttpResponse response = null;
                var idx = 0; var usedIndex = -1;
                foreach (var body in attempts)
                {
                    response = await SendAsync(body, cancellationToken);
                    if (response == null) { idx++; continue; }
                    var code = (int)response.StatusCode;
                    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest || code == 422)
                    {
                        idx++; continue; // try next
                    }
                    usedIndex = idx;
                    break;
                }
                if (response == null)
                {
                    _logger.Error("OpenAI request failed with no HTTP response");
                    return new List<Recommendation>();
                }
                // Cache preference: if first attempt (schema) succeeded, prefer structured next time; else prefer text/bare
                NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared.FormatPreferenceCache.SetPreferStructured(cacheKey, usedIndex == 0 && preferStructuredNow);

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    // Sanitize error message to prevent information disclosure
                    _logger.Error($"OpenAI API error: {response.StatusCode}");
                    _logger.Debug($"OpenAI API response details: {response.Content?.Substring(0, Math.Min(response.Content?.Length ?? 0, 500))}");
                    return new List<Recommendation>();
                }

                string content = null;
                ProviderResponses.OpenAIResponse responseData = null;
                try
                {
                    responseData = SecureJsonSerializer.Deserialize<ProviderResponses.OpenAIResponse>(response.Content);
                    content = responseData?.Choices?.FirstOrDefault()?.Message?.Content;
                }
                catch
                {
                    // Fallback: mock/test may return raw JSON array/object directly
                    var parsed = RecommendationJsonParser.Parse(response.Content ?? string.Empty, _logger);
                    if (parsed.Count > 0) return parsed;
                }
                if (DebugFlags.ProviderPayload)
                {
                    try
                    {
                        var snippet = content?.Length > 4000 ? (content.Substring(0, 4000) + "... [truncated]") : content;
                        _logger.InfoWithCorrelation($"[Brainarr Debug] OpenAI response content: {snippet}");
                        if (responseData?.Usage != null)
                        {
                            _logger.InfoWithCorrelation($"[Brainarr Debug] OpenAI usage: prompt={responseData.Usage.PromptTokens}, completion={responseData.Usage.CompletionTokens}, total={responseData.Usage.TotalTokens}");
                        }
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(content))
                {
                    // Fallback: some tests/mocks provide raw arrays or simplified shapes in the body
                    var fallback = RecommendationJsonParser.Parse(response.Content ?? string.Empty, _logger);
                    if (fallback.Count > 0) return fallback;
                    _logger.Warn("Empty response from OpenAI");
                    return new List<Recommendation>();
                }

                // Strip typical wrappers (citations, fences)
                try { content = System.Text.RegularExpressions.Regex.Replace(content, @"\[\d{1,3}\]", string.Empty); } catch { }
                content = content.Replace("```json", string.Empty).Replace("```", string.Empty);

                // Structured JSON pre-validate and one-shot repair
                if (!StructuredJsonValidator.IsLikelyValid(content))
                {
                    if (StructuredJsonValidator.TryRepair(content, out var repaired))
                    {
                        content = repaired;
                    }
                }

                var parsedResult = TryParseLenient(content);
                if (parsedResult.Count == 0)
                {
                    parsedResult = RecommendationJsonParser.Parse(content, _logger);
                }
                if (parsedResult.Count == 0)
                {
                    // Secondary fallback: parse raw body if the inner content was unstructured
                    parsedResult = RecommendationJsonParser.Parse(response.Content ?? string.Empty, _logger);
                    if (parsedResult.Count == 0)
                    {
                        // Tertiary fallback: lenient parse using JToken for test/mocks
                        parsedResult = TryParseLenient(content);
                        if (parsedResult.Count == 0)
                        {
                            parsedResult = TryParseLenient(response.Content ?? string.Empty);
                        }
                    }
                }
                // Provider-level defaults: fill missing genre with "Unknown" as tests expect
                for (int i = 0; i < parsedResult.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(parsedResult[i].Genre))
                    {
                        parsedResult[i] = parsedResult[i] with { Genre = "Unknown" };
                    }
                }
                return parsedResult;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from OpenAI");
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

        private static List<Recommendation> TryParseLenient(string text)
        {
            var list = new List<Recommendation>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            try
            {
                var tok = Newtonsoft.Json.Linq.JToken.Parse(text);
                if (tok.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                {
                    var obj = (Newtonsoft.Json.Linq.JObject)tok;
                    var recs = obj["recommendations"] ?? obj.Property("recommendations")?.Value;
                    if (recs is Newtonsoft.Json.Linq.JArray arr)
                    {
                        foreach (var it in arr) MapRec(it, list);
                    }
                    else
                    {
                        MapRec(obj, list);
                    }
                }
                else if (tok is Newtonsoft.Json.Linq.JArray arr)
                {
                    foreach (var it in arr) MapRec(it, list);
                }
            }
            catch { }
            return list;
        }

        private static void MapRec(Newtonsoft.Json.Linq.JToken it, List<Recommendation> list)
        {
            if (it?.Type != Newtonsoft.Json.Linq.JTokenType.Object) return;
            string GetStr(string name)
            {
                var o = (Newtonsoft.Json.Linq.JObject)it;
                var p = o.Property(name) ?? o.Properties().FirstOrDefault(pr => string.Equals(pr.Name, name, StringComparison.OrdinalIgnoreCase));
                return p?.Value?.Type == Newtonsoft.Json.Linq.JTokenType.String ? (string)p.Value : null;
            }
            double GetDouble(string name)
            {
                var o = (Newtonsoft.Json.Linq.JObject)it;
                var p = o.Property(name) ?? o.Properties().FirstOrDefault(pr => string.Equals(pr.Name, name, StringComparison.OrdinalIgnoreCase));
                if (p == null) return 0.85;
                if (p.Value.Type == Newtonsoft.Json.Linq.JTokenType.Float || p.Value.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                    return (double)p.Value;
                if (p.Value.Type == Newtonsoft.Json.Linq.JTokenType.String && double.TryParse((string)p.Value, out var d)) return d;
                return 0.85;
            }
            var artist = GetStr("artist");
            if (string.IsNullOrWhiteSpace(artist)) return;
            var album = GetStr("album") ?? string.Empty;
            var genre = GetStr("genre");
            var reason = GetStr("reason");
            var conf = GetDouble("confidence");
            list.Add(new Recommendation
            {
                Artist = artist,
                Album = album,
                Genre = genre,
                Reason = reason,
                Confidence = conf
            });
        }



        /// <summary>
        /// Tests the connection to the OpenAI API.
        /// </summary>
        /// <returns>True if the API is accessible and the key is valid; otherwise, false</returns>
        /// <remarks>
        /// Performs a lightweight request to the models endpoint to verify connectivity
        /// and API key validity without consuming significant tokens.
        /// </remarks>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "user", content = "Reply with OK" }
                    },
                    max_tokens = 5
                };

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(SecureJsonSerializer.Serialize(requestBody));

                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)));
                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "openai",
                    logger: _logger,
                    cancellationToken: cts.Token,
                    timeoutSeconds: TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout),
                    maxRetries: 2);

                var success = response.StatusCode == System.Net.HttpStatusCode.OK;
                _logger.Info($"OpenAI connection test: {(success ? "Success" : $"Failed with {response.StatusCode}")}");
                if (!success)
                {
                    TryCaptureOpenAIHint(response.Content, (int)response.StatusCode);
                }
                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "OpenAI connection test failed");
                if (ex is NzbDrone.Common.Http.HttpException httpEx)
                {
                    var sc = httpEx.Response != null ? (int)httpEx.Response.StatusCode : 0;
                    TryCaptureOpenAIHint(httpEx.Response?.Content, sc);
                }
                return false;
            }
        }

        public async Task<bool> TestConnectionAsync(System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                var body = new
                {
                    model = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("openai", _model),
                    messages = new[] { new { role = "user", content = "Reply with OK" } },
                    max_tokens = 5
                };
                request.SetContent(SecureJsonSerializer.Serialize(body));
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);

                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "openai",
                    logger: _logger,
                    cancellationToken: cancellationToken,
                    timeoutSeconds: TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout),
                    maxRetries: 2);
                var ok = response.StatusCode == System.Net.HttpStatusCode.OK;
                if (!ok)
                {
                    TryCaptureOpenAIHint(response.Content, (int)response.StatusCode);
                }
                return ok;
            }
            finally
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        // Parsing is centralized in RecommendationJsonParser

        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _logger.Info($"OpenAI model updated to: {modelName}");
            }
        }

        public string? GetLastUserMessage() => _lastUserMessage;
        public string? GetLearnMoreUrl() => _lastUserLearnMoreUrl;

        private void TryCaptureOpenAIHint(string? body, int status)
        {
            try
            {
                _lastUserMessage = null;
                _lastUserLearnMoreUrl = null;
                var content = body ?? string.Empty;
                if (status == 401 || content.IndexOf("invalid_api_key", StringComparison.OrdinalIgnoreCase) >= 0 || content.IndexOf("Incorrect API key", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _lastUserMessage = "Invalid OpenAI API key. Ensure it starts with 'sk-' and is active. Recreate at https://platform.openai.com/api-keys and verify billing if required.";
                    _lastUserLearnMoreUrl = BrainarrConstants.DocsOpenAIInvalidKey;
                }
                else if (status == 429 || content.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _lastUserMessage = "OpenAI rate limit exceeded. Wait 1–5 minutes, reduce request frequency, or switch to a cheaper model.";
                    _lastUserLearnMoreUrl = BrainarrConstants.DocsOpenAIRateLimit;
                }
                else if (content.IndexOf("insufficient_quota", StringComparison.OrdinalIgnoreCase) >= 0 || content.IndexOf("insufficient", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _lastUserMessage = "OpenAI quota/credits exhausted. Add payment method or reduce usage.";
                    _lastUserLearnMoreUrl = BrainarrConstants.DocsOpenAIRateLimit;
                }
            }
            catch { }
        }

        // Response models are now in ProviderResponses.cs for secure deserialization
    }
}
