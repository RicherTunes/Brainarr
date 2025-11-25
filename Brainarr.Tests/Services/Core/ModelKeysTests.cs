using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    public class ModelKeysTests
    {
        [Fact]
        public void ModelKey_ToString_formats_provider_and_model()
        {
            var k = new ModelKey("openai", "gpt-4o-mini");
            k.ToString().Should().Be("openai:gpt-4o-mini");
        }

        [Fact]
        public void ModelKey_From_normalizes_and_trims_inputs()
        {
            var k = ModelKey.From("  OpenAI  ", "  gpt-4o-mini ");
            k.Provider.Should().Be("openai");
            k.ModelId.Should().Be("gpt-4o-mini");
        }

        [Fact]
        public void ProviderKey_ToString_returns_provider_or_empty()
        {
            var pk = new ProviderKey("lmstudio");
            pk.ToString().Should().Be("lmstudio");

            var pkEmpty = new ProviderKey(null);
            pkEmpty.ToString().Should().BeEmpty();
        }
    }
}
