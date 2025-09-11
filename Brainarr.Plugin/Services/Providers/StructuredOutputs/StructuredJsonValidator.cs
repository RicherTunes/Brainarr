using System;
using System.Text.Json;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.StructuredOutputs
{
    /// <summary>
    /// Lightweight validator/repair helper for provider JSON content prior to parsing.
    /// It checks for the expected recommendations array and can perform a one-shot
    /// repair by extracting the first JSON array from the text.
    /// </summary>
    public static class StructuredJsonValidator
    {
        public static bool IsLikelyValid(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array) return true;
                if (root.ValueKind != JsonValueKind.Object) return false;
                if (root.TryGetProperty("recommendations", out var recs) && recs.ValueKind == JsonValueKind.Array) return true;
                // accept common alternate key used in some prompts
                if (root.TryGetProperty("albums", out var albums) && albums.ValueKind == JsonValueKind.Array) return true;
                return false;
            }
            catch { return false; }
        }

        public static bool TryRepair(string text, out string repaired)
        {
            repaired = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            // Extract first JSON array between '[' and ']'
            var start = text.IndexOf('[');
            var end = text.LastIndexOf(']');
            if (start >= 0 && end > start)
            {
                repaired = text.Substring(start, end - start + 1);
                return true;
            }
            return false;
        }
    }
}
