using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Models
{
    /// <summary>
    /// Represents a simple value/label pair for UI dropdowns and configuration selections.
    /// </summary>
    public sealed class SelectOption
    {
        public string Value { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
