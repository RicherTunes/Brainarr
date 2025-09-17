using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Registry
{
    public sealed class ModelRegistry
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1";

        [JsonPropertyName("providers")]
        public List<ProviderDescriptor> Providers { get; set; } = new();

        public ProviderDescriptor? FindProviderBySlug(string slug) => FindProvider(slug);

        public ProviderDescriptor? FindProvider(string? identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return null;
            }

            return Providers.FirstOrDefault(p =>
                string.Equals(p.Slug, identifier, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, identifier, StringComparison.OrdinalIgnoreCase));
        }

        public sealed class ProviderDescriptor
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("slug")]
            public string Slug { get; set; } = string.Empty;

            [JsonPropertyName("endpoint")]
            public string Endpoint { get; set; } = string.Empty;

            [JsonPropertyName("auth")]
            public AuthDescriptor Auth { get; set; } = new();

            [JsonPropertyName("models")]
            public List<ModelDescriptor> Models { get; set; } = new();

            [JsonPropertyName("timeouts")]
            public TimeoutsDescriptor Timeouts { get; set; } = new();

            [JsonPropertyName("retries")]
            public RetriesDescriptor Retries { get; set; } = new();

            [JsonPropertyName("integrity")]
            public IntegrityDescriptor? Integrity { get; set; }
        }

        public sealed class AuthDescriptor
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } = "none";

            [JsonPropertyName("env")]
            public string? Env { get; set; }

            [JsonPropertyName("header")]
            public string? Header { get; set; }
        }

        public sealed class ModelDescriptor
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("label")]
            public string? Label { get; set; }

            [JsonPropertyName("context_tokens")]
            public int ContextTokens { get; set; }

            [JsonPropertyName("pricing")]
            public PricingDescriptor? Pricing { get; set; }

            [JsonPropertyName("capabilities")]
            public CapabilitiesDescriptor Capabilities { get; set; } = new();
        }

        public sealed class PricingDescriptor
        {
            [JsonPropertyName("input_per_1k")]
            public double? InputPer1k { get; set; }

            [JsonPropertyName("output_per_1k")]
            public double? OutputPer1k { get; set; }
        }

        public sealed class CapabilitiesDescriptor
        {
            [JsonPropertyName("stream")]
            public bool Stream { get; set; }

            [JsonPropertyName("json_mode")]
            public bool JsonMode { get; set; }

            [JsonPropertyName("tools")]
            public bool Tools { get; set; }

            [JsonPropertyName("tool_choice")]
            public string? ToolChoice { get; set; }
        }

        public sealed class TimeoutsDescriptor
        {
            [JsonPropertyName("connect_ms")]
            public int ConnectMs { get; set; } = 5000;

            [JsonPropertyName("request_ms")]
            public int RequestMs { get; set; } = 30000;
        }

        public sealed class RetriesDescriptor
        {
            [JsonPropertyName("max")]
            public int Max { get; set; } = 2;

            [JsonPropertyName("backoff_ms")]
            public int BackoffMs { get; set; } = 200;
        }

        public sealed class IntegrityDescriptor
        {
            [JsonPropertyName("sha256")]
            public string? Sha256 { get; set; }

            [JsonPropertyName("signature")]
            public string? Signature { get; set; }
        }
    }
}
