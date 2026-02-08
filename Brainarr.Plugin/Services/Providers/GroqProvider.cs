using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Brainarr.Plugin.Services.Security;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public class GroqProvider : IAIProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.IHttpResilience? _httpExec;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private string _model;
        private const string API_URL = BrainarrConstants.GroqChatCompletionsUrl;
        private readonly bool _preferStructured;

        public string ProviderName => "Groq";

        public GroqProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null, bool preferStructured = true, NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.IHttpResilience? httpExec = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpExec = httpExec;

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Groq API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _model = model ?? BrainarrConstants.DefaultGroqModel; // UI label; mapped on request
            _preferStructured = preferStructured;

            _logger.Info($"Initialized Groq provider with model: {_model} (Ultra-fast inference)");
            if (_httpExec == null)
            {
                try { _logger.WarnOnceWithEvent(12001, "GroqProvider", "GroqProvider: IHttpResilience not injected; using static resilience fallback"); } catch (Exception) { /* Non-critical */ }
            }
        }

        private async Task<List<Recommendation>> GetRecommendationsInternalAsync(string prompt, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                var artistOnly = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing.PromptShapeHelper.IsArtistOnly(prompt);
                var systemContent = artistOnly
                    ? @"You are a music recommendation expert. Always return recommendations in JSON format.

Each recommendation MUST have these exact fields:
- artist: The artist name
- genre: The primary genre
- confidence: A number between 0 and 1
- reason: A brief reason for the recommendation

Do NOT include album or year fields.
Return ONLY a JSON array, no other text. Example:
[{""artist"": ""Pink Floyd"", ""genre"": ""Progressive Rock"", ""confidence"": 0.95, ""reason"": ""Iconic progressive artists""}]"
                    : @"You are a music recommendation expert. Always return recommendations in JSON format.

Each recommendation MUST have these exact fields:
- artist: The artist name
- album: The album name
- genre: The primary genre
- confidence: A number between 0 and 1
- reason: A brief reason for the recommendation

Return ONLY a JSON array, no other text. Example:
[{""artist"": ""Pink Floyd"", ""album"": ""Dark Side of the Moon"", ""genre"": ""Progressive Rock"", ""confidence"": 0.95, ""reason"": ""Classic album""}]";

                var temp = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.TemperaturePolicy.FromPrompt(prompt, 0.7);

                var modelRaw = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("groq", _model);
                var cacheKey = $"Groq:{modelRaw}";
                var preferStructuredNow = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared.FormatPreferenceCache.GetPreferStructuredOrDefault(cacheKey, _preferStructured);
                var attempts = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared.ChatRequestFactory.BuildBodies(
                    NzbDrone.Core.ImportLists.Brainarr.AIProvider.Groq,
                    modelRaw,
                    systemContent,
                    prompt,
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
                            origin: $"groq:{modelRaw}",
                            logger: _logger,
                            cancellationToken: ct,
                            maxRetries: 3,
                            maxConcurrencyPerHost: 2,
                            retryBudget: null,
                            perRequestTimeout: TimeSpan.FromSeconds(seconds))
                        : await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithHttpResilienceAsync(
                            request,
                            (req, token) => _httpClient.ExecuteAsync(req),
                            origin: $"groq:{modelRaw}",
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
                            _logger.InfoWithCorrelation($"[Brainarr Debug] Groq endpoint: {API_URL}");
                            _logger.InfoWithCorrelation($"[Brainarr Debug] Groq request JSON: {snippet}");
                        }
                        catch (Exception) { /* Non-critical */ }
                    }
                    return response;
                }

                var startTime = DateTime.UtcNow;
                NzbDrone.Common.Http.HttpResponse response = null;
                var idx = 0; var usedIndex = -1;
                foreach (var body in attempts)
                {
                    response = await SendAsync(body, cancellationToken);
                    if (response == null) { idx++; continue; }
                    var code = (int)response.StatusCode;
                    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest || code == 422) { idx++; continue; }
                    usedIndex = idx;
                    break;
                }
                if (response == null)
                {
                    _logger.Error("Groq request failed with no HTTP response");
                    return new List<Recommendation>();
                }
                NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared.FormatPreferenceCache.SetPreferStructured(cacheKey, usedIndex == 0 && preferStructuredNow);

                // request JSON already logged inside SendAsync when debug is enabled
                var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Error($"Groq API error: {response.StatusCode} - {response.Content}");
                    return new List<Recommendation>();
                }

                _logger.Debug($"Groq response time: {responseTime}ms");

                var responseData = JsonConvert.DeserializeObject<OpenAICompatibleResponse>(response.Content);
                var content = responseData?.Choices?.FirstOrDefault()?.Message?.Content;
                if (DebugFlags.ProviderPayload)
                {
                    try
                    {
                        var snippet = content?.Length > 4000 ? (content.Substring(0, 4000) + "... [truncated]") : content;
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Groq response content: {snippet}");
                        if (responseData?.Usage != null)
                        {
                            _logger.InfoWithCorrelation($"[Brainarr Debug] Groq usage: prompt={responseData.Usage.PromptTokens}, completion={responseData.Usage.CompletionTokens}, total={responseData.Usage.TotalTokens}");
                        }
                    }
                    catch (Exception) { /* Non-critical */ }
                }

                if (string.IsNullOrEmpty(content))
                {
                    _logger.Warn("Empty response from Groq");
                    return new List<Recommendation>();
                }

                // Log usage for monitoring
                if (responseData?.Usage != null)
                {
                    _logger.Debug($"Groq usage - Prompt: {responseData.Usage.PromptTokens}, Completion: {responseData.Usage.CompletionTokens}, Total: {responseData.Usage.TotalTokens}");
                }

                return RecommendationJsonParser.Parse(content, _logger);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from Groq");
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
                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "user", content = "Reply with OK" }
                    },
                    max_tokens = 5,
                    temperature = 0
                };

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(SecureJsonSerializer.Serialize(requestBody));
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);

                var startTime = DateTime.UtcNow;
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)));
                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "groq",
                    logger: _logger,
                    cancellationToken: cts.Token,
                    timeoutSeconds: TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout),
                    maxRetries: 2);
                var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                var success = response.StatusCode == System.Net.HttpStatusCode.OK;
                _logger.Info($"Groq connection test: {(success ? $"Success ({responseTime}ms)" : $"Failed with {response.StatusCode}")}");

                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Groq connection test failed");
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
                var testBody = new
                {
                    model = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("groq", _model),
                    messages = new[] { new { role = "user", content = "Reply with OK" } },
                    max_tokens = 5
                };
                request.SetContent(SecureJsonSerializer.Serialize(testBody));
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);

                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "groq",
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

        // Parsing centralized in RecommendationJsonParser

        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _logger.Info($"Groq model updated to: {modelName}");
            }
        }
    }
}
