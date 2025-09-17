using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration
{
    /// <summary>
    /// Loads the static model registry definition used to provide sensible defaults
    /// when Brainarr cannot reach provider discovery endpoints.
    /// </summary>
    public sealed class ModelRegistryLoader
    {
        private static readonly string[] _relativeCandidates =
        {
            Path.Combine("docs", "models.example.json"),
            "models.example.json"
        };

        private static readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private readonly Logger _logger;

        public ModelRegistryLoader(Logger? logger = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Loads the model registry from an explicit path.
        /// </summary>
        public Task<ModelRegistryDocument> LoadAsync(string? path, CancellationToken cancellationToken = default)
        {
            return LoadAsync(path, null, cancellationToken);
        }

        /// <summary>
        /// Loads the model registry from an explicit path or from known fallback locations.
        /// </summary>
        /// <param name="path">Absolute or relative path to the registry JSON. When null the loader searches known directories.</param>
        /// <param name="searchRoots">Optional directories that should be searched before defaults (publish layouts, etc.).</param>
        public async Task<ModelRegistryDocument> LoadAsync(
            string? path,
            IEnumerable<string>? searchRoots,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                return await LoadFromFileAsync(path!, cancellationToken).ConfigureAwait(false);
            }

            foreach (var candidate in ResolveCandidatePaths(searchRoots))
            {
                if (File.Exists(candidate))
                {
                    _logger.Debug($"ModelRegistryLoader: loading registry from '{candidate}'.");
                    return await LoadFromFileAsync(candidate, cancellationToken).ConfigureAwait(false);
                }
            }

            var resourceName = FindEmbeddedResourceName();
            if (resourceName != null)
            {
                var assembly = typeof(ModelRegistryLoader).Assembly;
                await using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    _logger.Debug($"ModelRegistryLoader: loading embedded registry '{resourceName}'.");
                    return await DeserializeAsync(stream, cancellationToken).ConfigureAwait(false);
                }
            }

            throw new FileNotFoundException("Model registry not found. Provide a custom path or ensure docs/models.example.json ships with the plugin.");
        }

        private IEnumerable<string> ResolveCandidatePaths(IEnumerable<string>? searchRoots)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (searchRoots != null)
            {
                foreach (var root in searchRoots)
                {
                    if (TryNormalizeDirectory(root, out var normalized) && seen.Add(normalized))
                    {
                        foreach (var relative in _relativeCandidates)
                        {
                            yield return Path.Combine(normalized, relative);
                        }
                    }
                }
            }

            if (TryNormalizeDirectory(AppContext.BaseDirectory, out var baseDir) && seen.Add(baseDir))
            {
                foreach (var relative in _relativeCandidates)
                {
                    yield return Path.Combine(baseDir, relative);
                }
            }

            var assemblyLocation = typeof(ModelRegistryLoader).Assembly.Location;
            var assemblyDir = Path.GetDirectoryName(assemblyLocation);
            if (TryNormalizeDirectory(assemblyDir, out var asmDir) && seen.Add(asmDir))
            {
                foreach (var relative in _relativeCandidates)
                {
                    yield return Path.Combine(asmDir, relative);
                }
            }
        }

        private static bool TryNormalizeDirectory(string? path, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                normalized = Path.GetFullPath(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<ModelRegistryDocument> LoadFromFileAsync(string fullPath, CancellationToken cancellationToken)
        {
            var resolved = Path.GetFullPath(fullPath);
            await using var stream = File.Open(resolved, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await DeserializeAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<ModelRegistryDocument> DeserializeAsync(Stream stream, CancellationToken cancellationToken)
        {
            var document = await JsonSerializer.DeserializeAsync<ModelRegistryDocument>(stream, _serializerOptions, cancellationToken).ConfigureAwait(false);
            if (document == null)
            {
                throw new InvalidDataException("Model registry JSON was empty.");
            }

            document.Normalize();
            return document;
        }

        private static string? FindEmbeddedResourceName()
        {
            var assembly = typeof(ModelRegistryLoader).Assembly;
            const string suffix = ".docs.models.example.json";
            return assembly
                .GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }
    }

    public sealed class ModelRegistryDocument
    {
        [JsonPropertyName("$schema")]
        public string? Schema { get; set; }

        [JsonPropertyName("generatedAt")]
        public DateTimeOffset? GeneratedAt { get; set; }

        [JsonPropertyName("providers")]
        public Dictionary<string, ProviderModelRegistry>? Providers { get; set; }

        public void Normalize()
        {
            Providers = Providers == null
                ? new Dictionary<string, ProviderModelRegistry>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, ProviderModelRegistry>(Providers, StringComparer.OrdinalIgnoreCase);

            foreach (var provider in Providers.Values)
            {
                provider?.Normalize();
            }
        }

        public ProviderModelRegistry? TryGetProvider(string providerId)
        {
            if (Providers == null) return null;
            if (string.IsNullOrWhiteSpace(providerId)) return null;
            Providers.TryGetValue(providerId, out var provider);
            return provider;
        }
    }

    public sealed class ProviderModelRegistry
    {
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("defaultModel")]
        public string? DefaultModel { get; set; }

        [JsonPropertyName("models")]
        public Dictionary<string, ModelRegistryEntry>? Models { get; set; }

        internal void Normalize()
        {
            Models = Models == null
                ? new Dictionary<string, ModelRegistryEntry>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, ModelRegistryEntry>(Models, StringComparer.OrdinalIgnoreCase);

            foreach (var entry in Models.Values)
            {
                entry?.Normalize();
            }
        }
    }

    public sealed class ModelRegistryEntry
    {
        [JsonPropertyName("rawId")]
        public string? RawId { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("aliases")]
        public List<string>? Aliases { get; set; }

        [JsonPropertyName("capabilities")]
        public ModelCapabilities? Capabilities { get; set; }

        [JsonPropertyName("pricing")]
        public ModelPricing? Pricing { get; set; }

        internal void Normalize()
        {
            Aliases = Aliases == null
                ? new List<string>()
                : Aliases
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Select(a => a.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }
    }

    public sealed class ModelCapabilities
    {
        [JsonPropertyName("contextWindow")]
        public int? ContextWindow { get; set; }

        [JsonPropertyName("supportsVision")]
        public bool? SupportsVision { get; set; }

        [JsonPropertyName("supportsThinking")]
        public bool? SupportsThinking { get; set; }

        [JsonPropertyName("supportsStreaming")]
        public bool? SupportsStreaming { get; set; }
    }

    public sealed class ModelPricing
    {
        [JsonPropertyName("inputPer1K")]
        public decimal? InputPer1K { get; set; }

        [JsonPropertyName("outputPer1K")]
        public decimal? OutputPer1K { get; set; }
    }
}
