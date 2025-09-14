using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing
{
    internal static class PromptShapeHelper
    {
        public static bool IsArtistOnly(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return false;
            var p = prompt.ToLowerInvariant();
            // Heuristics aligned with LibraryAwarePromptBuilder artist-mode instructions
            if (p.Contains("new artist recommendations")) return true;
            if (p.Contains("focus on artists")) return true;
            if (p.Contains("return exactly") && p.Contains("artist") && !p.Contains("album")) return true;
            return false;
        }
    }
}
