using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public sealed class LibraryProfileOptions
    {
        public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Maximum number of distinct cache keys retained. Cache is cleared on overflow.
        /// Prevents unbounded growth across long-running Lidarr instances where the
        /// caller might use many distinct (settings-shape) keys.
        /// </summary>
        public int MaxCapacity { get; set; } = 256;
    }
}
