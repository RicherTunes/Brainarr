using System;
using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Caching
{
    public class CacheStatistics
    {
        public long TotalHits { get; set; }
        public long TotalMisses { get; set; }
        public double HitRatio { get; set; }
        public int MemoryCacheSize { get; set; }
        public int WeakCacheSize { get; set; }
        public double AverageAccessTime { get; set; }
        public Dictionary<CacheLevel, long> HitsByLevel { get; set; }
        public List<KeyValuePair<string, int>> TopAccessedKeys { get; set; }
        public long MemoryUsageBytes { get; set; }
        public DateTime? LastMaintenanceRun { get; set; }
    }
}
