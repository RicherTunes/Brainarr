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

            // The schema is shipped as an embedded resource (see Brainarr.Plugin.csproj:
            // <EmbeddedResource Include="Configuration\Defaults\RecommendationJsonSchema.json" />),
            // so it is always present in a correctly built assembly. If the resource cannot be
            // located or deserialized, that indicates a build/packaging defect (tampered DLL,
            // ILMerge/ILRepack misconfiguration, trimming) rather than a recoverable runtime
            // condition — failing loudly is preferable to silently emitting a different schema
            // shape, which would change provider request payloads in hard-to-diagnose ways.
            var asm = typeof(StructuredOutputSchemas).Assembly;
            var resourceName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("RecommendationJsonSchema.json", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(resourceName))
            {
                throw new InvalidOperationException(
                    "Embedded resource 'RecommendationJsonSchema.json' not found in plugin assembly. " +
                    "The plugin DLL is corrupt or was built without the embedded schema resource.");
            }

            using var stream = asm.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{resourceName}' could not be opened from plugin assembly.");
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var deserialized = JsonSerializer.Deserialize<object>(json)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{resourceName}' deserialized to null; schema JSON is invalid.");

            _recommendationSchemaObject = deserialized;
            return _recommendationSchemaObject;
        }
    }
}
