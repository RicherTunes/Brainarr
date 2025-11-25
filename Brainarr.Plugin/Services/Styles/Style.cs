using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Styles
{
    public record Style
    {
        public string Name { get; init; } = string.Empty;
        public List<string> Aliases { get; init; } = new();
        public string Slug { get; init; } = string.Empty;
        public List<string> Parents { get; init; } = new();
    }

    /// <summary>
    /// Alias for Style used by prompting/planning components.
    /// </summary>
    public class StyleEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public List<string> Aliases { get; set; } = new();
        public List<string> Parents { get; set; } = new();
    }
}
