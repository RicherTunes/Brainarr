using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using Brainarr.Plugin.Services.Security;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.StructuredOutputs;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public class PerplexityProvider : IAIProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private string _model;
        private const string API_URL = "https://api.perplexity.ai/chat/completions";

        public string ProviderName => "Perplexity";

        public PerplexityProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = "sonar-large")
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Perplexity API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _model = string.IsNullOrWhiteSpace(model) ? "sonar-large" : model; // Default to current best online model

            _logger.Info($"Initialized Perplexity provider with model: {_model}");
        }

        private async Task<List<Recommendation>> GetRecommendationsInternalAsync(string prompt, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                var artistOnly = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing.PromptShapeHelper.IsArtistOnly(prompt);
                var systemContent = artistOnly
                    ? "You are a music recommendation expert. Always return strictly valid JSON as { \"recommendations\": [ { \"artist\": string, \"genre\": string?, \"confidence\": number 0-1, \"reason\": string? } ] }. Do not include album or year fields. No extra prose."
                    : "You are a music recommendation expert. Always return strictly valid JSON as { \"recommendations\": [ { \"artist\": string, \"album\": string, \"genre\": string?, \"year\": number?, \"confidence\": number 0-1, \"reason\": string? } ] }. No extra prose.";

                var temp = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.TemperaturePolicy.FromPrompt(prompt, 0.7);

                var modelRaw = NzbDrone.Core.ImportLists.Brainarr.Configuration.ModelIdMapper.ToRawId("perplexity", _model);
                var requestBody = new
                {
                    model = modelRaw,
                    messages = new[]
                    {
                        new { role = "system", content = systemContent },
                        new { role = "user", content = prompt }
                    },
                    temperature = temp,
                    max_tokens = 2000,
                    stream = false,
                    response_format = StructuredOutputSchemas.GetRecommendationResponseFormat()
                };

                async Task<NzbDrone.Common.Http.HttpResponse> SendAsync(object body, System.Threading.CancellationToken ct)
                {
                    var request = new HttpRequestBuilder(API_URL)
                        .SetHeader("Authorization", $"Bearer {_apiKey}")
                        .SetHeader("Content-Type", "application/json")
                        .SetHeader("Accept", "application/json")
                        .Build();

                    request.Method = HttpMethod.Post;
                    var json = SecureJsonSerializer.Serialize(body);
                    request.SetContent(json);

                    var seconds = TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout);
                    request.RequestTimeout = TimeSpan.FromSeconds(seconds);
                    var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                        _ => _httpClient.ExecuteAsync(request),
                        origin: "perplexity",
                        logger: _logger,
                        cancellationToken: ct,
                        timeoutSeconds: seconds,
                        maxRetries: 3);

                    // request JSON already logged inside SendAsync when debug is enabled
                    return response;
                }

                var response = await SendAsync(requestBody, cancellationToken);
                if (response.StatusCode == System.Net.HttpStatusCode.BadRequest || (int)response.StatusCode == 422)
                {
                    _logger.Warn("Perplexity response_format not supported; retrying without structured JSON request");
                    var fallback = new
                    {
                        model = modelRaw,
                        messages = new[]
                        {
                            new { role = "system", content = systemContent },
                            new { role = "user", content = prompt }
                        },
                        temperature = temp,
                        max_tokens = 2000,
                        stream = false
                    };
                    response = await SendAsync(fallback, cancellationToken);
                }

                // request JSON already logged inside SendAsync when debug is enabled

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Error($"Perplexity API error: {response.StatusCode}");
                    var errBody = response.Content ?? string.Empty;
                    if (!string.IsNullOrEmpty(errBody))
                    {
                        var snippet = errBody.Substring(0, Math.Min(errBody.Length, 500));
                        _logger.Debug($"Perplexity API error body (truncated): {snippet}");
                    }
                    return new List<Recommendation>();
                }

                string content = null;
                PerplexityResponse responseData = null;
                try
                {
                    responseData = JsonConvert.DeserializeObject<PerplexityResponse>(response.Content);
                    content = responseData?.Choices?.FirstOrDefault()?.Message?.Content;
                }
                catch
                {
                    var parsed = RecommendationJsonParser.Parse(response.Content ?? string.Empty, _logger);
                    if (parsed.Count > 0) return parsed;
                }
                if (DebugFlags.ProviderPayload)
                {
                    try
                    {
                        var snippet = content?.Length > 4000 ? (content.Substring(0, 4000) + "... [truncated]") : content;
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Perplexity response content: {snippet}");
                        if (responseData?.Usage != null)
                        {
                            _logger.InfoWithCorrelation($"[Brainarr Debug] Perplexity usage: prompt={responseData.Usage.PromptTokens}, completion={responseData.Usage.CompletionTokens}, total={responseData.Usage.TotalTokens}");
                        }
                    }
                    catch { }
                }
                if (DebugFlags.ProviderPayload)
                {
                    try
                    {
                        var snippet = content?.Length > 4000 ? (content.Substring(0, 4000) + "... [truncated]") : content;
                        _logger.Info($"[Brainarr Debug] Perplexity response content: {snippet}");
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(content))
                {
                    // Fallback: parse raw body for simplified shapes used in tests/mocks
                    var fallback = RecommendationJsonParser.Parse(response.Content ?? string.Empty, _logger);
                    if (fallback.Count > 0) return fallback;
                    _logger.Warn("Empty response from Perplexity");
                    return new List<Recommendation>();
                }

                // Remove citation markers like [1] [12] which can precede JSON
                try { content = System.Text.RegularExpressions.Regex.Replace(content, @"\[\d{1,3}\]", string.Empty); } catch { }
                // Remove common code fences
                content = content.Replace("```json", string.Empty).Replace("```", string.Empty);

                var parsedResult = TryParseLenient(content);
                if (parsedResult.Count == 0)
                {
                    parsedResult = RecommendationJsonParser.Parse(content, _logger);
                }
                if (parsedResult.Count == 0)
                {
                    parsedResult = RecommendationJsonParser.Parse(response.Content ?? string.Empty, _logger);
                    if (parsedResult.Count == 0)
                    {
                        parsedResult = TryParseLenient(content);
                        if (parsedResult.Count == 0)
                        {
                            parsedResult = TryParseLenient(response.Content ?? string.Empty);
                        }
                    }
                }
                // Apply provider-level defaults
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
                _logger.Error(ex, "Error getting recommendations from Perplexity");
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

        private static void MapRec(Newtonsoft.Json.Linq.JToken it, System.Collections.Generic.List<NzbDrone.Core.ImportLists.Brainarr.Models.Recommendation> list)
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
            list.Add(new NzbDrone.Core.ImportLists.Brainarr.Models.Recommendation
            {
                Artist = artist,
                Album = album,
                Genre = genre,
                Reason = reason,
                Confidence = conf
            });
        }



        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                // Simple test with minimal prompt
                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "user", content = "Reply with 'OK'" }
                    },
                    max_tokens = 10
                };

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(JsonConvert.SerializeObject(requestBody));

                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout));
                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "perplexity",
                    logger: _logger,
                    cancellationToken: cts.Token,
                    timeoutSeconds: BrainarrConstants.TestConnectionTimeout,
                    maxRetries: 2);

                var success = response.StatusCode == System.Net.HttpStatusCode.OK;
                _logger.Info($"Perplexity connection test: {(success ? "Success" : $"Failed with {response.StatusCode}")}");

                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Perplexity connection test failed");
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
                    messages = new[] { new { role = "user", content = "Reply with 'OK'" } },
                    max_tokens = 10
                };
                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .Build();
                request.Method = HttpMethod.Post;
                request.SetContent(JsonConvert.SerializeObject(requestBody));
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);

                var response = await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                    _ => _httpClient.ExecuteAsync(request),
                    origin: "perplexity",
                    logger: _logger,
                    cancellationToken: cancellationToken,
                    timeoutSeconds: BrainarrConstants.TestConnectionTimeout,
                    maxRetries: 2);
                return response.StatusCode == System.Net.HttpStatusCode.OK;
            }
            finally
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        // Parsing is centralized in RecommendationJsonParser

        // Response models
        private class PerplexityResponse
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("model")]
            public string Model { get; set; }

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

        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _logger.Info($"Perplexity model updated to: {modelName}");
            }
        }
    }
}
