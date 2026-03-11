using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Caching
{
    public class CacheConfiguration
    {
        public int MaxMemoryEntries { get; set; } = 1000;
        public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromMinutes(5);
        public bool EnableDistributedCache { get; set; } = false;
        public bool EnableWeakReferences { get; set; } = true;

        public static CacheConfiguration Default => new();

        public static CacheConfiguration HighPerformance => new()
        {
            MaxMemoryEntries = 5000,
            MaintenanceInterval = TimeSpan.FromMinutes(10)
        };

        public static CacheConfiguration LowMemory => new()
        {
            MaxMemoryEntries = 100,
            MaintenanceInterval = TimeSpan.FromMinutes(2),
            EnableWeakReferences = true
        };
    }
}
