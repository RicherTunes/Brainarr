using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public sealed class LibraryProfileOptions
    {
        public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(10);
    }
}
