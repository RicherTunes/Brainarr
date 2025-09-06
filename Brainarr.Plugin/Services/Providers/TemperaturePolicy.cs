using System;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers
{
    internal static class TemperaturePolicy
    {
        // Very lightweight heuristic for now. We can expand later (e.g., by provider/mode).
        public static double FromPrompt(string prompt, double @default)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return @default;

            // If artist-only mode detected, bias slightly lower for precision
            if (PromptShapeHelper.IsArtistOnly(prompt))
            {
                return Math.Max(0.2, Math.Min(@default, 0.7));
            }

            return @default;
        }
    }
}

