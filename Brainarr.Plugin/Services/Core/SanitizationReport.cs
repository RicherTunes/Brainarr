using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class SanitizationReport
    {
        public int TotalItems { get; set; }
        public int DroppedItems { get; set; }
        public int TrimmedFields { get; set; }
        public int ClampedConfidences { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }
}

