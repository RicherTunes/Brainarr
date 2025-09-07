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
    public class OpenRouterProvider : IAIProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private string _model;
        private const string API_URL = BrainarrConstants.OpenRouterChatCompletionsUrl;

        public string ProviderName => "OpenRouter";

        public OpenRouterProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("OpenRouter API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _model = model ?? BrainarrConstants.DefaultOpenRouterModel; // UI label; mapped on request

            _logger.Info($"Initialized OpenRouter provider with model: {_model}");
        }

        private async Task<List<Recommendation>> GetRecommendationsInternalAsync(string prompt, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                // Support optional SYSTEM_AVOID marker at the top of the prompt like [[SYSTEM_AVOID:Name — reason|...]]
                string userContent = prompt ?? string.Empty;
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
                if (avoidCount > 0) { try { _logger.Info("[Brainarr Debug] Applied system avoid list (OpenRouter): " + avoidCount + " names"); } catch { } }
                var systemContent = artistOnly
                    ? "You are a music recommendation expert. Always return recommendations in JSON format with fields: artist, genre, confidence (0-1), and reason. Provide diverse, high-quality artist recommendations based on the user's music taste. Do not include album or year fields."
                    : "You are a music recommendation expert. Always return recommendations in JSON format with fields: artist, album, genre, confidence (0-1), and reason. Provide diverse, high-quality recommendations based on the user's music taste.";

                var temp = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.TemperaturePolicy.FromPrompt(userContent, 0.8);

                var modelRaw = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("openrouter", _model);
                object bodyWithFormat = new
                {
                    model = modelRaw,
                    messages = new[]
                    {
                        new { role = "system", content = systemContent + avoidAppendix },
                        new { role = "user", content = userContent }
                    },
                    response_format = new { type = "json_object" },
                    temperature = temp,
                    max_tokens = 2000,
                    transforms = new[] { "middle-out" },
                    route = "fallback"
                };
                object bodyWithoutFormat = new
                {
                    model = modelRaw,
                    messages = new[]
                    {
                        new { role = "system", content = systemContent + avoidAppendix },
                        new { role = "user", content = userContent }
                    },
                    temperature = temp,
                    max_tokens = 2000,
                    transforms = new[] { "middle-out" },
                    route = "fallback"
                };

                async Task<NzbDrone.Common.Http.HttpResponse> SendAsync(object body, System.Threading.CancellationToken ct)
                {
                    var request = new HttpRequestBuilder(API_URL)
                        .SetHeader("Authorization", $"Bearer {_apiKey}")
                        .SetHeader("Content-Type", "application/json")
                        .SetHeader("HTTP-Referer", BrainarrConstants.ProjectReferer)
                        .SetHeader("X-Title", BrainarrConstants.OpenRouterTitle)
                        .Build();

                    request.Method = HttpMethod.Post;
                    var json = SecureJsonSerializer.Serialize(body);
                    request.SetContent(json);
                    var seconds = TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout);
                    request.RequestTimeout = TimeSpan.FromSeconds(seconds);
                    var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                        _ => _httpClient.ExecuteAsync(request),
                        origin: "openrouter",
                        logger: _logger,
                        cancellationToken: ct,
                        timeoutSeconds: seconds,
                        maxRetries: 3);
                    // request JSON already logged inside SendAsync when debug is enabled
                    return response;
                }

                var response = await SendAsync(bodyWithFormat, cancellationToken);
                if (response.StatusCode == System.Net.HttpStatusCode.BadRequest || (int)response.StatusCode == 422)
                {
                    _logger.Warn("OpenRouter response_format not supported; retrying without structured JSON request");
                    response = await SendAsync(bodyWithoutFormat, cancellationToken);
                }

                // request JSON already logged inside SendAsync when debug is enabled

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Error($"OpenRouter API error: {response.StatusCode} - {response.Content}");

                    // Log specific error if available
                    try
                    {
                        var errorResponse = JsonConvert.DeserializeObject<OpenRouterError>(response.Content);
                        if (errorResponse?.Error != null)
                        {
                            _logger.Error($"OpenRouter error details: {errorResponse.Error.Message} (Code: {errorResponse.Error.Code})");
                        }
                    }
                    catch { }

                    return new List<Recommendation>();
                }

                var responseData = JsonConvert.DeserializeObject<OpenRouterResponse>(response.Content);
                var content = responseData?.Choices?.FirstOrDefault()?.Message?.Content;
                if (DebugFlags.ProviderPayload)
                {
                    try
                    {
                        var snippet = content?.Length > 4000 ? (content.Substring(0, 4000) + "... [truncated]") : content;
                        _logger.InfoWithCorrelation($"[Brainarr Debug] OpenRouter response content: {snippet}");
                        if (responseData?.Usage != null)
                        {
                            _logger.InfoWithCorrelation($"[Brainarr Debug] OpenRouter usage: prompt={responseData.Usage.PromptTokens}, completion={responseData.Usage.CompletionTokens}, total={responseData.Usage.TotalTokens}");
                        }
                    }
                    catch { }
                }
                if (DebugFlags.ProviderPayload)
                {
                    try
                    {
                        var snippet = content?.Length > 4000 ? (content.Substring(0, 4000) + "... [truncated]") : content;
                        _logger.Info($"[Brainarr Debug] OpenRouter response content: {snippet}");
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(content))
                {
                    _logger.Warn("Empty response from OpenRouter");
                    return new List<Recommendation>();
                }

                // Log the model that actually handled the request (might differ due to fallback)
                if (!string.IsNullOrEmpty(responseData?.Model))
                {
                    _logger.Debug($"Request handled by model: {responseData.Model}");
                }

                return RecommendationJsonParser.Parse(content, _logger);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from OpenRouter");
                return new List<Recommendation>();
            }
        }

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
            => await GetRecommendationsInternalAsync(prompt, System.Threading.CancellationToken.None);

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt, System.Threading.CancellationToken cancellationToken)
            => await GetRecommendationsInternalAsync(prompt, cancellationToken);



        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                // Use a widely available minimal model for connection test to save costs
                var requestBody = new
                {
                    model = BrainarrConstants.DefaultOpenRouterTestModelRaw,
                    messages = new[]
                    {
                        new { role = "user", content = "Reply with OK" }
                    },
                    max_tokens = 5
                };

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .SetHeader("HTTP-Referer", BrainarrConstants.ProjectReferer)
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(SecureJsonSerializer.Serialize(requestBody));
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);

                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "openrouter",
                    logger: _logger,
                    cancellationToken: System.Threading.CancellationToken.None,
                    timeoutSeconds: TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout),
                    maxRetries: 2);

                var success = response.StatusCode == System.Net.HttpStatusCode.OK;
                _logger.Info($"OpenRouter connection test: {(success ? "Success" : $"Failed with {response.StatusCode}")}");

                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "OpenRouter connection test failed");
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
                    model = BrainarrConstants.DefaultOpenRouterTestModelRaw,
                    messages = new[]
                    {
                        new { role = "user", content = "Reply with OK" }
                    },
                    max_tokens = 5
                };

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .SetHeader("HTTP-Referer", BrainarrConstants.ProjectReferer)
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(SecureJsonSerializer.Serialize(requestBody));
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);

                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "openrouter",
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

        // Response models
        private class OpenRouterResponse
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("model")]
            public string Model { get; set; }

            [JsonProperty("object")]
            public string Object { get; set; }

            [JsonProperty("created")]
            public long Created { get; set; }

            [JsonProperty("choices")]
            public List<Choice> Choices { get; set; }

            [JsonProperty("usage")]
            public Usage Usage { get; set; }
        }

        private class Choice
        {
            [JsonProperty("index")]
            public int Index { get; set; }

            [JsonProperty("message")]
            public Message Message { get; set; }

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
        }

        private class OpenRouterError
        {
            [JsonProperty("error")]
            public ErrorDetail Error { get; set; }
        }

        private class ErrorDetail
        {
            [JsonProperty("message")]
            public string Message { get; set; }

            [JsonProperty("type")]
            public string Type { get; set; }

            [JsonProperty("code")]
            public string Code { get; set; }
        }

        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _logger.Info($"OpenRouter model updated to: {modelName}");
            }
        }
    }
}
