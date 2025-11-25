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
}
