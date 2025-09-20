using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    internal static class CanonicalModelMapper
    {
        private static readonly Dictionary<string, string> Map = BuildMap();

        private static Dictionary<string, string> BuildMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddProvider<OpenAIModelKind>("openai", map);
            AddProvider<AnthropicModelKind>("anthropic", map);
            AddProvider<PerplexityModelKind>("perplexity", map);
            AddProvider<OpenRouterModelKind>("openrouter", map);
            AddProvider<DeepSeekModelKind>("deepseek", map);
            AddProvider<GeminiModelKind>("gemini", map);
            AddProvider<GroqModelKind>("groq", map);
            return map;
        }

        private static void AddProvider<TEnum>(string provider, Dictionary<string, string> map)
            where TEnum : Enum
        {
            foreach (var value in Enum.GetValues(typeof(TEnum)).Cast<Enum>())
            {
                var canonical = value.ToString();
                var raw = ModelIdMapper.ToRawId(provider, canonical);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var key = MakeKey(provider, raw);
                if (!map.ContainsKey(key))
                {
                    map[key] = canonical;
                }
            }
        }

        private static string MakeKey(string provider, string raw)
        {
            return $"{provider}:{raw}".ToLowerInvariant();
        }

        public static string ToCanonical(AIProvider provider, string? registryId)
        {
            return ToCanonical(provider.ToString(), registryId);
        }

        public static string ToCanonical(string? provider, string? registryId)
        {
            if (string.IsNullOrWhiteSpace(registryId))
            {
                return registryId ?? string.Empty;
            }

            var providerSlug = (provider ?? string.Empty).Trim().ToLowerInvariant();
            var key = MakeKey(providerSlug, registryId.Trim());
            if (Map.TryGetValue(key, out var canonical))
            {
                return canonical;
            }

            var sanitized = registryId.Replace('/', '_').Replace('-', '_');
            return sanitized.ToUpperInvariant();
        }
    }
}
