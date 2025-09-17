using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Tokens
{
    internal sealed class GeminiTokenCounter : ITokenCounter
    {
        private readonly ITokenCounter _fallback;
        private readonly HttpClient _httpClient;

        public GeminiTokenCounter()
            : this(null, null)
        {
        }

        public GeminiTokenCounter(ITokenCounter? fallback, HttpClient? httpClient)
        {
            _fallback = fallback ?? new ApproximateTokenCounter();
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<int> CountAsync(string providerSlug, string modelId, string text, CancellationToken cancellationToken)
        {
            var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return await _fallback.CountAsync(providerSlug, modelId, text, cancellationToken).ConfigureAwait(false);
            }

            var baseUrl = Environment.GetEnvironmentVariable("GEMINI_COUNT_TOKENS_URL");
            var escapedModel = Uri.EscapeDataString(modelId ?? string.Empty);
            var escapedKey = Uri.EscapeDataString(apiKey);
            var url = string.IsNullOrWhiteSpace(baseUrl)
                ? $"https://generativelanguage.googleapis.com/v1beta/models/{escapedModel}:countTokens?key={escapedKey}"
                : $"{baseUrl.TrimEnd('/')}/{escapedModel}:countTokens?key={escapedKey}";

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = text ?? string.Empty }
                        }
                    }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return await _fallback.CountAsync(providerSlug, modelId, text, cancellationToken).ConfigureAwait(false);
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (document.RootElement.TryGetProperty("totalTokens", out var totalTokensElement) && totalTokensElement.TryGetInt32(out var total))
                {
                    return total;
                }
            }
            catch
            {
                // Fall back to approximation on any failure.
            }

            return await _fallback.CountAsync(providerSlug, modelId, text, cancellationToken).ConfigureAwait(false);
        }
    }
}
