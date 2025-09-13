#if BRAINARR_EXPERIMENTAL_CACHE
using System;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Services.Caching;
using Xunit;

namespace Brainarr.Tests.Caching
{
    public class EnhancedRecommendationCacheTests
    {
        [Fact]
        public void Can_instantiate_cache_with_defaults()
        {
            var cache = new EnhancedRecommendationCache();
            Assert.NotNull(cache);
        }
    }
}
#endif
