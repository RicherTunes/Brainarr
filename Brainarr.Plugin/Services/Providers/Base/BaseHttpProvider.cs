using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;

namespace Brainarr.Plugin.Services.Providers.Base
{
    public abstract class BaseHttpProvider : IAIProvider
    {
        protected readonly IHttpClient _httpClient;
        protected readonly Logger _logger;
        protected readonly JsonSerializerOptions _jsonOptions;
        
        private readonly SemaphoreSlim _requestSemaphore;
        private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);
        
        public abstract string ProviderName { get; }
        protected abstract string ApiEndpoint { get; }
        protected abstract bool RequiresAuthentication { get; }
        
        protected BaseHttpProvider(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _requestSemaphore = new SemaphoreSlim(1, 1);
            
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }
        
        public virtual async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("Prompt cannot be empty", nameof(prompt));
            }
            
            await _requestSemaphore.WaitAsync();
            try
            {
                _logger.Debug($"[{ProviderName}] Sending request with prompt length: {prompt.Length}");
                
                var requestPayload = BuildRequestPayload(prompt);
                var request = BuildHttpRequest(requestPayload);
                
                using var cts = new CancellationTokenSource(_defaultTimeout);
                var response = await ExecuteRequestAsync(request, cts.Token);
                
                if (!response.HasHttpSuccess)
                {
                    throw new HttpRequestException($"Request failed with status {response.StatusCode}: {response.Content}");
                }
                
                var recommendations = await ParseResponseAsync(response.Content);
                
                _logger.Info($"[{ProviderName}] Successfully retrieved {recommendations.Count} recommendations");
                return recommendations;
            }
            catch (TaskCanceledException)
            {
                _logger.Error($"[{ProviderName}] Request timed out after {_defaultTimeout.TotalSeconds} seconds");
                throw new TimeoutException($"{ProviderName} request timed out");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[{ProviderName}] Failed to get recommendations");
                throw;
            }
            finally
            {
                _requestSemaphore.Release();
            }
        }
        
        public virtual async Task<bool> TestConnectionAsync()
        {
            try
            {
                _logger.Debug($"[{ProviderName}] Testing connection to {ApiEndpoint}");
                
                var testPrompt = "Recommend 1 album.";
                var recommendations = await GetRecommendationsAsync(testPrompt);
                
                var success = recommendations?.Any() == true;
                _logger.Info($"[{ProviderName}] Connection test {(success ? "succeeded" : "failed")}");
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[{ProviderName}] Connection test failed");
                return false;
            }
        }
        
        protected abstract object BuildRequestPayload(string prompt);
        
        protected abstract void ConfigureAuthentication(HttpRequestBuilder requestBuilder);
        
        protected virtual HttpRequest BuildHttpRequest(object payload)
        {
            var requestBuilder = new HttpRequestBuilder(ApiEndpoint)
                .Post()
                .SetHeader("Content-Type", "application/json")
                .SetHeader("Accept", "application/json");
            
            if (RequiresAuthentication)
            {
                ConfigureAuthentication(requestBuilder);
            }
            
            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            requestBuilder.SetContent(json);
            
            return requestBuilder.Build();
        }
        
        protected virtual async Task<HttpResponse> ExecuteRequestAsync(HttpRequest request, CancellationToken cancellationToken)
        {
            try
            {
                // Using Task.Run to avoid blocking on the synchronous Execute method
                return await Task.Run(() => _httpClient.Execute(request), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw new TaskCanceledException("Request was cancelled");
            }
        }
        
        protected virtual async Task<List<Recommendation>> ParseResponseAsync(string responseContent)
        {
            if (string.IsNullOrWhiteSpace(responseContent))
            {
                _logger.Warn($"[{ProviderName}] Received empty response");
                return new List<Recommendation>();
            }
            
            try
            {
                // Try to parse the response with provider-specific logic
                var providerResponse = await ParseProviderResponseAsync(responseContent);
                
                if (providerResponse != null)
                {
                    return providerResponse;
                }
                
                // Fallback to generic parsing if provider-specific fails
                return await GenericParseResponseAsync(responseContent);
            }
            catch (JsonException ex)
            {
                _logger.Error(ex, $"[{ProviderName}] Failed to parse JSON response");
                
                // Try extracting recommendations from raw text as last resort
                return ExtractRecommendationsFromText(responseContent);
            }
        }
        
        protected abstract Task<List<Recommendation>> ParseProviderResponseAsync(string responseContent);
        
        private async Task<List<Recommendation>> GenericParseResponseAsync(string responseContent)
        {
            try
            {
                // Try parsing as array first
                var recommendations = JsonSerializer.Deserialize<List<Recommendation>>(responseContent, _jsonOptions);
                if (recommendations != null)
                {
                    return recommendations;
                }
            }
            catch
            {
                // Not an array, continue
            }
            
            try
            {
                // Try parsing as object with recommendations property
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;
                
                // Look for common response patterns
                if (root.TryGetProperty("recommendations", out var recsElement) ||
                    root.TryGetProperty("choices", out recsElement) ||
                    root.TryGetProperty("data", out recsElement) ||
                    root.TryGetProperty("items", out recsElement) ||
                    root.TryGetProperty("results", out recsElement))
                {
                    var json = recsElement.GetRawText();
                    return JsonSerializer.Deserialize<List<Recommendation>>(json, _jsonOptions);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"[{ProviderName}] Generic parsing failed");
            }
            
            return new List<Recommendation>();
        }
        
        private List<Recommendation> ExtractRecommendationsFromText(string text)
        {
            var recommendations = new List<Recommendation>();
            
            try
            {
                // Simple pattern matching for "Artist - Album" format
                var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var line in lines)
                {
                    if (line.Contains(" - ") || line.Contains(" – "))
                    {
                        var parts = line.Split(new[] { " - ", " – " }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            var artist = CleanText(parts[0]);
                            var album = CleanText(parts[1]);
                            
                            if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(album))
                            {
                                recommendations.Add(new Recommendation
                                {
                                    Artist = artist,
                                    Album = album
                                });
                            }
                        }
                    }
                }
                
                _logger.Debug($"[{ProviderName}] Extracted {recommendations.Count} recommendations from text");
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"[{ProviderName}] Text extraction failed");
            }
            
            return recommendations;
        }
        
        private string CleanText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            
            // Remove common prefixes and clean up
            text = text.Trim();
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^\d+\.\s*", ""); // Remove numbering
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^[-*•]\s*", ""); // Remove bullets
            text = text.Replace("\"", "").Replace("'", "'").Replace(""", "").Replace(""", "");
            
            return text.Trim();
        }
        
        public virtual void Dispose()
        {
            _requestSemaphore?.Dispose();
        }
    }
}