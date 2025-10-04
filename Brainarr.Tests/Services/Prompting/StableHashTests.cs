using System;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using Xunit;

namespace Brainarr.Tests.Services.Prompting
{
    public class StableHashTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "StableHash")]
        public void Limits_Component_Count_To_Maximum()
        {
            var components = Enumerable.Range(0, 5000).Select(i => $"component-{i}");

            var result = StableHash.Compute(components);

            Assert.Equal(4096, result.ComponentCount);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "StableHash")]
        public void Truncates_Long_Components_Deterministically()
        {
            var longComponent = new string('a', 30000);
            var truncatedComponent = new string('a', 24576);

            var resultLong = StableHash.FromComponents(longComponent);
            var resultTruncated = StableHash.FromComponents(truncatedComponent);

            Assert.Equal(resultTruncated.FullHash, resultLong.FullHash);
            Assert.Equal(resultTruncated.Seed, resultLong.Seed);
        }
    }
}
