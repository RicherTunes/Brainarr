using System;
using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Caching
{
    public class CacheOptions
    {
        public TimeSpan? AbsoluteExpiration { get; set; }
        public TimeSpan? SlidingExpiration { get; set; }
        public CachePriority Priority { get; set; } = CachePriority.Normal;
        public bool UseDistributedCache { get; set; } = true;
        public Dictionary<string, object> Tags { get; set; }

        public TimeSpan GetEffectiveTTL()
        {
            return SlidingExpiration ?? AbsoluteExpiration ?? TimeSpan.FromMinutes(30);
        }

        public static CacheOptions Default => new()
        {
            AbsoluteExpiration = TimeSpan.FromMinutes(30)
        };

        public static CacheOptions ShortLived => new()
        {
            AbsoluteExpiration = TimeSpan.FromMinutes(5)
        };

        public static CacheOptions LongLived => new()
        {
            AbsoluteExpiration = TimeSpan.FromHours(2),
            Priority = CachePriority.High
        };

        public static CacheOptions Sliding => new()
        {
            SlidingExpiration = TimeSpan.FromMinutes(15)
        };
    }

    public enum CachePriority
    {
        Low,
        Normal,
        High
    }

    public enum CacheLevel
    {
        Memory,
        WeakReference,
        Distributed
    }
}
