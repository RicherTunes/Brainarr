using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Brainarr.Plugin.Services.Security;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Utils;
using System.Text.Json;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers
{
    internal class GeminiModelDiscovery
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
        private readonly object _cacheLock = new object();
        private readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);

        public GeminiModelDiscovery(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<SelectOption>> GetModelOptionsAsync(string apiKey, System.Threading.CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new List<SelectOption>();
            }

            var sanitizedKey = apiKey.Trim();
            var cacheKey = CreateCacheKey(sanitizedKey);
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(cacheKey, out var entry) && entry.Options.Any() && (DateTime.UtcNow - entry.CachedAt) < CacheDuration)
                {
                    return CloneOptions(entry.Options);
                }
            }

            try
            {
                var url = $"{BrainarrConstants.GeminiModelsBaseUrl}?pageSize=200&key={WebUtility.UrlEncode(sanitizedKey)}";
                var request = new HttpRequestBuilder(url)
                    .Build();
                request.Method = HttpMethod.Get;
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.ModelDetectionTimeout);

                var response = await _httpClient.ExecuteAsync(request);
                if (response == null || response.StatusCode != HttpStatusCode.OK || string.IsNullOrWhiteSpace(response.Content))
                {
                    _logger.Warn($"Gemini /models query failed: {response?.StatusCode}");
                    return CreateDefaultOptions();
                }

                using var doc = SecureJsonSerializer.ParseDocument(response.Content);
                var root = doc.RootElement;
                if (!root.TryGetProperty("models", out var modelsElement) || modelsElement.ValueKind != JsonValueKind.Array)
                {
                    _logger.Warn("Unexpected Gemini /models response shape");
                    return CreateDefaultOptions();
                }

                var options = new List<SelectOption>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var modelElement in modelsElement.EnumerateArray())
                {
                    if (!TryExtractModel(modelElement, out var value, out var label))
                    {
                        continue;
                    }

                    if (seen.Add(value))
                    {
                        options.Add(new SelectOption
                        {
                            Value = value,
                            Name = label
                        });
                    }
                }

                if (options.Count == 0)
                {
                    return options;
                }

                AppendDefaultModels(options);

                lock (_cacheLock)
                {
                    _cache[cacheKey] = new CacheEntry
                    {
                        CachedAt = DateTime.UtcNow,
                        Options = CloneOptions(options)
                    };
                }

                return options;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to enumerate Gemini models");
                return CreateDefaultOptions();
            }
        }

        private static bool TryExtractModel(JsonElement modelElement, out string value, out string label)
        {
            value = string.Empty;
            label = string.Empty;

            if (modelElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!modelElement.TryGetProperty("name", out var nameElement))
            {
                return false;
            }

            var rawName = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return false;
            }

            var modelId = rawName;
            if (modelId.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            {
                modelId = modelId.Substring("models/".Length);
            }

            if (!SupportsGenerateContent(modelElement))
            {
                return false;
            }

            if (modelElement.TryGetProperty("state", out var stateElement))
            {
                var state = stateElement.GetString();
                if (!string.IsNullOrWhiteSpace(state) && !state.Equals("STATE_ACTIVE", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            value = rawName;
            var normalized = ProviderModelNormalizer.Normalize(AIProvider.Gemini, modelId);

            var displayName = modelElement.TryGetProperty("displayName", out var displayNameElement)
                ? displayNameElement.GetString()
                : null;

            var formattedId = ModelNameFormatter.FormatModelName(modelId);
            var labelParts = new List<string>();

            if (!string.IsNullOrWhiteSpace(displayName) && !displayName.Equals(formattedId, StringComparison.OrdinalIgnoreCase))
            {
                labelParts.Add(displayName);
                labelParts.Add(formattedId);
            }
            else
            {
                labelParts.Add(formattedId);
            }

            if (!string.IsNullOrWhiteSpace(normalized) && !normalized.Equals(modelId, StringComparison.OrdinalIgnoreCase))
            {
                labelParts.Add(ModelNameFormatter.FormatEnumName(normalized));
            }

            label = string.Join(" · ", labelParts.Distinct(StringComparer.OrdinalIgnoreCase));

            return true;
        }

        private static bool SupportsGenerateContent(JsonElement modelElement)
        {
            if (!modelElement.TryGetProperty("supportedGenerationMethods", out var methodsElement) ||
                methodsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var method in methodsElement.EnumerateArray())
            {
                var value = method.GetString();
                if (string.Equals(value, "generateContent", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<SelectOption> CloneOptions(List<SelectOption> options)
        {
            return options.Select(o => new SelectOption
            {
                Value = o.Value,
                Name = o.Name
            }).ToList();
        }

        private static void AppendDefaultModels(List<SelectOption> options)
        {
            var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["gemini-2.5-pro"] = ModelNameFormatter.FormatModelName("gemini-2.5-pro"),
                ["gemini-2.5-flash"] = ModelNameFormatter.FormatModelName("gemini-2.5-flash"),
                ["gemini-2.5-flash-lite"] = ModelNameFormatter.FormatModelName("gemini-2.5-flash-lite"),
                ["gemini-2.0-flash"] = ModelNameFormatter.FormatModelName("gemini-2.0-flash"),
                ["gemini-1.5-pro"] = ModelNameFormatter.FormatModelName("gemini-1.5-pro"),
                ["gemini-1.5-flash"] = ModelNameFormatter.FormatModelName("gemini-1.5-flash"),
                ["gemini-1.5-flash-8b"] = ModelNameFormatter.FormatModelName("gemini-1.5-flash-8b")
            };

            var known = new HashSet<string>(options.Select(o => o.Value), StringComparer.OrdinalIgnoreCase);
            foreach (var option in options)
            {
                if (option.Value.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
                {
                    known.Add(option.Value.Substring("models/".Length));
                }
            }

            foreach (var kvp in defaults)
            {
                if (known.Add(kvp.Key))
                {
                    options.Add(new SelectOption
                    {
                        Value = kvp.Key,
                        Name = kvp.Value
                    });
                }
            }

            options.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        private static List<SelectOption> CreateDefaultOptions()
        {
            var options = new List<SelectOption>();
            AppendDefaultModels(options);
            return options;
        }

        private static string CreateCacheKey(string apiKey)
        {
            var normalized = (apiKey ?? string.Empty).Trim();
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(normalized);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }

        private class CacheEntry
        {
            public DateTime CachedAt { get; set; }
            public List<SelectOption> Options { get; set; } = new List<SelectOption>();
        }
    }
}
