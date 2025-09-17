using System;
using System.Collections.Generic;
using System.Text.Json;
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
        public Dictionary<string, ModelRegistryProvider> Providers { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Describes the models made available by a single provider.
    /// </summary>
    public sealed class ModelRegistryProvider
    {
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("models")]
        public List<ModelRegistryEntry> Models { get; set; } = new();
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

        [JsonPropertyName("capabilities")]
        public Dictionary<string, JsonElement>? Capabilities { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, string>? Metadata { get; set; }
    }
}
