using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.ImportLists.Brainarr.Models
{
    /// <summary>
    /// Represents the structured model registry document bundled with the plugin.
    /// </summary>
    public sealed class ModelRegistryDocument
    {
        [JsonPropertyName("$schema")]
        public string? Schema { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("generatedAt")]
        public DateTimeOffset? GeneratedAt { get; set; }

        [JsonPropertyName("providers")]
        [JsonConverter(typeof(ProvidersFlexibleConverter))]
        public Dictionary<string, ModelRegistryProvider> Providers { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Describes the models made available by a single provider.
    /// </summary>
    public sealed class ModelRegistryProvider
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("endpoint")]
        public string? Endpoint { get; set; }

        [JsonPropertyName("auth")]
        public ModelRegistryAuth? Auth { get; set; }

        [JsonPropertyName("defaultModel")]
        public string? DefaultModel { get; set; }

        [JsonPropertyName("timeouts")]
        public ModelRegistryTimeouts? Timeouts { get; set; }

        [JsonPropertyName("retries")]
        public ModelRegistryRetries? Retries { get; set; }

        [JsonPropertyName("integrity")]
        public ModelRegistryIntegrity? Integrity { get; set; }

        [JsonPropertyName("models")]
        public List<ModelRegistryEntry> Models { get; set; } = new();

        [JsonExtensionData]
        public Dictionary<string, JsonElement> AdditionalProperties { get; set; } = new();
    }

    /// <summary>
    /// Defines metadata for an individual model entry in the registry.
    /// </summary>
    public sealed class ModelRegistryEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("aliases")]
        public List<string>? Aliases { get; set; }

        [JsonPropertyName("context_tokens")]
        public int? ContextTokens { get; set; }

        [JsonPropertyName("pricing")]
        public ModelRegistryPricing? Pricing { get; set; }

        [JsonPropertyName("capabilities")]
        public ModelRegistryCapabilities? Capabilities { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, string>? Metadata { get; set; }
    }

    public sealed class ModelRegistryAuth
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("env")]
        public string? Env { get; set; }
    }

    public sealed class ModelRegistryTimeouts
    {
        [JsonPropertyName("connect_ms")]
        public int? ConnectMs { get; set; }

        [JsonPropertyName("request_ms")]
        public int? RequestMs { get; set; }
    }

    public sealed class ModelRegistryRetries
    {
        [JsonPropertyName("max")]
        public int? Max { get; set; }

        [JsonPropertyName("backoff_ms")]
        public int? BackoffMs { get; set; }
    }

    public sealed class ModelRegistryIntegrity
    {
        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }
    }

    public sealed class ModelRegistryPricing
    {
        [JsonPropertyName("input_per_1k")]
        public double? InputPer1k { get; set; }

        [JsonPropertyName("output_per_1k")]
        public double? OutputPer1k { get; set; }
    }

    public sealed class ModelRegistryCapabilities
    {
        [JsonPropertyName("stream")]
        public bool? Stream { get; set; }

        [JsonPropertyName("json_mode")]
        public bool? JsonMode { get; set; }

        [JsonPropertyName("tools")]
        public bool? Tools { get; set; }

        [JsonPropertyName("tool_choice")]
        public string? ToolChoice { get; set; }
    }


    internal sealed class ProvidersFlexibleConverter : JsonConverter<Dictionary<string, ModelRegistryProvider>>
    {
        public override Dictionary<string, ModelRegistryProvider> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var result = new Dictionary<string, ModelRegistryProvider>(StringComparer.OrdinalIgnoreCase);

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                var map = JsonSerializer.Deserialize<Dictionary<string, ModelRegistryProvider>>(ref reader, options);
                if (map != null)
                {
                    foreach (var kvp in map)
                    {
                        if (kvp.Value == null)
                        {
                            continue;
                        }

                        AddOrMerge(result, kvp.Key, kvp.Value);
                    }
                }

                return result;
            }

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                var legacy = JsonSerializer.Deserialize<List<LegacyProviderDescriptor>>(ref reader, options) ?? new List<LegacyProviderDescriptor>();

                foreach (var provider in legacy)
                {
                    var key = SelectLegacyKey(provider);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        throw new JsonException("Provider entry is missing 'slug', 'id', or 'name'.");
                    }

                    var mapped = new ModelRegistryProvider
                    {
                        Name = provider.Name ?? provider.DisplayName ?? key,
                        DisplayName = provider.DisplayName ?? provider.Name ?? key,
                        Slug = provider.Slug ?? provider.Id ?? key,
                        Models = provider.Models ?? new List<ModelRegistryEntry>()
                    };

                    if (provider.AdditionalProperties != null)
                    {
                        foreach (var extra in provider.AdditionalProperties)
                        {
                            mapped.AdditionalProperties[extra.Key] = extra.Value;
                        }
                    }

                    AddOrMerge(result, key, mapped);
                }

                return result;
            }

            throw new JsonException("Expected object or array for 'providers'.");
        }

        public override void Write(Utf8JsonWriter writer, Dictionary<string, ModelRegistryProvider> value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }

        private static string? SelectLegacyKey(LegacyProviderDescriptor provider)
        {
            if (!string.IsNullOrWhiteSpace(provider.Slug))
            {
                return provider.Slug.Trim();
            }

            if (!string.IsNullOrWhiteSpace(provider.Id))
            {
                return provider.Id.Trim();
            }

            return provider.Name?.Trim();
        }

        private static void AddOrMerge(Dictionary<string, ModelRegistryProvider> target, string key, ModelRegistryProvider provider)
        {
            var normalizedKey = NormalizeKey(key ?? provider.Slug ?? provider.Name);
            if (string.IsNullOrEmpty(normalizedKey))
            {
                throw new JsonException("Provider entry is missing a valid identifier.");
            }

            EnsureProviderDefaults(provider, key);

            if (!target.TryGetValue(normalizedKey, out var existing))
            {
                target[normalizedKey] = provider;
                return;
            }

            MergeProviders(existing, provider);
        }

        private static void EnsureProviderDefaults(ModelRegistryProvider provider, string? key)
        {
            var fallback = string.IsNullOrWhiteSpace(key) ? provider.Slug ?? provider.Name ?? provider.DisplayName : key;
            fallback = string.IsNullOrWhiteSpace(fallback) ? "provider" : fallback.Trim();

            if (string.IsNullOrWhiteSpace(provider.Slug))
            {
                provider.Slug = fallback;
            }

            if (string.IsNullOrWhiteSpace(provider.Name))
            {
                provider.Name = provider.DisplayName ?? fallback;
            }

            if (string.IsNullOrWhiteSpace(provider.DisplayName))
            {
                provider.DisplayName = provider.Name ?? fallback;
            }

            provider.Models ??= new List<ModelRegistryEntry>();
        }

        private static void MergeProviders(ModelRegistryProvider destination, ModelRegistryProvider source)
        {
            destination.Name = Prefer(destination.Name, source.Name);
            destination.DisplayName = Prefer(destination.DisplayName, source.DisplayName);
            destination.Slug = Prefer(destination.Slug, source.Slug);
            destination.Endpoint = Prefer(destination.Endpoint, source.Endpoint);
            destination.DefaultModel = Prefer(destination.DefaultModel, source.DefaultModel);
            destination.Auth = MergeAuth(destination.Auth, source.Auth);
            destination.Timeouts = MergeTimeouts(destination.Timeouts, source.Timeouts);
            destination.Retries = MergeRetries(destination.Retries, source.Retries);
            destination.Integrity = MergeIntegrity(destination.Integrity, source.Integrity);

            MergeModels(destination.Models, source.Models);
            MergeAdditionalProperties(destination.AdditionalProperties, source.AdditionalProperties);
        }

        private static void MergeModels(List<ModelRegistryEntry> destination, List<ModelRegistryEntry>? source)
        {
            if (source == null || source.Count == 0)
            {
                return;
            }

            var index = new Dictionary<string, ModelRegistryEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var model in destination)
            {
                if (!string.IsNullOrWhiteSpace(model.Id))
                {
                    index[NormalizeKey(model.Id)] = model;
                }
            }

            foreach (var incoming in source)
            {
                if (incoming == null || string.IsNullOrWhiteSpace(incoming.Id))
                {
                    continue;
                }

                var key = NormalizeKey(incoming.Id);
                if (!index.TryGetValue(key, out var existing))
                {
                    destination.Add(incoming);
                    index[key] = incoming;
                    continue;
                }

                MergeModel(existing, incoming);
            }
        }

        private static void MergeModel(ModelRegistryEntry destination, ModelRegistryEntry source)
        {
            destination.Label = Prefer(destination.Label, source.Label);
            destination.ContextTokens = MergeContextTokens(destination.ContextTokens, source.ContextTokens);
            destination.Pricing = MergePricing(destination.Pricing, source.Pricing);
            destination.Capabilities = MergeCapabilities(destination.Capabilities, source.Capabilities);

            if (source.Aliases?.Count > 0)
            {
                destination.Aliases ??= new List<string>();
                foreach (var alias in source.Aliases)
                {
                    if (string.IsNullOrWhiteSpace(alias))
                    {
                        continue;
                    }

                    if (!destination.Aliases.Any(existing => string.Equals(existing, alias, StringComparison.OrdinalIgnoreCase)))
                    {
                        destination.Aliases.Add(alias);
                    }
                }
            }

            if (source.Metadata != null)
            {
                destination.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in source.Metadata)
                {
                    destination.Metadata[kvp.Key] = kvp.Value;
                }
            }
        }

        private static int? MergeContextTokens(int? current, int? incoming)
        {
            if (incoming.HasValue && incoming.Value > 0 && (!current.HasValue || incoming.Value > current.Value))
            {
                return incoming;
            }

            return current;
        }

        private static ModelRegistryPricing? MergePricing(ModelRegistryPricing? current, ModelRegistryPricing? incoming)
        {
            if (incoming == null)
            {
                return current;
            }

            current ??= new ModelRegistryPricing();
            current.InputPer1k ??= incoming.InputPer1k;
            current.OutputPer1k ??= incoming.OutputPer1k;
            return current;
        }

        private static ModelRegistryCapabilities? MergeCapabilities(ModelRegistryCapabilities? current, ModelRegistryCapabilities? incoming)
        {
            if (incoming == null)
            {
                return current;
            }

            current ??= new ModelRegistryCapabilities();
            current.Stream ??= incoming.Stream;
            current.JsonMode ??= incoming.JsonMode;
            current.Tools ??= incoming.Tools;
            current.ToolChoice ??= incoming.ToolChoice;
            return current;
        }

        private static ModelRegistryAuth? MergeAuth(ModelRegistryAuth? current, ModelRegistryAuth? incoming)
        {
            if (incoming == null)
            {
                return current;
            }

            current ??= new ModelRegistryAuth();
            current.Type ??= incoming.Type;
            current.Env ??= incoming.Env;
            return current;
        }

        private static ModelRegistryTimeouts? MergeTimeouts(ModelRegistryTimeouts? current, ModelRegistryTimeouts? incoming)
        {
            if (incoming == null)
            {
                return current;
            }

            current ??= new ModelRegistryTimeouts();
            current.ConnectMs ??= incoming.ConnectMs;
            current.RequestMs ??= incoming.RequestMs;
            return current;
        }

        private static ModelRegistryRetries? MergeRetries(ModelRegistryRetries? current, ModelRegistryRetries? incoming)
        {
            if (incoming == null)
            {
                return current;
            }

            current ??= new ModelRegistryRetries();
            current.Max ??= incoming.Max;
            current.BackoffMs ??= incoming.BackoffMs;
            return current;
        }

        private static ModelRegistryIntegrity? MergeIntegrity(ModelRegistryIntegrity? current, ModelRegistryIntegrity? incoming)
        {
            if (incoming == null)
            {
                return current;
            }

            current ??= new ModelRegistryIntegrity();
            current.Sha256 ??= incoming.Sha256;
            return current;
        }

        private static void MergeAdditionalProperties(Dictionary<string, JsonElement> destination, Dictionary<string, JsonElement> source)
        {
            foreach (var kvp in source)
            {
                if (!destination.ContainsKey(kvp.Key))
                {
                    destination[kvp.Key] = kvp.Value;
                }
            }
        }

        private static string NormalizeKey(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }

        private static string? Prefer(string? current, string? incoming)
        {
            return string.IsNullOrWhiteSpace(current) ? incoming : current;
        }
    }


    internal sealed class LegacyProviderDescriptor
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("models")]
        public List<ModelRegistryEntry>? Models { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }
}
