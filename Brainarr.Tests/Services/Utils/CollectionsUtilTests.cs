using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Utils;
using Xunit;

namespace Brainarr.Tests.Services.Utils
{
    public class CollectionsUtilTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Utils")]
        public void ShuffleInPlace_IsDeterministicForSeed()
        {
            var original = Enumerable.Range(1, 8).ToList();
            var listA = original.ToList();
            var listB = original.ToList();

            CollectionsUtil.ShuffleInPlace(listA, new Random(12345));
            CollectionsUtil.ShuffleInPlace(listB, new Random(12345));

            Assert.Equal(listA, listB);
            Assert.Equal(original.OrderBy(x => x), listA.OrderBy(x => x));
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Utils")]
        public void ShuffleInPlace_AllowsEmptySequences()
        {
            var list = new List<int>();

            CollectionsUtil.ShuffleInPlace(list, new Random(42));

            Assert.Empty(list);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Utils")]
        public void ShuffleInPlace_ThrowsWhenRandomMissing()
        {
            Assert.Throws<ArgumentNullException>(() => CollectionsUtil.ShuffleInPlace(new List<int>(), null!));
        }
    }
}
