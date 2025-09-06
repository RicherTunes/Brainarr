using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.StructuredOutputs
{
    /// <summary>
    /// Provides shared JSON Schemas and request bodies for providers that support structured outputs.
    /// </summary>
    public static class StructuredOutputSchemas
    {
        private static object _recommendationSchemaObject;

        /// <summary>
        /// Returns an object suitable to pass as OpenAI/Perplexity response_format using JSON Schema.
        /// { type: "json_schema", json_schema: { name, schema, strict: true } }
        /// </summary>
        public static object GetRecommendationResponseFormat()
        {
            var schema = GetRecommendationSchemaObject();
            return new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "MusicRecommendations",
                    schema,
                    strict = true
                }
            };
        }

        private static object GetRecommendationSchemaObject()
        {
            if (_recommendationSchemaObject != null) return _recommendationSchemaObject;

            // Prefer embedded resource to avoid file path issues at runtime
            var asm = typeof(StructuredOutputSchemas).Assembly;
            try
            {
                var resourceName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("RecommendationJsonSchema.json", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(resourceName))
                {
                    using var stream = asm.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream);
                        var json = reader.ReadToEnd();
                        _recommendationSchemaObject = JsonSerializer.Deserialize<object>(json);
                        if (_recommendationSchemaObject != null)
                        {
                            return _recommendationSchemaObject;
                        }
                    }
                }
            }
            catch
            {
                // fall through to filesystem and then minimal fallback
            }

            // Secondary: attempt filesystem paths for local dev scenarios
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var candidate1 = Path.Combine(baseDir, "Brainarr.Plugin", "Configuration", "Defaults", "RecommendationJsonSchema.json");
                var path = File.Exists(candidate1)
                    ? candidate1
                    : Path.Combine("Brainarr.Plugin", "Configuration", "Defaults", "RecommendationJsonSchema.json");
                if (File.Exists(path))
                {
                    _recommendationSchemaObject = JsonSerializer.Deserialize<object>(File.ReadAllText(path));
                    if (_recommendationSchemaObject != null)
                    {
                        return _recommendationSchemaObject;
                    }
                }
            }
            catch
            {
                // ignore and use minimal schema below
            }

            // Final fallback: minimal schema to avoid provider failures if resource is unavailable
            _recommendationSchemaObject = new
            {
                name = "MusicRecommendations",
                schema = new
                {
                    type = "object",
                    additionalProperties = false,
                    properties = new
                    {
                        recommendations = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    artist = new { type = "string" },
                                    album = new { type = "string" },
                                    genre = new { type = "string" },
                                    reason = new { type = "string" },
                                    confidence = new { type = "number" }
                                },
                                required = new[] { "artist" }
                            }
                        }
                    },
                    required = new[] { "recommendations" }
                }
            };
            return _recommendationSchemaObject;
        }
    }
}
