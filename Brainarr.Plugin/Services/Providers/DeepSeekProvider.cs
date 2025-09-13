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
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public class DeepSeekProvider : IAIProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private string _model;
        private const string API_URL = BrainarrConstants.DeepSeekChatCompletionsUrl;
        private readonly bool _preferStructured;

        public string ProviderName => "DeepSeek";

        public DeepSeekProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null, bool preferStructured = true)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("DeepSeek API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _model = model ?? BrainarrConstants.DefaultDeepSeekModel; // UI label; mapped on request
            _preferStructured = preferStructured;

            _logger.Info($"Initialized DeepSeek provider with model: {_model}");
        }

        private async Task<List<Recommendation>> GetRecommendationsInternalAsync(string prompt, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                var artistOnly = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing.PromptShapeHelper.IsArtistOnly(prompt);
                var systemContent = artistOnly
                    ? "You are a music recommendation expert. Always return recommendations in JSON format with fields: artist, genre, confidence (0-1), and reason. Focus on diverse, high-quality artist recommendations. Do not include album or year fields."
                    : "You are a music recommendation expert. Always return recommendations in JSON format with fields: artist, album, genre, confidence (0-1), and reason. Focus on diverse, high-quality album recommendations that match the user's taste.";

                var temp = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.TemperaturePolicy.FromPrompt(prompt, 0.7);

                var modelRaw = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("deepseek", _model);
                var cacheKey = $"DeepSeek:{modelRaw}";
                var preferStructuredNow = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared.FormatPreferenceCache.GetPreferStructuredOrDefault(cacheKey, _preferStructured);
                var attempts = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared.ChatRequestFactory.BuildBodies(
                    NzbDrone.Core.ImportLists.Brainarr.AIProvider.DeepSeek,
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
                    var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithHttpResilienceAsync(
                        _ => _httpClient.ExecuteAsync(request),
                        origin: $"deepseek:{modelRaw}",
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
                            _logger.Info($"[Brainarr Debug] DeepSeek endpoint: {API_URL}");
                            _logger.Info($"[Brainarr Debug] DeepSeek request JSON: {snippet}");
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
                    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest || code == 422) { idx++; continue; }
                    usedIndex = idx;
                    break;
                }
                if (response == null)
                {
                    _logger.Error("DeepSeek request failed with no HTTP response");
                    return new List<Recommendation>();
                }
                NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared.FormatPreferenceCache.SetPreferStructured(cacheKey, usedIndex == 0 && preferStructuredNow);

                // request JSON already logged inside SendAsync when debug is enabled

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Error($"DeepSeek API error: {response.StatusCode} - {response.Content}");
                    return new List<Recommendation>();
                }

                var responseData = JsonConvert.DeserializeObject<DeepSeekResponse>(response.Content);
                var content = responseData?.Choices?.FirstOrDefault()?.Message?.Content;

                if (string.IsNullOrEmpty(content))
                {
                    _logger.Warn("Empty response from DeepSeek");
                    return new List<Recommendation>();
                }

                // Log token usage for cost tracking
                if (responseData?.Usage != null)
                {
                    _logger.Debug($"DeepSeek token usage - Prompt: {responseData.Usage.PromptTokens}, Completion: {responseData.Usage.CompletionTokens}, Total: {responseData.Usage.TotalTokens}");
                }

                return RecommendationJsonParser.Parse(content, _logger);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from DeepSeek");
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
                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithHttpResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "deepseek",
                    logger: _logger,
                    cancellationToken: cts.Token,
                    maxRetries: 2);

                var success = response.StatusCode == System.Net.HttpStatusCode.OK;
                _logger.Info($"DeepSeek connection test: {(success ? "Success" : $"Failed with {response.StatusCode}")}");

                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "DeepSeek connection test failed");
                return false;
            }
        }

        public async Task<bool> TestConnectionAsync(System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var requestBody = new
                {
                    model = _model,
                    messages = new[] { new { role = "user", content = "Reply with OK" } },
                    max_tokens = 5
                };
                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .Build();
                request.Method = HttpMethod.Post;
                request.SetContent(SecureJsonSerializer.Serialize(requestBody));
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);

                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithHttpResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "deepseek",
                    logger: _logger,
                    cancellationToken: cancellationToken,
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
                _logger.Info($"DeepSeek model updated to: {modelName}");
            }
        }

        // Response models (OpenAI-compatible format)
        private class DeepSeekResponse
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("object")]
            public string Object { get; set; }

            [JsonProperty("created")]
            public long Created { get; set; }

            [JsonProperty("model")]
            public string Model { get; set; }

            [JsonProperty("choices")]
            public List<Choice> Choices { get; set; }

            [JsonProperty("usage")]
            public Usage Usage { get; set; }

            [JsonProperty("system_fingerprint")]
            public string SystemFingerprint { get; set; }
        }

        private class Choice
        {
            [JsonProperty("index")]
            public int Index { get; set; }

            [JsonProperty("message")]
            public Message Message { get; set; }

            [JsonProperty("logprobs")]
            public object LogProbs { get; set; }

            [JsonProperty("finish_reason")]
            public string FinishReason { get; set; }
        }

        private class Message
        {
            [JsonProperty("role")]
            public string Role { get; set; }

            [JsonProperty("content")]
            public string Content { get; set; }
        }

        private class Usage
        {
            [JsonProperty("prompt_tokens")]
            public int PromptTokens { get; set; }

            [JsonProperty("completion_tokens")]
            public int CompletionTokens { get; set; }

            [JsonProperty("total_tokens")]
            public int TotalTokens { get; set; }

            [JsonProperty("prompt_cache_hit_tokens")]
            public int? PromptCacheHitTokens { get; set; }

            [JsonProperty("prompt_cache_miss_tokens")]
            public int? PromptCacheMissTokens { get; set; }
        }
    }
}
