using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.StructuredOutputs;
using Xunit;

namespace Brainarr.Tests.Services.Providers.StructuredOutputs
{
    [Trait("Category", "Unit")]
    public class StructuredOutputSchemasTests
    {
        [Fact]
        public void GetRecommendationResponseFormat_ReturnsJsonSchemaShape()
        {
            var fmt = StructuredOutputSchemas.GetRecommendationResponseFormat();
            fmt.Should().NotBeNull();

            // anonymous types — use reflection to validate shape
            var t = fmt.GetType();
            var typeProp = t.GetProperty("type");
            var schemaProp = t.GetProperty("json_schema");
            typeProp.Should().NotBeNull();
            schemaProp.Should().NotBeNull();
            typeProp!.GetValue(fmt).Should().Be("json_schema");

            var schema = schemaProp!.GetValue(fmt);
            schema.Should().NotBeNull();
            var schemaType = schema!.GetType();
            schemaType.GetProperty("name")!.GetValue(schema).Should().Be("MusicRecommendations");
            schemaType.GetProperty("strict")!.GetValue(schema).Should().Be(true);
            schemaType.GetProperty("schema")!.GetValue(schema).Should().NotBeNull();
        }

        [Fact]
        public void GetRecommendationResponseFormat_IsCached_ReturnsConsistentSchemaInstance()
        {
            var a = StructuredOutputSchemas.GetRecommendationResponseFormat();
            var b = StructuredOutputSchemas.GetRecommendationResponseFormat();

            // Schema sub-object should be the same cached reference between calls.
            var aSchema = a.GetType().GetProperty("json_schema")!.GetValue(a);
            var bSchema = b.GetType().GetProperty("json_schema")!.GetValue(b);
            var aInner = aSchema!.GetType().GetProperty("schema")!.GetValue(aSchema);
            var bInner = bSchema!.GetType().GetProperty("schema")!.GetValue(bSchema);
            ReferenceEquals(aInner, bInner).Should().BeTrue();
        }

        [Fact]
        public void GetRecommendationResponseFormat_SerializesToJson_WithExpectedFields()
        {
            var fmt = StructuredOutputSchemas.GetRecommendationResponseFormat();
            var json = JsonSerializer.Serialize(fmt);
            json.Should().Contain("\"type\":\"json_schema\"");
            json.Should().Contain("MusicRecommendations");
            json.Should().Contain("\"strict\":true");
        }

        [Fact]
        public void RecommendationSchemaResource_IsEmbeddedInPluginAssembly()
        {
            // Sanity: the resource the production code prefers should exist in the plugin assembly,
            // so our tests exercise the embedded-resource path rather than the minimal-fallback path.
            var asm = typeof(StructuredOutputSchemas).Assembly;
            var names = asm.GetManifestResourceNames();
            names.Should().Contain(n => n.EndsWith("RecommendationJsonSchema.json", System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
