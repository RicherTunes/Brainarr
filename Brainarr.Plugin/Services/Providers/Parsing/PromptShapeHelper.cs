using System;
using System.Linq;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing
{
    internal static class PromptShapeHelper
    {
        private const string SystemAvoidPrefix = "[[SYSTEM_AVOID:";

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

        /// <summary>
        /// Extracts the optional <c>[[SYSTEM_AVOID:Name1|Name2|...]]</c> marker from
        /// the beginning of a prompt. Returns the cleaned prompt and parsed avoid names.
        /// </summary>
        public static SystemAvoidResult ExtractSystemAvoid(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt) || !prompt.StartsWith(SystemAvoidPrefix))
                return new SystemAvoidResult(prompt ?? string.Empty, Array.Empty<string>());

            try
            {
                var endIdx = prompt.IndexOf("]]", StringComparison.Ordinal);
                if (endIdx <= 0)
                    return new SystemAvoidResult(prompt, Array.Empty<string>());

                var marker = prompt.Substring(0, endIdx + 2);
                var inner = marker.Substring(SystemAvoidPrefix.Length, marker.Length - SystemAvoidPrefix.Length - 2);
                var cleaned = prompt.Substring(endIdx + 2).TrimStart();

                if (string.IsNullOrWhiteSpace(inner))
                    return new SystemAvoidResult(cleaned, Array.Empty<string>());

                var names = inner.Split('|').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                return new SystemAvoidResult(cleaned, names);
            }
            catch (Exception)
            {
                return new SystemAvoidResult(prompt, Array.Empty<string>());
            }
        }

        /// <summary>
        /// Builds the standard avoid instruction sentence from parsed avoid names.
        /// </summary>
        public static string BuildAvoidInstruction(string[] avoidNames)
        {
            if (avoidNames == null || avoidNames.Length == 0)
                return string.Empty;

            return " Additionally, do not recommend these entities under any circumstances: " + string.Join(", ", avoidNames) + ".";
        }
    }

    internal readonly struct SystemAvoidResult
    {
        public SystemAvoidResult(string cleanedPrompt, string[] avoidNames)
        {
            CleanedPrompt = cleanedPrompt;
            AvoidNames = avoidNames;
        }

        public string CleanedPrompt { get; }
        public string[] AvoidNames { get; }
        public int Count => AvoidNames?.Length ?? 0;
        public bool HasAvoidList => Count > 0;
    }
}
