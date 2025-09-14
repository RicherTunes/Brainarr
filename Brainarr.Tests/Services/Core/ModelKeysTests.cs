using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    public class ModelKeysTests
    {
        [Fact]
        public void ModelKey_ToString_Joins_Provider_And_Model()
        {
            var mk = new ModelKey("OpenAI", "gpt-4o");
            mk.ToString().Should().Be("OpenAI:gpt-4o");
        }

        [Fact]
        public void ModelKey_From_Normalizes_And_Trims()
        {
            var mk = ModelKey.From("  OPENAI  ", " gpt-4o ");
            mk.Provider.Should().Be("openai");
            mk.ModelId.Should().Be("gpt-4o");
        }

        [Fact]
        public void ProviderKey_ToString_Returns_Value()
        {
            var pk = new ProviderKey("Anthropic");
            pk.ToString().Should().Be("Anthropic");
        }
    }
}
