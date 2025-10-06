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
        public void Order_Invariance_Same_Hash_And_Seed()
        {
            var set1 = new[] { "b", "a", "c" };
            var set2 = new[] { "c", "b", "a" };

            var h1 = StableHash.FromComponents(set1);
            var h2 = StableHash.FromComponents(set2);

            Assert.Equal(h1.FullHash, h2.FullHash);
            Assert.Equal(h1.Seed, h2.Seed);
            Assert.Equal(3, h1.ComponentCount);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "StableHash")]
        public void Nulls_Are_Treated_As_Empty_Strings()
        {
            var withNulls = new string[] { null, "alpha", null, "beta" };
            var withEmpties = new[] { "", "alpha", "", "beta" };

            var h1 = StableHash.FromComponents(withNulls);
            var h2 = StableHash.FromComponents(withEmpties);

            Assert.Equal(h1.FullHash, h2.FullHash);
            Assert.Equal(h1.Seed, h2.Seed);
            Assert.Equal(h1.ComponentCount, h2.ComponentCount);
        }
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
